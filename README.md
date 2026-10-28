# Audio Transcription Pipeline

A simple REST API built with **ASP.NET Core Web API** that accepts an audio file, converts spoken language into text using **Whisper.net**, and returns the transcript with timestamps per segment.

The focus of this project is on engineering decisions, not on training a model.

---

## 1. Overview

```
Client ──► POST /api/transcriptions ──► Save file ──► Queue job ──► Return job ID
                                                          │
                                                          ▼
                                                  Background worker
                                                          │
                                        FFmpeg (normalize to 16 kHz mono WAV)
                                                          │
                                        Split into chunks (long files only)
                                                          │
                                           Whisper.net transcription
                                                          │
                                     Save transcript + segments to database
                                                          │
Client ◄── GET /api/transcriptions/{id} ◄──── Status: Completed
```

---

## 2. Tech Stack

| Part | Choice | Why |
|------|--------|-----|
| API | ASP.NET Core Web API (.NET 8) | Fast, well-supported, easy to host |
| Speech-to-text | Whisper.net | .NET wrapper around OpenAI Whisper, runs locally, no API cost |
| Audio conversion | FFmpeg | Handles almost any audio format reliably |
| Database | SQL Server / PostgreSQL (EF Core) | Transcripts and job tracking |
| File storage | Local disk (dev) / S3 or Azure Blob (prod) | Audio files are large and should not go in the database |
| Background jobs | `BackgroundService` + `Channel<T>` | Keeps uploads fast and processing separate |

---

## 3. Project Structure

```
TranscriptionService/
├── Controllers/
│   └── TranscriptionsController.cs     # API endpoints
├── Services/
│   ├── AudioNormalizer.cs              # Runs FFmpeg to convert audio to 16 kHz mono WAV
│   ├── AudioChunker.cs                 # Splits long audio into chunks with overlap
│   ├── WhisperTranscriber.cs           # Loads Whisper model once, transcribes WAV
│   ├── TranscriptionWorker.cs          # Background worker that processes queued jobs
│   └── JobQueue.cs                     # In-memory queue (Channel<T>)
├── Storage/
│   ├── IFileStorage.cs                 # Interface so storage can be swapped
│   └── LocalFileStorage.cs             # Saves files to disk
├── Data/
│   ├── AppDbContext.cs                 # EF Core context
│   ├── TranscriptionJob.cs             # Job entity
│   └── TranscriptSegment.cs            # Segment entity
├── Models/
│   ├── TranscriptionResponse.cs        # API response DTO
│   └── SegmentDto.cs
├── appsettings.json
└── Program.cs
```

---

## 4. Part 1 – Transcription Pipeline

### 4.1 Accepting an audio file

- `POST /api/transcriptions` accepts a multipart file upload.
- The file is validated by **size** (configurable limit, e.g. 200 MB) and **format** (checked by content type and file header, not just extension).
- The file is streamed to disk with a unique GUID-based name so uploads never overwrite each other.
- A job record is created with status `Queued` and the job ID is returned to the client immediately.

### 4.2 Transcribing speech to text

- **Whisper.net** is used to convert audio to text.
- The Whisper model (e.g. `ggml-base.bin`) is loaded **once** at startup and reused for every request. Loading the model is slow, so this avoids repeating that cost.
- Before transcription, every file is normalized with FFmpeg into the format Whisper expects: **16 kHz, mono, 16-bit PCM WAV**.

### 4.3 Timestamps per segment

Whisper.net returns results as segments, each with a start time, end time, and text. These are mapped to a simple DTO and returned as JSON:

```json
{
  "jobId": "3f1c...",
  "status": "Completed",
  "text": "Hello and welcome to the meeting. Today we are going to cover three topics.",
  "segments": [
    { "start": 0.0, "end": 3.2, "text": "Hello and welcome to the meeting." },
    { "start": 3.2, "end": 7.8, "text": "Today we are going to cover three topics." }
  ]
}
```

Timestamps are in seconds. An export endpoint can convert the same segments into SRT or VTT.

### 4.4 Handling different audio formats

- Supported inputs: WAV, MP3, M4A, FLAC, OGG, WebM.
- Instead of writing a decoder for each format, **every file goes through FFmpeg** and comes out as the same 16 kHz mono WAV.
- The transcription code only ever sees one format, which keeps it simple.
- Adding a new format only means updating the validation list.
- If FFmpeg cannot decode the file, the API returns `400 Bad Request` with a clear error.

### 4.5 Handling long audio files

- Long files are **split into chunks** (around 30 seconds to a few minutes) using FFmpeg, with a small overlap so words at the edge are not cut off. Silence detection can be used to split at natural pauses.
- Each chunk is transcribed separately, and segment timestamps are shifted by the chunk's start time so they stay correct for the full file.
- Repeated text at chunk boundaries is removed when the results are merged.
- Processing runs in the background. The client gets a job ID right away and polls `GET /api/transcriptions/{id}` for the result.
- Files are streamed to disk, not loaded into memory, so memory stays flat regardless of file size.

---

## 5. Part 2 – System Design

### 5.1 Concurrent uploads

- The upload endpoint only saves the file, creates a job with status `Queued`, and returns a job ID. All heavy work happens in a background worker.
- Whisper uses a lot of CPU, so the worker processes only a **fixed number of jobs at a time** (e.g. one per CPU core). Extra jobs wait in the queue instead of overloading the server.
- Unique file names prevent collisions, and a file size limit plus rate limiting protects against abuse.
- If traffic grows, the in-memory queue can be replaced with a message broker (RabbitMQ, Azure Service Bus) so more worker instances can be added.

### 5.2 Storing audio and transcripts

- **Audio files** → file storage (local disk in dev, S3 / Azure Blob in production), named by job ID. Old files are deleted automatically after a retention period.
- **Transcripts and job info** → SQL database:
  - `Jobs` table: job ID, status, original filename, duration, attempts, error message, created/completed time.
  - `Segments` table: job ID, start, end, text.
- Keeping segments in the database makes searching and exporting easy without re-running transcription.
- The job record only stores the file path, so the storage backend can be swapped without changing the schema.

### 5.3 Retrying and recovering failed transcriptions

- Job states: `Queued → Processing → Completed / Failed`.
- **Temporary failures** (worker crash, out of memory, FFmpeg timeout) are retried automatically a few times with a growing delay between attempts.
- **Permanent failures** (corrupt or unsupported file) are marked `Failed` immediately with the error saved.
- Because the file is saved before processing starts, a crashed worker loses nothing. On restart, jobs stuck in `Processing` for too long are put back in the queue.
- Jobs that fail all retries are kept aside so they can be inspected and retried manually.
- Chunked processing allows a long file to resume from the last finished chunk instead of starting over.

### 5.4 API design

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/transcriptions` | Upload audio, returns `202 Accepted` with job ID |
| `GET` | `/api/transcriptions/{id}` | Get job status and transcript when complete |
| `GET` | `/api/transcriptions/{id}/export?format=srt\|vtt\|txt` | Download in subtitle/text format |
| `DELETE` | `/api/transcriptions/{id}` | Delete job and its files |

- Documented with Swagger / OpenAPI.
- Secured with API keys or JWT.
- Optional webhook URL can be passed at upload so the client is notified when the job finishes instead of polling.
- Standard status codes: `400` invalid file, `413` file too large, `404` unknown job, `429` rate limited.
- Health-check endpoint (`/health`) so the service can be containerized and monitored.

---

## 6. Running Locally

### Prerequisites

- .NET 8 SDK
- FFmpeg installed and available on `PATH`
- A Whisper model file (e.g. `ggml-base.bin`) placed in the `Models/` folder

### Steps

```bash
# 1. Restore packages
dotnet restore

# 2. Apply database migrations
dotnet ef database update

# 3. Run the API
dotnet run

# 4. Open Swagger
# https://localhost:5001/swagger
```

### Example request

```bash
curl -X POST https://localhost:5001/api/transcriptions \
  -F "file=@sample.mp3"

# Response
# { "jobId": "3f1c...", "status": "Queued" }

curl https://localhost:5001/api/transcriptions/3f1c...
```

### Key NuGet packages

```
Whisper.net
Whisper.net.Runtime
Microsoft.EntityFrameworkCore
Microsoft.EntityFrameworkCore.SqlServer   (or Npgsql.EntityFrameworkCore.PostgreSQL)
Swashbuckle.AspNetCore
```

---

## 7. Mock Data

Sample audio files are placed in `samples/`. For testing without a real model, `WhisperTranscriber` can be swapped with a `MockTranscriber` (via the `ITranscriber` interface) that returns fixed segments.

---

## 8. Future Improvements

- Replace in-memory queue with RabbitMQ / Azure Service Bus for multi-instance scaling
- GPU support for faster Whisper inference
- Speaker diarization (who said what)
- Language detection and translation
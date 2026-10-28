# Audio Transcriber

A REST API in ASP.NET Core (.NET 8) that takes an audio file and returns the transcript with timestamps. Transcription is done locally with Whisper.net, so there is no external API or cost.

## How it works

```
POST /api/transcriptions -> validate and save file -> create job (Queued) -> return job id
                                                              |
                                                     background worker
                                                              |
                                          FFmpeg: convert to 16 kHz mono WAV
                                                              |
                                          split into chunks if the file is long
                                                              |
                                                     Whisper.net
                                                              |
                                          save text and segments to the database
                                                              |
GET /api/transcriptions/{id} <---------------------- status: Completed
```

The upload only saves the file and creates a job, so it returns quickly. The heavy work happens in a background worker, and the client polls for the result.

## Tech used

- ASP.NET Core Web API (.NET 8)
- Whisper.net for speech to text
- FFmpeg / ffprobe for converting audio
- SQLite with EF Core (code first). I picked SQLite because it needs no setup. The database file is created on startup. Moving to SQL Server or PostgreSQL only means changing the provider in `Program.cs`.
- `BackgroundService` + `Channel<T>` for the job queue

## Project structure

```
AudioTranscriber/
  Controllers/   API endpoints
  Services/      validation, FFmpeg, chunking, Whisper, worker, queue, export
  Storage/       IFileStorage and the local disk implementation
  Data/          DbContext and entities (TranscriptionJob, TranscriptSegment)
  Migrations/    EF Core migrations
  Models/        request and response DTOs
```

## Transcription pipeline

**Upload.** The file is checked for size (200 MB by default), extension, content type and the first bytes of the file, so a renamed text file is rejected. It is saved to disk under the job id so names never clash. Then ffprobe checks that the file can be decoded. If not, the API returns 400.

**Formats.** WAV, MP3, M4A, FLAC, OGG and WebM are supported. Every file goes through FFmpeg and becomes a 16 kHz mono WAV, which is what Whisper needs. This way the transcription code only deals with one format.

**Whisper.** The model (`ggml-base.bin`) is loaded once at startup because loading it is slow. If the file is missing it is downloaded on the first run.

**Long files.** Files longer than 120 seconds are cut into chunks. Each chunk starts 2 seconds early so words at the edge are not lost. Timestamps are shifted by the chunk start so they are correct for the whole file, and text that was heard twice in the overlap is removed when merging.

**Result.** Timestamps are in seconds:

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

## System design

**Concurrent uploads.** Whisper is CPU heavy, so the worker only runs a fixed number of jobs at a time (`MaxConcurrentJobs`). Other jobs wait in the queue. If more scale is needed, the in-memory queue can be replaced with RabbitMQ or Azure Service Bus.

**Storage.** Audio files go to disk (`uploads/`). Job info and segments go to the database. The job only stores the file path, so the storage can later be moved to S3 or Azure Blob by adding another `IFileStorage`.

**Retries and recovery.** A job goes `Queued -> Processing -> Completed / Failed`.

- Temporary errors (timeouts, crashes) are retried up to 3 times, waiting longer each time (10s, then 20s).
- If FFmpeg can't read the file, the job fails straight away because retrying won't help.
- The queue is in memory, so on startup unfinished jobs are loaded from the database and queued again.
- Failed jobs keep their error message and can be retried by hand.

## API

- `POST /api/transcriptions` - upload audio, returns 202 with the job id
- `GET /api/transcriptions/{id}` - job status and transcript
- `GET /api/transcriptions/{id}/export?format=srt|vtt|txt` - download as subtitles or text
- `POST /api/transcriptions/{id}/retry` - retry a failed job
- `DELETE /api/transcriptions/{id}` - not implemented yet (returns 501)

Errors use the standard ProblemDetails format: 400 bad file, 413 too large, 404 job not found, 409 job not in the right state.

## Running locally

You need the .NET 8 SDK and FFmpeg on your PATH. The first run downloads the Whisper model (about 150 MB). You can also put `ggml-base.bin` in `AudioTranscriber/whisper-models/` yourself.

```bash
cd AudioTranscriber
dotnet run --launch-profile http
```

Swagger is at http://localhost:5080/swagger. I used port 5080 because 5000 is taken by AirPlay on macOS.

```bash
curl -X POST http://localhost:5080/api/transcriptions -F "file=@sample.mp3"
curl http://localhost:5080/api/transcriptions/{jobId}
curl -OJ "http://localhost:5080/api/transcriptions/{jobId}/export?format=srt"
```

Settings like chunk size, retry count, upload limit and model type are in `appsettings.json`.

## Limitations

- Only one instance should run, since the queue is in memory.
- With very short chunks, Whisper can hear the overlap a bit differently in each chunk, so some repeated text can stay in the result.
- A retried job starts again from the beginning instead of the last finished chunk.
- Delete is not implemented yet.

## Future improvements

- Delete endpoint and cleanup of old audio files
- API key or JWT auth, rate limiting and a health check endpoint
- Webhook to notify the client instead of polling
- Split long files at silences
- S3 / Azure Blob storage and a message broker for more instances
- GPU support, speaker detection, translation

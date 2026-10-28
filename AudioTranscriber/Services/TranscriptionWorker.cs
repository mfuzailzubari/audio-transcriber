using AudioTranscriber.Data;
using AudioTranscriber.Storage;
using Microsoft.Extensions.Options;

namespace AudioTranscriber.Services;

// Takes job ids from the queue and transcribes them in the background
public class TranscriptionWorker : BackgroundService
{
    private readonly JobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileStorage _storage;
    private readonly AudioNormalizer _normalizer;
    private readonly AudioChunker _chunker;
    private readonly ITranscriber _transcriber;
    private readonly TranscriptionOptions _options;
    private readonly ILogger<TranscriptionWorker> _logger;

    public TranscriptionWorker(
        JobQueue queue,
        IServiceScopeFactory scopeFactory,
        IFileStorage storage,
        AudioNormalizer normalizer,
        AudioChunker chunker,
        ITranscriber transcriber,
        IOptions<TranscriptionOptions> options,
        ILogger<TranscriptionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _storage = storage;
        _normalizer = normalizer;
        _chunker = chunker;
        _transcriber = transcriber;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Transcription is CPU heavy, so only a fixed number of jobs run at once.
        // Extra jobs simply wait in the queue.
        var workerCount = _options.MaxConcurrentJobs > 0 ? _options.MaxConcurrentJobs : Environment.ProcessorCount;
        _logger.LogInformation("Starting {WorkerCount} transcription workers", workerCount);

        var workers = new List<Task>();
        for (var i = 0; i < workerCount; i++)
        {
            workers.Add(RunWorkerLoopAsync(stoppingToken));
        }

        await Task.WhenAll(workers);
    }

    private async Task RunWorkerLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid jobId;
            try
            {
                jobId = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ProcessJobAsync(jobId, stoppingToken);
            }
            catch (Exception ex)
            {
                // Never let one bad job stop the worker loop
                _logger.LogError(ex, "Unexpected error while processing job {JobId}", jobId);
            }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        // DbContext is scoped, so each job gets its own scope
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.Jobs.FindAsync(new object[] { jobId }, stoppingToken);
        if (job == null || job.Status != JobStatus.Queued)
        {
            return;
        }

        job.Status = JobStatus.Processing;
        job.StartedAt = DateTime.UtcNow;
        job.Attempts++;
        await db.SaveChangesAsync(stoppingToken);

        _logger.LogInformation("Processing job {JobId} (attempt {Attempt})", job.Id, job.Attempts);

        // Temporary files for this job (converted WAV and chunks) are kept in their own folder
        var workFolder = Path.Combine(Path.GetTempPath(), "audiotranscriber", job.Id.ToString());
        Directory.CreateDirectory(workFolder);

        try
        {
            // Whisper only accepts 16 kHz mono WAV, so every upload is converted first
            var inputPath = _storage.GetLocalPath(job.StoredFilePath);
            var wavPath = Path.Combine(workFolder, "normalized.wav");
            await _normalizer.NormalizeAsync(inputPath, wavPath, stoppingToken);

            var segments = await TranscribeFileAsync(job, wavPath, workFolder, stoppingToken);

            for (var i = 0; i < segments.Count; i++)
            {
                db.Segments.Add(new TranscriptSegment
                {
                    JobId = job.Id,
                    Index = i,
                    Start = segments[i].Start,
                    End = segments[i].End,
                    Text = segments[i].Text
                });
            }

            job.FullText = string.Join(" ", segments.Select(s => s.Text.Trim()));
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.ErrorMessage = null;
            await db.SaveChangesAsync(stoppingToken);

            _logger.LogInformation("Job {JobId} completed with {SegmentCount} segments", job.Id, segments.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Job {JobId} failed", job.Id);

            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            // The original upload is kept, only the temporary converted files are removed
            if (Directory.Exists(workFolder))
            {
                Directory.Delete(workFolder, recursive: true);
            }
        }
    }

    private async Task<List<TranscribedSegment>> TranscribeFileAsync(TranscriptionJob job, string wavPath,
        string workFolder, CancellationToken stoppingToken)
    {
        // Measure the converted file, it is the exact audio Whisper will hear
        var duration = await _normalizer.GetDurationAsync(wavPath, stoppingToken);
        job.DurationSeconds = duration;

        if (!_chunker.NeedsChunking(duration))
        {
            return await _transcriber.TranscribeAsync(wavPath, stoppingToken);
        }

        var chunks = await _chunker.SplitAsync(wavPath, duration, workFolder, stoppingToken);
        _logger.LogInformation("Job {JobId} split into {ChunkCount} chunks", job.Id, chunks.Count);

        var merged = new List<TranscribedSegment>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkSegments = await _transcriber.TranscribeAsync(chunks[i].FilePath, stoppingToken);
            SegmentMerger.AddChunk(merged, chunkSegments, chunks[i].StartSeconds);

            _logger.LogInformation("Job {JobId} chunk {Chunk}/{ChunkCount} done", job.Id, i + 1, chunks.Count);
        }

        return merged;
    }
}

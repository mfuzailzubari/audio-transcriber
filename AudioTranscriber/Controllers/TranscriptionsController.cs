using System.Text;
using AudioTranscriber.Data;
using AudioTranscriber.Models;
using AudioTranscriber.Services;
using AudioTranscriber.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscriber.Controllers;

[ApiController]
[Route("api/transcriptions")]
public class TranscriptionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly AudioFileValidator _validator;
    private readonly JobQueue _queue;
    private readonly AudioNormalizer _normalizer;
    private readonly UploadOptions _uploadOptions;
    private readonly ILogger<TranscriptionsController> _logger;

    public TranscriptionsController(
        AppDbContext db,
        IFileStorage storage,
        AudioFileValidator validator,
        JobQueue queue,
        AudioNormalizer normalizer,
        IOptions<UploadOptions> uploadOptions,
        ILogger<TranscriptionsController> logger)
    {
        _db = db;
        _storage = storage;
        _validator = validator;
        _queue = queue;
        _normalizer = normalizer;
        _uploadOptions = uploadOptions.Value;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload([FromForm] UploadTranscriptionRequest request, CancellationToken cancellationToken)
    {
        var file = request.File;
        if (file == null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "No file was uploaded.");
        }

        if (file.Length > _uploadOptions.MaxFileSizeBytes)
        {
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                detail: $"The file is larger than the {_uploadOptions.MaxFileSizeMb} MB limit.");
        }

        var error = _validator.Validate(file);
        if (error != null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: error);
        }

        var jobId = Guid.NewGuid();
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        // Name the file by job id so uploads never overwrite each other
        string storedPath;
        await using (var stream = file.OpenReadStream())
        {
            storedPath = await _storage.SaveAsync(stream, $"{jobId}{extension}", cancellationToken);
        }

        // Check FFmpeg can actually decode the file, so a broken file is rejected now instead of failing later
        double duration;
        try
        {
            duration = await _normalizer.GetDurationAsync(_storage.GetLocalPath(storedPath), cancellationToken);
        }
        catch (AudioConversionException ex)
        {
            await _storage.DeleteAsync(storedPath);
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: ex.Message);
        }

        var job = new TranscriptionJob
        {
            Id = jobId,
            Status = JobStatus.Queued,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFilePath = storedPath,
            FileSizeBytes = file.Length,
            DurationSeconds = duration
        };

        try
        {
            _db.Jobs.Add(job);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Do not leave an orphan file behind if the job could not be saved
            await _storage.DeleteAsync(storedPath);
            throw;
        }

        // Heavy work happens in the background worker, the client gets the job id right away
        await _queue.EnqueueAsync(job.Id, cancellationToken);

        _logger.LogInformation("Job {JobId} created for file {FileName}", job.Id, job.OriginalFileName);

        // The Location header points the client to the status endpoint it should poll
        return AcceptedAtAction(
            nameof(GetById),
            new { id = job.Id },
            new UploadResponse { JobId = job.Id, Status = job.Status.ToString() });
    }

    // Failed jobs are kept with their error so they can be inspected and then retried by hand
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs.FindAsync(new object[] { id }, cancellationToken);
        if (job == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: $"Job {id} was not found.");
        }

        if (job.Status != JobStatus.Failed)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                detail: $"Only failed jobs can be retried, this job is {job.Status}.");
        }

        job.Status = JobStatus.Queued;
        job.Attempts = 0;
        job.ErrorMessage = null;
        job.StartedAt = null;
        job.CompletedAt = null;
        await _db.SaveChangesAsync(cancellationToken);

        await _queue.EnqueueAsync(job.Id, cancellationToken);

        return AcceptedAtAction(
            nameof(GetById),
            new { id = job.Id },
            new UploadResponse { JobId = job.Id, Status = job.Status.ToString() });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TranscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs
            .AsNoTracking()
            .Include(j => j.Segments.OrderBy(s => s.Index))
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: $"Job {id} was not found.");
        }

        var response = new TranscriptionResponse
        {
            JobId = job.Id,
            Status = job.Status.ToString(),
            OriginalFileName = job.OriginalFileName,
            DurationSeconds = job.DurationSeconds,
            Attempts = job.Attempts,
            Error = job.ErrorMessage,
            CreatedAt = job.CreatedAt,
            CompletedAt = job.CompletedAt,
            Text = job.FullText,
            Segments = job.Segments
                .Select(s => new SegmentDto { Start = s.Start, End = s.End, Text = s.Text })
                .ToList()
        };

        return Ok(response);
    }

    [HttpGet("{id:guid}/export")]
    [Produces("text/plain", "application/x-subrip", "text/vtt")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Export(Guid id, [FromQuery] string format = "srt",
        CancellationToken cancellationToken = default)
    {
        format = format.ToLowerInvariant();
        if (!TranscriptExporter.SupportedFormats.Contains(format))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: $"Unsupported format '{format}'. Use one of: {string.Join(", ", TranscriptExporter.SupportedFormats)}.");
        }

        var job = await _db.Jobs
            .AsNoTracking()
            .Include(j => j.Segments.OrderBy(s => s.Index))
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job == null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: $"Job {id} was not found.");
        }

        if (job.Status != JobStatus.Completed)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                detail: $"The transcript is not ready yet, the job is {job.Status}.");
        }

        string content;
        string contentType;
        switch (format)
        {
            case "srt":
                content = TranscriptExporter.ToSrt(job.Segments);
                contentType = "application/x-subrip";
                break;
            case "vtt":
                content = TranscriptExporter.ToVtt(job.Segments);
                contentType = "text/vtt";
                break;
            default:
                content = TranscriptExporter.ToTxt(job.Segments);
                contentType = "text/plain";
                break;
        }

        // Name the download after the original file, e.g. meeting.mp3 -> meeting.srt
        var downloadName = $"{Path.GetFileNameWithoutExtension(job.OriginalFileName)}.{format}";

        return File(Encoding.UTF8.GetBytes(content), $"{contentType}; charset=utf-8", downloadName);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult Delete(Guid id)
    {
        // TODO: delete the job, its segments and the stored audio file
        return Problem(statusCode: StatusCodes.Status501NotImplemented, detail: "Deleting jobs is not implemented yet.");
    }
}

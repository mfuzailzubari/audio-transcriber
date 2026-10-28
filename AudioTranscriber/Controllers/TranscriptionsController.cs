using AudioTranscriber.Data;
using AudioTranscriber.Models;
using AudioTranscriber.Services;
using AudioTranscriber.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AudioTranscriber.Controllers;

[ApiController]
[Route("api/transcriptions")]
public class TranscriptionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly AudioFileValidator _validator;
    private readonly UploadOptions _uploadOptions;
    private readonly ILogger<TranscriptionsController> _logger;

    public TranscriptionsController(
        AppDbContext db,
        IFileStorage storage,
        AudioFileValidator validator,
        IOptions<UploadOptions> uploadOptions,
        ILogger<TranscriptionsController> logger)
    {
        _db = db;
        _storage = storage;
        _validator = validator;
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

        var job = new TranscriptionJob
        {
            Id = jobId,
            Status = JobStatus.Queued,
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFilePath = storedPath,
            FileSizeBytes = file.Length
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

        _logger.LogInformation("Job {JobId} created for file {FileName}", job.Id, job.OriginalFileName);

        return Accepted(new UploadResponse { JobId = job.Id, Status = job.Status.ToString() });
    }
}

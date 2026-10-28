namespace AudioTranscriber.Models;

public class UploadTranscriptionRequest
{
    public IFormFile File { get; set; } = null!;
}

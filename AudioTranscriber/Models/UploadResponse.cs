namespace AudioTranscriber.Models;

public class UploadResponse
{
    public Guid JobId { get; set; }

    public string Status { get; set; } = string.Empty;
}

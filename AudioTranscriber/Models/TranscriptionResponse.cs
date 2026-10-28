namespace AudioTranscriber.Models;

public class TranscriptionResponse
{
    public Guid JobId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public double? DurationSeconds { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? Text { get; set; }

    public List<SegmentDto> Segments { get; set; } = new();
}

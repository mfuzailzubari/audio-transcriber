namespace AudioTranscriber.Data;

public class TranscriptionJob
{
    public Guid Id { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Queued;

    public string OriginalFileName { get; set; } = string.Empty;

    // Path inside the file storage, not the file itself
    public string StoredFilePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public double? DurationSeconds { get; set; }

    public string? FullText { get; set; }

    public int Attempts { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public List<TranscriptSegment> Segments { get; set; } = new();
}

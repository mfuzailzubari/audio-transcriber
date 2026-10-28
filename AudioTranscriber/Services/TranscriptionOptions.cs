namespace AudioTranscriber.Services;

public class TranscriptionOptions
{
    public const string SectionName = "Transcription";

    // How many jobs are transcribed at the same time. 0 means one per CPU core.
    public int MaxConcurrentJobs { get; set; } = 0;

    // Files longer than this are split into chunks of this length
    public int ChunkSeconds { get; set; } = 120;

    // Each chunk starts this much earlier so words at the boundary are not cut off
    public int OverlapSeconds { get; set; } = 2;

    // How many times a job is tried before it is marked Failed
    public int MaxAttempts { get; set; } = 3;

    // Wait before the first retry; the wait doubles after each failed attempt
    public int RetryDelaySeconds { get; set; } = 10;
}

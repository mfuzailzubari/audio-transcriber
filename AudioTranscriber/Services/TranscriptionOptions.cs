namespace AudioTranscriber.Services;

public class TranscriptionOptions
{
    public const string SectionName = "Transcription";

    // How many jobs are transcribed at the same time. 0 means one per CPU core.
    public int MaxConcurrentJobs { get; set; } = 0;
}

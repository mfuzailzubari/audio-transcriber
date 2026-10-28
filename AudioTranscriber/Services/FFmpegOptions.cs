namespace AudioTranscriber.Services;

public class FFmpegOptions
{
    public const string SectionName = "FFmpeg";

    // Name or full path of the executables, the default expects them on PATH
    public string FFmpegPath { get; set; } = "ffmpeg";

    public string FFprobePath { get; set; } = "ffprobe";

    // Stop FFmpeg if it runs longer than this
    public int TimeoutSeconds { get; set; } = 600;
}

namespace AudioTranscriber.Models;

public class SegmentDto
{
    // Times are in seconds from the start of the file
    public double Start { get; set; }

    public double End { get; set; }

    public string Text { get; set; } = string.Empty;
}

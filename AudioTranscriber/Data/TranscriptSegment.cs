namespace AudioTranscriber.Data;

public class TranscriptSegment
{
    public int Id { get; set; }

    public Guid JobId { get; set; }

    // Position of the segment in the transcript
    public int Index { get; set; }

    // Start and end time in seconds from the beginning of the file
    public double Start { get; set; }

    public double End { get; set; }

    public string Text { get; set; } = string.Empty;

    public TranscriptionJob? Job { get; set; }
}

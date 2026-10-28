namespace AudioTranscriber.Services;

public interface ITranscriber
{
    // Transcribes a 16 kHz mono WAV file and returns its segments in order
    Task<List<TranscribedSegment>> TranscribeAsync(string wavFilePath, CancellationToken cancellationToken);
}

public class TranscribedSegment
{
    // Times are in seconds from the start of the file
    public double Start { get; set; }

    public double End { get; set; }

    public string Text { get; set; } = string.Empty;
}

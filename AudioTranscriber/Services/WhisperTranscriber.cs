namespace AudioTranscriber.Services;

public class WhisperTranscriber : ITranscriber
{
    public async Task<List<TranscribedSegment>> TranscribeAsync(string wavFilePath, CancellationToken cancellationToken)
    {
        // TODO: call Whisper.net here. For now just wait to simulate the processing time.
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        return new List<TranscribedSegment>();
    }
}

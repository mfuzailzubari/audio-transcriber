using Microsoft.Extensions.Options;

namespace AudioTranscriber.Services;

public class AudioChunk
{
    public string FilePath { get; set; } = string.Empty;

    // Where this chunk starts in the original file, used to shift its timestamps
    public double StartSeconds { get; set; }
}

// Splits long audio into smaller chunks so each Whisper call stays short
public class AudioChunker
{
    private readonly AudioNormalizer _normalizer;
    private readonly TranscriptionOptions _options;

    public AudioChunker(AudioNormalizer normalizer, IOptions<TranscriptionOptions> options)
    {
        _normalizer = normalizer;
        _options = options.Value;
    }

    public bool NeedsChunking(double durationSeconds)
    {
        return durationSeconds > _options.ChunkSeconds;
    }

    public async Task<List<AudioChunk>> SplitAsync(string wavPath, double durationSeconds, string outputFolder,
        CancellationToken cancellationToken)
    {
        var chunks = new List<AudioChunk>();
        var index = 0;

        for (double boundary = 0; boundary < durationSeconds; boundary += _options.ChunkSeconds)
        {
            // Start a little before the boundary so a word cut at the end of the previous chunk
            // is heard in full at the start of this one
            var chunkStart = Math.Max(0, boundary - _options.OverlapSeconds);
            var chunkLength = boundary + _options.ChunkSeconds - chunkStart;

            var chunkPath = Path.Combine(outputFolder, $"chunk_{index:D4}.wav");
            await _normalizer.ExtractSectionAsync(wavPath, chunkPath, chunkStart, chunkLength, cancellationToken);

            chunks.Add(new AudioChunk { FilePath = chunkPath, StartSeconds = chunkStart });
            index++;
        }

        return chunks;
    }
}

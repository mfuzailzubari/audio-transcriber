using Microsoft.Extensions.Options;
using Whisper.net;

namespace AudioTranscriber.Services;

// Registered as a singleton so the model is loaded only once and reused for every job
public class WhisperTranscriber : ITranscriber, IDisposable
{
    private readonly WhisperFactory _factory;
    private readonly WhisperOptions _options;
    private readonly ILogger<WhisperTranscriber> _logger;

    public WhisperTranscriber(IOptions<WhisperOptions> options, ILogger<WhisperTranscriber> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Loading the model is slow, so it happens here once instead of per request
        _logger.LogInformation("Loading Whisper model from {ModelPath}", _options.ModelPath);
        _factory = WhisperFactory.FromPath(_options.ModelPath);
    }

    public async Task<List<TranscribedSegment>> TranscribeAsync(string wavFilePath, CancellationToken cancellationToken)
    {
        // A processor is not thread safe, so each job creates its own from the shared model
        using var processor = _factory.CreateBuilder()
            .WithLanguage(_options.Language)
            .Build();

        await using var fileStream = File.OpenRead(wavFilePath);

        var segments = new List<TranscribedSegment>();
        await foreach (var result in processor.ProcessAsync(fileStream, cancellationToken))
        {
            segments.Add(new TranscribedSegment
            {
                Start = result.Start.TotalSeconds,
                End = result.End.TotalSeconds,
                Text = result.Text.Trim()
            });
        }

        return segments;
    }

    public void Dispose()
    {
        _factory.Dispose();
    }
}

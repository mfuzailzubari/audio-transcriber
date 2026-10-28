using Whisper.net.Ggml;

namespace AudioTranscriber.Services;

public static class WhisperModelDownloader
{
    // Makes sure the model file exists before the app starts accepting work
    public static async Task EnsureModelExistsAsync(WhisperOptions options, ILogger logger)
    {
        if (File.Exists(options.ModelPath))
        {
            return;
        }

        if (!options.AutoDownload)
        {
            throw new FileNotFoundException(
                $"Whisper model not found at '{options.ModelPath}'. Download it or set Whisper:AutoDownload to true.");
        }

        if (!Enum.TryParse<GgmlType>(options.ModelType, ignoreCase: true, out var modelType))
        {
            throw new InvalidOperationException($"Unknown Whisper model type '{options.ModelType}'.");
        }

        logger.LogInformation("Whisper model not found, downloading {ModelType} model to {ModelPath}...",
            modelType, options.ModelPath);

        var folder = Path.GetDirectoryName(options.ModelPath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // Download to a temp file first so a failed download does not leave a broken model behind
        var tempPath = options.ModelPath + ".download";
        await using (var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(modelType))
        await using (var fileStream = File.Create(tempPath))
        {
            await modelStream.CopyToAsync(fileStream);
        }

        File.Move(tempPath, options.ModelPath, overwrite: true);

        logger.LogInformation("Whisper model downloaded");
    }
}

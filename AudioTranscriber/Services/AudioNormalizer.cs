using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace AudioTranscriber.Services;

// Uses FFmpeg so every supported format ends up as the one format Whisper expects
public class AudioNormalizer
{
    private readonly FFmpegOptions _options;

    public AudioNormalizer(IOptions<FFmpegOptions> options)
    {
        _options = options.Value;
    }

    // Converts any audio file to 16 kHz, mono, 16-bit PCM WAV
    public async Task NormalizeAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-nostdin", "-y",
            "-i", inputPath,
            "-vn",                  // ignore any video stream (e.g. WebM)
            "-ar", "16000",         // 16 kHz
            "-ac", "1",             // mono
            "-c:a", "pcm_s16le",    // 16-bit PCM
            outputPath
        };

        var result = await RunAsync(_options.FFmpegPath, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new AudioConversionException($"FFmpeg could not convert the audio file: {LastLine(result.Error)}");
        }
    }

    // Copies part of a WAV file into a new WAV file, used to split long audio into chunks
    public async Task ExtractSectionAsync(string inputPath, string outputPath, double startSeconds, double lengthSeconds,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-nostdin", "-y",
            "-ss", startSeconds.ToString(CultureInfo.InvariantCulture),
            "-t", lengthSeconds.ToString(CultureInfo.InvariantCulture),
            "-i", inputPath,
            "-c:a", "pcm_s16le",
            outputPath
        };

        var result = await RunAsync(_options.FFmpegPath, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new AudioConversionException($"FFmpeg could not split the audio file: {LastLine(result.Error)}");
        }
    }

    // Returns the length of the audio in seconds, or throws if the file cannot be read
    public async Task<double> GetDurationAsync(string inputPath, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            inputPath
        };

        var result = await RunAsync(_options.FFprobePath, arguments, cancellationToken);

        if (result.ExitCode != 0 ||
            !double.TryParse(result.Output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        {
            throw new AudioConversionException("The audio file could not be decoded.");
        }

        return duration;
    }

    private async Task<ProcessResult> RunAsync(string fileName, List<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList handles quoting, so file paths with spaces work
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Read both streams at the same time, otherwise a full buffer can block FFmpeg
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"{fileName} did not finish within {_options.TimeoutSeconds} seconds.");
        }

        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[^1].Trim() : "unknown error";
    }

    private record ProcessResult(int ExitCode, string Output, string Error);
}

using System.Text;
using Microsoft.Extensions.Options;

namespace AudioTranscriber.Services;

public class AudioFileValidator
{
    private readonly UploadOptions _options;

    public AudioFileValidator(IOptions<UploadOptions> options)
    {
        _options = options.Value;
    }

    // Returns an error message, or null when the file is valid
    public string? Validate(IFormFile file)
    {
        if (file.Length == 0)
        {
            return "The file is empty.";
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(extension))
        {
            return $"Unsupported file type '{extension}'. Allowed: {string.Join(", ", _options.AllowedExtensions)}.";
        }

        if (!IsAllowedContentType(file.ContentType))
        {
            return $"Unsupported content type '{file.ContentType}'.";
        }

        // The extension can be renamed, so also check the first bytes of the file
        if (!HasKnownAudioHeader(file))
        {
            return "The file content does not look like a supported audio format.";
        }

        return null;
    }

    private static bool IsAllowedContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return true;
        }

        contentType = contentType.ToLowerInvariant();

        // Many clients (curl included) send octet-stream for audio files, the header check covers that case
        return contentType.StartsWith("audio/")
            || contentType == "video/webm"
            || contentType == "video/mp4"
            || contentType == "video/ogg"
            || contentType == "application/octet-stream";
    }

    private static bool HasKnownAudioHeader(IFormFile file)
    {
        var header = new byte[12];
        int read;
        using (var stream = file.OpenReadStream())
        {
            read = stream.Read(header, 0, header.Length);
        }

        if (read < 4)
        {
            return false;
        }

        var ascii = Encoding.ASCII.GetString(header, 0, read);

        // WAV: "RIFF" .... "WAVE"
        if (ascii.StartsWith("RIFF") && read >= 12 && ascii.Substring(8, 4) == "WAVE")
        {
            return true;
        }

        // MP3: ID3 tag or an MPEG frame sync
        if (ascii.StartsWith("ID3") || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0))
        {
            return true;
        }

        // M4A / MP4: "ftyp" box at offset 4
        if (read >= 8 && ascii.Substring(4, 4) == "ftyp")
        {
            return true;
        }

        // FLAC
        if (ascii.StartsWith("fLaC"))
        {
            return true;
        }

        // OGG
        if (ascii.StartsWith("OggS"))
        {
            return true;
        }

        // WebM / Matroska (EBML header)
        if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
        {
            return true;
        }

        return false;
    }
}

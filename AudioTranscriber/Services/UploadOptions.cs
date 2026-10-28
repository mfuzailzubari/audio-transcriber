namespace AudioTranscriber.Services;

public class UploadOptions
{
    public const string SectionName = "Upload";

    public int MaxFileSizeMb { get; set; } = 200;

    // Set in appsettings.json (a default here would be merged with the config list, not replaced)
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();

    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}

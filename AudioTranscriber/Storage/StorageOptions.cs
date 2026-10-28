namespace AudioTranscriber.Storage;

public class StorageOptions
{
    public const string SectionName = "Storage";

    // Folder where uploaded audio files are saved
    public string RootPath { get; set; } = "uploads";
}

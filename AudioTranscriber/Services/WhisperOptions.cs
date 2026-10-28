namespace AudioTranscriber.Services;

public class WhisperOptions
{
    public const string SectionName = "Whisper";

    // Path to the ggml model file, e.g. whisper-models/ggml-base.bin
    public string ModelPath { get; set; } = "whisper-models/ggml-base.bin";

    // Which model to download when the file is missing: Tiny, Base, Small, Medium, LargeV3...
    public string ModelType { get; set; } = "Base";

    // Download the model on startup if it is not found at ModelPath
    public bool AutoDownload { get; set; } = true;

    // Spoken language, or "auto" to let Whisper detect it
    public string Language { get; set; } = "auto";
}

namespace AudioTranscriber.Services;

// Thrown when FFmpeg cannot read the file, e.g. it is corrupt or not really audio.
// Retrying will not help, so the job should fail straight away.
public class AudioConversionException : Exception
{
    public AudioConversionException(string message) : base(message)
    {
    }
}

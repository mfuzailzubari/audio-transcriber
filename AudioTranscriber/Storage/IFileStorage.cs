namespace AudioTranscriber.Storage;

// Keeps the rest of the app independent of where files live (local disk, S3, Azure Blob)
public interface IFileStorage
{
    // Saves the stream and returns the stored path to keep in the database
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    // Returns a path on local disk that tools like FFmpeg can read
    string GetLocalPath(string storedPath);

    Task DeleteAsync(string storedPath);
}

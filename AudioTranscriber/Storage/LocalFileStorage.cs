using Microsoft.Extensions.Options;

namespace AudioTranscriber.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        // Only keep the file name so a client cannot write outside the storage folder
        var safeName = Path.GetFileName(fileName);
        var fullPath = Path.Combine(_rootPath, safeName);

        // Stream to disk instead of loading the whole file into memory
        await using var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
        await content.CopyToAsync(fileStream, cancellationToken);

        return safeName;
    }

    public string GetLocalPath(string storedPath)
    {
        return Path.Combine(_rootPath, Path.GetFileName(storedPath));
    }

    public Task DeleteAsync(string storedPath)
    {
        var fullPath = GetLocalPath(storedPath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }
}

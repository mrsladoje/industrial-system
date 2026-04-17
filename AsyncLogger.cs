namespace ProcessingSystemApp;

public class AsyncLogger
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AsyncLogger(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public async Task LogAsync(string line)
    {
        await _lock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_path, line + Environment.NewLine);
        }
        finally
        {
            _lock.Release();
        }
    }
}

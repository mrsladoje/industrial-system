namespace ProcessingSystemApp;

public class IOJobProcessor : IJobProcessor
{
    private static readonly Random _random = new();

    public Task<int> ProcessAsync(Job job, CancellationToken ct)
    {
        int delayMs = int.Parse(job.Payload);
        return Task.Run(() =>
        {
            Thread.Sleep(delayMs);
            lock (_random)
            {
                return _random.Next(0, 101);
            }
        }, ct);
    }
}

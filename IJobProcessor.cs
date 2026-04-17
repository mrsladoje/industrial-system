namespace ProcessingSystemApp;

public interface IJobProcessor
{
    Task<int> ProcessAsync(Job job, CancellationToken ct);
}

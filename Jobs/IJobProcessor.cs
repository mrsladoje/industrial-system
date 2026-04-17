using ProcessingSystemApp.Models;

namespace ProcessingSystemApp.Jobs;

public interface IJobProcessor
{
    Task<int> ProcessAsync(Job job, CancellationToken ct);
}

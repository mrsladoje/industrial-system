using ProcessingSystemApp.Models;

namespace ProcessingSystemApp.Jobs;

public class PrimeJobProcessor : IJobProcessor
{
    public Task<int> ProcessAsync(Job job, CancellationToken ct)
    {
        var parts = job.Payload.Split(',');
        int upperBound = int.Parse(parts[0]);
        int threads = Math.Clamp(int.Parse(parts[1]), 1, 8);

        return Task.Run(() =>
        {
            var opts = new ParallelOptions
            {
                MaxDegreeOfParallelism = threads,
                CancellationToken = ct
            };

            int count = 0;
            Parallel.For(0, threads, opts, worker =>
            {
                int local = 0;
                for (long n = 2 + worker; n <= upperBound; n += threads)
                    if (IsPrime(n)) local++;
                Interlocked.Add(ref count, local);
            });
            return count;
        }, ct);
    }

    private static bool IsPrime(long n)
    {
        if (n < 2) return false;
        if (n == 2) return true;
        if (n % 2 == 0) return false;
        for (long i = 3; i * i <= n; i += 2)
            if (n % i == 0) return false;
        return true;
    }
}

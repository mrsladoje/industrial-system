using System.Collections.Concurrent;
using System.Diagnostics;
using ProcessingSystemApp.Jobs;
using ProcessingSystemApp.Models;

namespace ProcessingSystemApp;

public class JobCompletedEventArgs : EventArgs
{
    public Job Job { get; }
    public int Result { get; }
    public TimeSpan Duration { get; }

    public JobCompletedEventArgs(Job job, int result, TimeSpan duration)
    {
        Job = job;
        Result = result;
        Duration = duration;
    }
}

public class JobFailedEventArgs : EventArgs
{
    public Job Job { get; }
    public string Reason { get; }
    public int Attempt { get; }
    public bool Aborted { get; }

    public JobFailedEventArgs(Job job, string reason, int attempt, bool aborted)
    {
        Job = job;
        Reason = reason;
        Attempt = attempt;
        Aborted = aborted;
    }
}

public class ProcessingSystem : IAsyncDisposable
{
    private readonly SystemConfig _config;
    private readonly AsyncLogger _logger;
    private readonly ReportGenerator _reports;

    private readonly PriorityQueue<Job, int> _queue = new();
    private readonly object _queueLock = new();
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly Dictionary<JobType, IJobProcessor> _processors;

    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _workers = new();
    private Task? _reportLoop;

    public event EventHandler<JobCompletedEventArgs>? JobCompleted;
    public event EventHandler<JobFailedEventArgs>? JobFailed;

    public ProcessingSystem(SystemConfig config, AsyncLogger logger)
    {
        _config = config;
        _logger = logger;
        _reports = new ReportGenerator(config.ReportsDirectory);
        _processors = new Dictionary<JobType, IJobProcessor>
        {
            [JobType.Prime] = new PrimeJobProcessor(),
            [JobType.IO] = new IOJobProcessor(),
        };
    }

    public void Start()
    {
        foreach (var job in _config.InitialJobs)
            Submit(job);

        for (int i = 0; i < _config.WorkerThreadCount; i++)
            _workers.Add(Task.Run(() => WorkerLoopAsync(_cts.Token)));

        _reportLoop = Task.Run(() => ReportLoopAsync(_cts.Token));
    }

    public JobHandle Submit(Job job)
    {
        lock (_queueLock)
        {
            if (_entries.TryGetValue(job.Id, out var existing))
                return existing.Handle;

            if (_queue.Count >= _config.MaxQueueSize)
                throw new InvalidOperationException("Queue is full.");

            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handle = new JobHandle { Id = job.Id, Result = tcs.Task };
            _entries[job.Id] = new Entry(job, handle, tcs);

            _queue.Enqueue(job, job.Priority);
            Monitor.Pulse(_queueLock);
            return handle;
        }
    }

    public IEnumerable<Job> GetTopJobs(int n)
    {
        lock (_queueLock)
        {
            return _queue.UnorderedItems
                .OrderBy(x => x.Priority)
                .Take(n)
                .Select(x => x.Element)
                .ToList();
        }
    }

    public Job GetJob(Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry))
            throw new KeyNotFoundException($"Job {id} not found.");
        return entry.Job;
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Entry? entry = null;

            lock (_queueLock)
            {
                while (_queue.Count == 0 && !ct.IsCancellationRequested)
                    Monitor.Wait(_queueLock);

                if (ct.IsCancellationRequested) return;

                if (_queue.TryDequeue(out var job, out _))
                    _entries.TryGetValue(job.Id, out entry);
            }

            if (entry != null)
                await RunAsync(entry, ct);
        }
    }

    private async Task RunAsync(Entry entry, CancellationToken ct)
    {
        var processor = _processors[entry.Job.Type];

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sw = Stopwatch.StartNew();

            try
            {
                var task = processor.ProcessAsync(entry.Job, attemptCts.Token);
                var winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(2), ct));
                sw.Stop();

                if (winner == task && task.IsCompletedSuccessfully)
                {
                    _reports.RecordCompleted(entry.Job.Type, sw.Elapsed);
                    JobCompleted?.Invoke(this, new JobCompletedEventArgs(entry.Job, task.Result, sw.Elapsed));
                    entry.Tcs.TrySetResult(task.Result);
                    return;
                }

                attemptCts.Cancel();
                string reason = winner != task
                    ? "timeout >2s"
                    : task.Exception?.GetBaseException().Message ?? "failed";

                _reports.RecordFailed(entry.Job.Type);
                bool aborted = attempt == 3;
                JobFailed?.Invoke(this, new JobFailedEventArgs(entry.Job, reason, attempt, aborted));

                if (aborted)
                {
                    await _logger.LogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ABORT] {entry.Job.Id}, -");
                    entry.Tcs.TrySetCanceled();
                    return;
                }
            }
            catch (Exception ex)
            {
                _reports.RecordFailed(entry.Job.Type);
                bool aborted = attempt == 3;
                JobFailed?.Invoke(this, new JobFailedEventArgs(entry.Job, ex.Message, attempt, aborted));

                if (aborted)
                {
                    await _logger.LogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ABORT] {entry.Job.Id}, -");
                    entry.Tcs.TrySetException(ex);
                    return;
                }
            }
        }
    }

    private async Task ReportLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_config.ReportIntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { return; }

            try { _reports.Generate(); }
            catch (Exception ex) { Console.Error.WriteLine($"Report error: {ex.Message}"); }
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        lock (_queueLock) Monitor.PulseAll(_queueLock);

        await Task.WhenAll(_workers);
        if (_reportLoop != null)
            await _reportLoop;

        foreach (var e in _entries.Values)
            e.Tcs.TrySetCanceled();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }

    private class Entry
    {
        public Job Job { get; }
        public JobHandle Handle { get; }
        public TaskCompletionSource<int> Tcs { get; }

        public Entry(Job job, JobHandle handle, TaskCompletionSource<int> tcs)
        {
            Job = job;
            Handle = handle;
            Tcs = tcs;
        }
    }
}

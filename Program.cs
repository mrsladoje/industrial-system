using ProcessingSystemApp;
using ProcessingSystemApp.Models;

var configPath = args.Length > 0 ? args[0] : "config.xml";
var config = SystemConfig.Load(configPath);

var logger = new AsyncLogger(config.LogFilePath);
await using var system = new ProcessingSystem(config, logger);

system.JobCompleted += (_, e) =>
{
    _ = logger.LogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [COMPLETED] {e.Job.Id}, {e.Result}");
    Console.WriteLine($"OK    {e.Job.Type,-5} {e.Job.Id} = {e.Result} ({e.Duration.TotalMilliseconds:F0} ms)");
};

system.JobFailed += (_, e) =>
{
    _ = logger.LogAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [FAILED] {e.Job.Id}, {e.Reason}");
    Console.WriteLine($"{(e.Aborted ? "ABORT" : "FAIL"),-5} {e.Job.Type,-5} {e.Job.Id} attempt {e.Attempt} ({e.Reason})");
};

system.Start();

using var producerCts = new CancellationTokenSource();
var producers = Enumerable.Range(0, config.ProducerThreadCount)
    .Select(_ => Task.Run(() => Produce(system, producerCts.Token)))
    .ToArray();

Console.WriteLine($"Running ({config.WorkerThreadCount} workers, {config.ProducerThreadCount} producers). Press ENTER to stop.");
Console.ReadLine();

producerCts.Cancel();
try { await Task.WhenAll(producers); } catch { }
await system.StopAsync();

static async Task Produce(ProcessingSystem system, CancellationToken ct)
{
    var rnd = new Random();
    while (!ct.IsCancellationRequested)
    {
        try
        {
            var type = rnd.Next(2) == 0 ? JobType.Prime : JobType.IO;
            var payload = type == JobType.Prime
                ? $"{rnd.Next(5000, 300000)},{rnd.Next(1, 12)}"
                : rnd.Next(100, 2500).ToString();
            system.Submit(new Job(type, payload, rnd.Next(1, 10)));
        }
        catch (InvalidOperationException) { }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); }

        try { await Task.Delay(rnd.Next(150, 600), ct); }
        catch (OperationCanceledException) { return; }
    }
}

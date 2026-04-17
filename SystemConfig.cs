using System.Xml.Linq;

namespace ProcessingSystemApp;

public class SystemConfig
{
    public int WorkerThreadCount { get; set; }
    public int ProducerThreadCount { get; set; }
    public int MaxQueueSize { get; set; }
    public string LogFilePath { get; set; } = "events.log";
    public string ReportsDirectory { get; set; } = "reports";
    public int ReportIntervalSeconds { get; set; } = 60;
    public List<Job> InitialJobs { get; } = new();

    public static SystemConfig Load(string path)
    {
        var root = XDocument.Load(path).Root!;
        var cfg = new SystemConfig
        {
            WorkerThreadCount = int.Parse(root.Element("WorkerThreadCount")!.Value),
            ProducerThreadCount = int.Parse(root.Element("ProducerThreadCount")!.Value),
            MaxQueueSize = int.Parse(root.Element("MaxQueueSize")!.Value),
        };

        cfg.LogFilePath = root.Element("LogFilePath")?.Value ?? cfg.LogFilePath;
        cfg.ReportsDirectory = root.Element("ReportsDirectory")?.Value ?? cfg.ReportsDirectory;
        if (int.TryParse(root.Element("ReportIntervalSeconds")?.Value, out var interval))
            cfg.ReportIntervalSeconds = interval;

        foreach (var j in root.Element("InitialJobs")?.Elements("Job") ?? Array.Empty<XElement>())
        {
            cfg.InitialJobs.Add(new Job(
                Enum.Parse<JobType>(j.Element("Type")!.Value, ignoreCase: true),
                j.Element("Payload")!.Value,
                int.Parse(j.Element("Priority")!.Value)));
        }

        return cfg;
    }
}

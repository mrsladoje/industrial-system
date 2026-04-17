using System.Collections.Concurrent;
using System.Xml.Linq;
using ProcessingSystemApp.Models;

namespace ProcessingSystemApp;

public class ReportGenerator
{
    private const int MaxReports = 10;

    private readonly ConcurrentBag<(JobType Type, TimeSpan Duration, bool Success)> _records = new();
    private readonly string _directory;

    public ReportGenerator(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public void RecordCompleted(JobType type, TimeSpan duration) =>
        _records.Add((type, duration, true));

    public void RecordFailed(JobType type) =>
        _records.Add((type, TimeSpan.Zero, false));

    public void Generate()
    {
        var snapshot = _records.ToArray();

        var completed = snapshot.Where(r => r.Success)
            .GroupBy(r => r.Type)
            .OrderBy(g => g.Key.ToString())
            .Select(g => new XElement("Entry",
                new XAttribute("Type", g.Key),
                new XAttribute("Count", g.Count())));

        var averages = snapshot.Where(r => r.Success)
            .GroupBy(r => r.Type)
            .OrderBy(g => g.Key.ToString())
            .Select(g => new XElement("Entry",
                new XAttribute("Type", g.Key),
                new XAttribute("AverageMs", g.Average(r => r.Duration.TotalMilliseconds).ToString("F2"))));

        var failed = snapshot.Where(r => !r.Success)
            .GroupBy(r => r.Type)
            .OrderBy(g => g.Key.ToString())
            .Select(g => new XElement("Entry",
                new XAttribute("Type", g.Key),
                new XAttribute("Count", g.Count())));

        var doc = new XDocument(new XElement("Report",
            new XAttribute("Generated", DateTime.Now.ToString("s")),
            new XElement("Completed", completed),
            new XElement("AverageDuration", averages),
            new XElement("Failed", failed)));

        var file = Path.Combine(_directory, $"report_{DateTime.Now:yyyyMMdd_HHmmss_fff}.xml");
        doc.Save(file);

        foreach (var old in Directory.GetFiles(_directory, "report_*.xml")
                     .OrderByDescending(f => f)
                     .Skip(MaxReports))
        {
            File.Delete(old);
        }
    }
}

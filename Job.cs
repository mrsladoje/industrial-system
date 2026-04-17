namespace ProcessingSystemApp;

public class Job
{
    public Guid Id { get; set; }
    public JobType Type { get; set; }
    public string Payload { get; set; } = "";
    public int Priority { get; set; }

    public Job() { }

    public Job(JobType type, string payload, int priority)
    {
        Id = Guid.NewGuid();
        Type = type;
        Payload = payload;
        Priority = priority;
    }
}

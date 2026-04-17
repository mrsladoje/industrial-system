namespace ProcessingSystemApp;

public class JobHandle
{
    public Guid Id { get; set; }
    public Task<int> Result { get; set; } = null!;
}

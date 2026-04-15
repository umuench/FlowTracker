namespace FlowTracker.Domain;

public sealed class TimeEntry
{
    public long Id { get; init; }
    public string UserId { get; init; } = string.Empty;
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsDeleted { get; init; }
}

namespace FlowTracker.Domain;

public sealed class BreakPolicy
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int DurationMinutes { get; init; }
    public int TargetWorkMinutes { get; init; } = 480;
    public string? EarliestStart { get; init; }
    public string? LatestStart { get; init; }
    public bool AutoApply { get; init; }
    public bool IsPaid { get; init; }
    public bool RequiresReason { get; init; }
}

namespace FlowTracker.Domain;

public sealed class RadialMenuItemDefinition
{
    public string Label { get; init; } = string.Empty;
    public string ActionKey { get; init; } = string.Empty;
    public string? ColorHex { get; init; }
    public List<RadialMenuItemDefinition> Children { get; init; } = [];
}

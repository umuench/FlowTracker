namespace FlowTracker.Services;

/// <summary>
/// Konfigurationsprofil für Reminder- und Orbital-Verhalten.
/// </summary>
public sealed record OrbitalBehaviorProfile(
    string Name,
    TimeSpan IdleThreshold,
    TimeSpan SelectionWindow,
    TimeSpan ReopenCooldown,
    TimeSpan InteractionPinWindow,
    TimeSpan PostSelectionActivityWindow,
    TimeSpan ReminderEscalationCooldown,
    bool EnableFocusSuppression)
{
    /// <summary>
    /// Zurückhaltendes Profil mit längeren Schwellen und Cooldowns.
    /// </summary>
    public static readonly OrbitalBehaviorProfile Quiet = new(
        Name: "quiet",
        IdleThreshold: TimeSpan.FromSeconds(14),
        SelectionWindow: TimeSpan.FromSeconds(10),
        ReopenCooldown: TimeSpan.FromSeconds(36),
        InteractionPinWindow: TimeSpan.FromSeconds(6),
        PostSelectionActivityWindow: TimeSpan.FromSeconds(10),
        ReminderEscalationCooldown: TimeSpan.FromSeconds(30),
        EnableFocusSuppression: true);

    /// <summary>
    /// Standardprofil mit ausgewogener Erinnerung.
    /// </summary>
    public static readonly OrbitalBehaviorProfile Balanced = new(
        Name: "balanced",
        IdleThreshold: TimeSpan.FromSeconds(8),
        SelectionWindow: TimeSpan.FromSeconds(8),
        ReopenCooldown: TimeSpan.FromSeconds(24),
        InteractionPinWindow: TimeSpan.FromSeconds(7),
        PostSelectionActivityWindow: TimeSpan.FromSeconds(10),
        ReminderEscalationCooldown: TimeSpan.FromSeconds(20),
        EnableFocusSuppression: true);

    /// <summary>
    /// Strengeres Profil mit schneller Eskalation.
    /// </summary>
    public static readonly OrbitalBehaviorProfile Strict = new(
        Name: "strict",
        IdleThreshold: TimeSpan.FromSeconds(5),
        SelectionWindow: TimeSpan.FromSeconds(8),
        ReopenCooldown: TimeSpan.FromSeconds(14),
        InteractionPinWindow: TimeSpan.FromSeconds(8),
        PostSelectionActivityWindow: TimeSpan.FromSeconds(8),
        ReminderEscalationCooldown: TimeSpan.FromSeconds(12),
        EnableFocusSuppression: false);
}

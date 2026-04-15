namespace FlowTracker.Domain;

/// <summary>
/// Persistente Benutzereinstellungen für FlowTracker.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Aktives Reminder-Profil (quiet, balanced, strict).
    /// </summary>
    public string ReminderProfile { get; set; } = "balanced";

    /// <summary>
    /// Prozessnamen, bei denen Reminder im Fokuskontext unterdrückt werden.
    /// </summary>
    public List<string> FocusSuppressedProcesses { get; set; } = [];
}

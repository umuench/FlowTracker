using FlowTracker.Domain;

namespace FlowTracker.Repositories;

/// <summary>
/// Definiert den Persistenzvertrag für Zeitbuchungen.
/// </summary>
public interface ITimeEntryRepository
{
    /// <summary>
    /// Startet eine neue Buchung und beendet ggf. eine vorher noch offene Buchung.
    /// </summary>
    Task<TimeEntry> StartTrackingAsync(string userId, string category, string description, DateTimeOffset startTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Beendet alle offenen Buchungen des Benutzers mit dem übergebenen Endzeitpunkt.
    /// </summary>
    Task StopTrackingAsync(string userId, DateTimeOffset endTimeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lädt Buchungen eines Benutzers im angegebenen Zeitfenster.
    /// </summary>
    Task<IReadOnlyList<TimeEntry>> GetEntriesAsync(string userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aktualisiert einen bestehenden Zeiteintrag.
    /// </summary>
    Task UpdateEntryAsync(TimeEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Führt ein Soft Delete auf einem Eintrag aus.
    /// </summary>
    Task DeleteEntryAsync(long id, string userId, CancellationToken cancellationToken = default);
}

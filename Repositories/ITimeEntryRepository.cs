using FlowTracker.Domain;

namespace FlowTracker.Repositories;

public interface ITimeEntryRepository
{
    Task<TimeEntry> StartTrackingAsync(string userId, string category, string description, DateTimeOffset startTimeUtc, CancellationToken cancellationToken = default);
    Task StopTrackingAsync(string userId, DateTimeOffset endTimeUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimeEntry>> GetEntriesAsync(string userId, DateTimeOffset fromUtc, DateTimeOffset toUtc, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task UpdateEntryAsync(TimeEntry entry, CancellationToken cancellationToken = default);
    Task DeleteEntryAsync(long id, string userId, CancellationToken cancellationToken = default);
}

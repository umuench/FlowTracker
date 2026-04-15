using FlowTracker.Domain;
using FlowTracker.ViewModels;

namespace FlowTracker.Tests;

public class EditableTimeEntryRowTests
{
    [Fact]
    public void TryBuildTimeEntry_WithValidValues_ReturnsTrueAndMapsFields()
    {
        var row = new EditableTimeEntryRow(new TimeEntry
        {
            Id = 7,
            UserId = "test-user",
            StartTime = DateTimeOffset.UtcNow.AddHours(-1),
            EndTime = DateTimeOffset.UtcNow,
            Category = "Meeting",
            Description = "Sync",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            IsDeleted = false
        })
        {
            StartLocalText = "2026-04-13 09:00",
            EndLocalText = "2026-04-13 10:30",
            Category = " Projekt 1 ",
            Description = " Fokuszeit "
        };

        var ok = row.TryBuildTimeEntry(out var entry);

        Assert.True(ok);
        Assert.Equal(7, entry.Id);
        Assert.Equal("test-user", entry.UserId);
        Assert.Equal("Projekt 1", entry.Category);
        Assert.Equal("Fokuszeit", entry.Description);
        Assert.NotNull(entry.EndTime);
    }

    [Fact]
    public void TryBuildTimeEntry_WithInvalidStartDate_ReturnsFalse()
    {
        var row = new EditableTimeEntryRow(new TimeEntry
        {
            Id = 1,
            UserId = "u",
            StartTime = DateTimeOffset.UtcNow,
            Category = "A",
            Description = "",
            CreatedAt = DateTimeOffset.UtcNow
        })
        {
            StartLocalText = "13.04.2026 09:00"
        };

        var ok = row.TryBuildTimeEntry(out _);

        Assert.False(ok);
    }
}

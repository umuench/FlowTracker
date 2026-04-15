using FlowTracker.Domain;
using FlowTracker.ViewModels;

namespace FlowTracker.Tests;

public class DashboardViewModelTests
{
    [Fact]
    public void CalculateRange_Tag_ReturnsOneDayWindow()
    {
        var now = new DateTimeOffset(2026, 4, 13, 15, 20, 0, TimeSpan.FromHours(2));

        var (fromUtc, toUtc) = DashboardViewModel.CalculateRange(ReportingPeriod.Tag, now);

        Assert.Equal(TimeSpan.FromDays(1), toUtc - fromUtc);
    }

    [Fact]
    public void CalculateRange_Woche_ReturnsSevenDaysWindow()
    {
        var now = new DateTimeOffset(2026, 4, 15, 8, 0, 0, TimeSpan.FromHours(2));

        var (fromUtc, toUtc) = DashboardViewModel.CalculateRange(ReportingPeriod.Woche, now);

        Assert.Equal(TimeSpan.FromDays(7), toUtc - fromUtc);
    }

    [Fact]
    public void CountWorkdays_WeekSpan_ExcludesWeekend()
    {
        var count = DashboardViewModel.CountWorkdays(
            new DateTime(2026, 4, 13), // Montag
            new DateTime(2026, 4, 20)); // Montag

        Assert.Equal(5, count);
    }

    [Fact]
    public void CalculateTargetDuration_UsesWorkdaysOnly()
    {
        var fromUtc = new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.Zero); // Montag
        var toUtc = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero); // Montag

        var target = DashboardViewModel.CalculateTargetDuration(fromUtc, toUtc, TimeSpan.FromHours(8));

        Assert.Equal(TimeSpan.FromHours(40), target);
    }

    [Fact]
    public void BuildDailyBalances_CalculatesDailyAndCumulativeBalance()
    {
        var entries = new List<TimeEntry>
        {
            new()
            {
                Id = 1,
                UserId = "u",
                StartTime = new DateTimeOffset(2026, 4, 13, 8, 0, 0, TimeSpan.Zero),
                EndTime = new DateTimeOffset(2026, 4, 13, 17, 0, 0, TimeSpan.Zero),
                Category = "Arbeit",
                Description = "",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = 2,
                UserId = "u",
                StartTime = new DateTimeOffset(2026, 4, 14, 8, 0, 0, TimeSpan.Zero),
                EndTime = new DateTimeOffset(2026, 4, 14, 15, 0, 0, TimeSpan.Zero),
                Category = "Arbeit",
                Description = "",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        var rows = DashboardViewModel.BuildDailyBalances(
            entries,
            new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero),
            TimeSpan.FromHours(8));

        Assert.Equal(2, rows.Count);
        Assert.Equal(TimeSpan.FromHours(1), rows[0].Balance);
        Assert.Equal(TimeSpan.FromHours(-1), rows[1].Balance);
        Assert.Equal(TimeSpan.Zero, rows[1].Cumulative);
    }
}

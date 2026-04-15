using FlowTracker.Domain;
using FlowTracker.Services;

namespace FlowTracker.Tests;

public class WorkStateMachineTests
{
    private static WorkRuleContext CreateDefaultContext(DateOnly today) => new(
        Today: today,
        StateDay: today,
        HasStartedToday: false,
        FixedBreakStartedAtUtc: null,
        MinFixedBreakDuration: TimeSpan.FromMinutes(30),
        AllowEndDuringBreak: true,
        CurrentTimestampUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void StartWork_FromOffDuty_IsAllowed()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = WorkStateMachine.Transition(WorkSessionState.OffDuty, WorkAction.StartWork, CreateDefaultContext(today));
        Assert.True(result.IsAllowed);
        Assert.Equal(WorkSessionState.Working, result.NextState);
    }

    [Fact]
    public void StartWork_WhenAlreadyWorking_IsBlocked()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = WorkStateMachine.Transition(WorkSessionState.Working, WorkAction.StartWork, CreateDefaultContext(today));
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void EndWork_FromBreak_IsAllowedAndEndsDay()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = WorkStateMachine.Transition(WorkSessionState.BreakFixed, WorkAction.EndWork, CreateDefaultContext(today));
        Assert.True(result.IsAllowed);
        Assert.Equal(WorkSessionState.Ended, result.NextState);
    }

    [Fact]
    public void StartWork_WhenAlreadyStartedToday_IsBlocked()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var context = CreateDefaultContext(today) with { HasStartedToday = true };
        var result = WorkStateMachine.Transition(WorkSessionState.OffDuty, WorkAction.StartWork, context);
        Assert.False(result.IsAllowed);
        Assert.Contains("bereits gesetzt", result.Message);
    }

    [Fact]
    public void ResumeWork_FromFixedBreakBeforeMinimum_IsBlocked()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.LocalDateTime.Date);
        var context = new WorkRuleContext(
            Today: today,
            StateDay: today,
            HasStartedToday: true,
            FixedBreakStartedAtUtc: now.AddMinutes(-10),
            MinFixedBreakDuration: TimeSpan.FromMinutes(30),
            AllowEndDuringBreak: true,
            CurrentTimestampUtc: now);

        var result = WorkStateMachine.Transition(WorkSessionState.BreakFixed, WorkAction.ResumeWork, context);
        Assert.False(result.IsAllowed);
        Assert.Contains("Mittagspause", result.Message);
    }

    [Fact]
    public void ResumeWork_FromFixedBreakAfterMinimum_IsAllowed()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.LocalDateTime.Date);
        var context = new WorkRuleContext(
            Today: today,
            StateDay: today,
            HasStartedToday: true,
            FixedBreakStartedAtUtc: now.AddMinutes(-35),
            MinFixedBreakDuration: TimeSpan.FromMinutes(30),
            AllowEndDuringBreak: true,
            CurrentTimestampUtc: now);

        var result = WorkStateMachine.Transition(WorkSessionState.BreakFixed, WorkAction.ResumeWork, context);
        Assert.True(result.IsAllowed);
        Assert.Equal(WorkSessionState.Working, result.NextState);
    }

    [Fact]
    public void StartWork_AfterEndedSameDay_IsAllowed()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var context = CreateDefaultContext(today) with { HasStartedToday = true };
        var result = WorkStateMachine.Transition(WorkSessionState.Ended, WorkAction.StartWork, context);
        Assert.True(result.IsAllowed);
        Assert.Equal(WorkSessionState.Working, result.NextState);
    }

    [Fact]
    public void SwitchContext_WhileWorking_IsAllowed()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = WorkStateMachine.Transition(WorkSessionState.Working, WorkAction.SwitchContext, CreateDefaultContext(today));
        Assert.True(result.IsAllowed);
        Assert.Equal(WorkSessionState.Working, result.NextState);
    }
}

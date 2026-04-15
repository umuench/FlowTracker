using FlowTracker.Domain;

namespace FlowTracker.Services;

public static class WorkStateMachine
{
    public static TransitionResult Transition(WorkSessionState currentState, WorkAction action, DateOnly stateDay, DateOnly today)
    {
        var context = new WorkRuleContext(
            Today: today,
            StateDay: stateDay,
            HasStartedToday: currentState is not WorkSessionState.OffDuty,
            FixedBreakStartedAtUtc: null,
            MinFixedBreakDuration: TimeSpan.FromMinutes(30),
            AllowEndDuringBreak: true,
            CurrentTimestampUtc: DateTimeOffset.UtcNow);

        return Transition(currentState, action, context);
    }

    public static TransitionResult Transition(WorkSessionState currentState, WorkAction action, WorkRuleContext context)
    {
        if (context.StateDay != context.Today && currentState == WorkSessionState.Ended)
        {
            currentState = WorkSessionState.OffDuty;
        }

        var allowedActions = GetAllowedActions(currentState, context);
        if (!allowedActions.Contains(action))
        {
            return TransitionResult.Invalid(BuildBlockedMessage(currentState, action, allowedActions, context));
        }

        return (currentState, action) switch
        {
            (WorkSessionState.OffDuty, WorkAction.StartWork) =>
                TransitionResult.Ok(WorkSessionState.Working, "Arbeitsbeginn gesetzt"),

            (WorkSessionState.Ended, WorkAction.StartWork) =>
                TransitionResult.Ok(WorkSessionState.Working, "Arbeitstag erneut gestartet"),

            (WorkSessionState.Working, WorkAction.StartFlexibleBreak) =>
                TransitionResult.Ok(WorkSessionState.BreakFlexible, "Flexible Pause gestartet"),

            (WorkSessionState.Working, WorkAction.StartFixedBreak) =>
                TransitionResult.Ok(WorkSessionState.BreakFixed, "Feste Pause gestartet"),

            (WorkSessionState.BreakFlexible, WorkAction.ResumeWork) or
            (WorkSessionState.BreakFixed, WorkAction.ResumeWork) =>
                TransitionResult.Ok(WorkSessionState.Working, "Arbeit fortgesetzt"),

            (WorkSessionState.Working, WorkAction.SwitchContext) =>
                TransitionResult.Ok(WorkSessionState.Working, "Tätigkeit gewechselt"),

            (WorkSessionState.Working, WorkAction.EndWork) =>
                TransitionResult.Ok(WorkSessionState.Ended, "Arbeitsende gesetzt"),

            (WorkSessionState.BreakFlexible, WorkAction.EndWork) or
            (WorkSessionState.BreakFixed, WorkAction.EndWork) =>
                TransitionResult.Ok(WorkSessionState.Ended, "Arbeitsende gesetzt (Pause automatisch beendet)"),

            _ => TransitionResult.Invalid($"Aktion '{action}' ist im Zustand '{currentState}' nicht erlaubt")
        };
    }

    public static IReadOnlyList<WorkAction> GetAllowedActions(WorkSessionState currentState, WorkRuleContext context)
    {
        if (context.StateDay != context.Today && currentState == WorkSessionState.Ended)
        {
            currentState = WorkSessionState.OffDuty;
        }

        return currentState switch
        {
            WorkSessionState.OffDuty => context.HasStartedToday
                ? []
                : [WorkAction.StartWork],

            WorkSessionState.Working =>
            [
                WorkAction.SwitchContext,
                WorkAction.StartFlexibleBreak,
                WorkAction.StartFixedBreak,
                WorkAction.EndWork
            ],

            WorkSessionState.BreakFlexible => context.AllowEndDuringBreak
                ? [WorkAction.ResumeWork, WorkAction.EndWork]
                : [WorkAction.ResumeWork],

            WorkSessionState.BreakFixed => BuildFixedBreakActions(context),

            WorkSessionState.Ended => [WorkAction.StartWork],

            _ => []
        };
    }

    private static IReadOnlyList<WorkAction> BuildFixedBreakActions(WorkRuleContext context)
    {
        var canResume = !context.FixedBreakStartedAtUtc.HasValue
            || context.CurrentTimestampUtc - context.FixedBreakStartedAtUtc.Value >= context.MinFixedBreakDuration;

        if (!canResume)
        {
            return context.AllowEndDuringBreak
                ? [WorkAction.EndWork]
                : [];
        }

        return context.AllowEndDuringBreak
            ? [WorkAction.ResumeWork, WorkAction.EndWork]
            : [WorkAction.ResumeWork];
    }

    private static string BuildBlockedMessage(
        WorkSessionState currentState,
        WorkAction action,
        IReadOnlyList<WorkAction> allowedActions,
        WorkRuleContext context)
    {
        if (currentState == WorkSessionState.OffDuty && action == WorkAction.EndWork)
        {
            return "Arbeitsende ist erst nach Arbeitsbeginn möglich.";
        }

        if (currentState == WorkSessionState.OffDuty && context.HasStartedToday)
        {
            return "Arbeitsbeginn wurde heute bereits gesetzt.";
        }

        if (currentState == WorkSessionState.BreakFixed && action == WorkAction.ResumeWork && context.FixedBreakStartedAtUtc.HasValue)
        {
            var remaining = context.MinFixedBreakDuration - (context.CurrentTimestampUtc - context.FixedBreakStartedAtUtc.Value);
            if (remaining > TimeSpan.Zero)
            {
                return $"Mittagspause kann erst in {Math.Ceiling(remaining.TotalMinutes)} Minute(n) beendet werden.";
            }
        }

        if (allowedActions.Count == 0)
        {
            return "Aktion nicht möglich. Der Tag ist abgeschlossen.";
        }

        var next = string.Join(", ", allowedActions.Select(static a => ToLabel(a)));
        return $"Aktion '{ToLabel(action)}' ist gerade nicht erlaubt. Nächster sinnvoller Schritt: {next}.";
    }

    private static string ToLabel(WorkAction action) => action switch
    {
        WorkAction.StartWork => "Arbeitsbeginn",
        WorkAction.EndWork => "Arbeitsende",
        WorkAction.StartFlexibleBreak => "Pause kurz",
        WorkAction.StartFixedBreak => "Pause Mittag",
        WorkAction.ResumeWork => "Arbeit fortsetzen",
        WorkAction.SwitchContext => "Projekt/Grund wechseln",
        _ => action.ToString()
    };
}

public readonly record struct TransitionResult(bool IsAllowed, WorkSessionState NextState, string Message)
{
    public static TransitionResult Ok(WorkSessionState next, string message) => new(true, next, message);
    public static TransitionResult Invalid(string message) => new(false, default, message);
}

public readonly record struct WorkRuleContext(
    DateOnly Today,
    DateOnly StateDay,
    bool HasStartedToday,
    DateTimeOffset? FixedBreakStartedAtUtc,
    TimeSpan MinFixedBreakDuration,
    bool AllowEndDuringBreak,
    DateTimeOffset CurrentTimestampUtc);

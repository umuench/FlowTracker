namespace FlowTracker.Domain;

public enum WorkSessionState
{
    OffDuty,
    Working,
    BreakFlexible,
    BreakFixed,
    Ended
}

public enum WorkAction
{
    StartWork,
    EndWork,
    StartFlexibleBreak,
    StartFixedBreak,
    ResumeWork,
    SwitchContext
}

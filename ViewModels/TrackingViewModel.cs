using FlowTracker.Domain;
using FlowTracker.Repositories;
using FlowTracker.Services;
using System.Collections.ObjectModel;

namespace FlowTracker.ViewModels;

public sealed class TrackingViewModel(ITimeEntryRepository repository, string userId) : ViewModelBase
{
    private readonly ITimeEntryRepository _repository = repository;
    private readonly string _userId = userId;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private string _currentCategory = "Nicht gestartet";
    private string _statusText = "Bereit";
    private bool _isTracking;
    private WorkSessionState _sessionState = WorkSessionState.OffDuty;
    private DateOnly _stateDay = DateOnly.FromDateTime(DateTime.Now);
    private bool _hasStartedToday;
    private DateTimeOffset? _fixedBreakStartedAtUtc;

    public ObservableCollection<TimeEntry> Entries { get; } = [];

    public string CurrentCategory
    {
        get => _currentCategory;
        private set => SetProperty(ref _currentCategory, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsTracking
    {
        get => _isTracking;
        private set => SetProperty(ref _isTracking, value);
    }

    public WorkSessionState SessionState
    {
        get => _sessionState;
        private set => SetProperty(ref _sessionState, value);
    }

    public TimeSpan TodayLoggedDuration =>
        Entries.Where(static e => !e.IsDeleted && e.EndTime is not null)
            .Aggregate(TimeSpan.Zero, static (acc, item) => acc + (item.EndTime!.Value - item.StartTime));

    public bool NeedsContextReminder =>
        IsTracking
        && SessionState == WorkSessionState.Working
        && string.Equals(CurrentCategory, "Arbeit", StringComparison.OrdinalIgnoreCase);

    public async Task LoadTodayEntriesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        var from = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset).ToUniversalTime();
        var to = from.AddDays(1);

        var entries = await _repository.GetEntriesAsync(_userId, from, to, includeDeleted: false, cancellationToken)
            .ConfigureAwait(false);

        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }

        RaisePropertyChanged(nameof(TodayLoggedDuration));
        RebuildSessionStateFromEntries();
    }

    public async Task SelectCategoryAsync(string category, string? description = null, CancellationToken cancellationToken = default)
    {
        var action = ResolveActionForCategory(category);
        if (!TryApplyAction(action, out var blockedReason))
        {
            StatusText = blockedReason;
            return;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            await _repository.StartTrackingAsync(_userId, category, description?.Trim() ?? string.Empty, now, cancellationToken).ConfigureAwait(false);
            CurrentCategory = category;
            IsTracking = true;
            StatusText = $"Tracking: {category}";
        }
        finally
        {
            _writeGate.Release();
        }

        await LoadTodayEntriesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopTrackingAsync(CancellationToken cancellationToken = default)
    {
        if (!TryApplyAction(WorkAction.EndWork, out var blockedReason))
        {
            StatusText = blockedReason;
            return;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsTracking)
            {
                return;
            }

            await _repository.StopTrackingAsync(_userId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            IsTracking = false;
            CurrentCategory = "Nicht gestartet";
            StatusText = "Arbeitstag beendet";
        }
        finally
        {
            _writeGate.Release();
        }

        await LoadTodayEntriesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateEntryAsync(TimeEntry entry, CancellationToken cancellationToken = default)
    {
        await _repository.UpdateEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        await LoadTodayEntriesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteEntryAsync(long id, CancellationToken cancellationToken = default)
    {
        await _repository.DeleteEntryAsync(id, _userId, cancellationToken).ConfigureAwait(false);
        await LoadTodayEntriesAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryApplyAction(WorkAction action, out string message)
    {
        var context = CreateRuleContext();
        var result = WorkStateMachine.Transition(SessionState, action, context);
        if (!result.IsAllowed)
        {
            message = result.Message;
            return false;
        }

        SessionState = result.NextState;
        _stateDay = context.Today;
        ApplyStateSideEffects(action);
        message = result.Message;
        return true;
    }

    public IReadOnlyList<WorkAction> GetAllowedActions() =>
        WorkStateMachine.GetAllowedActions(SessionState, CreateRuleContext());

    private WorkAction ResolveActionForCategory(string category)
    {
        var isProjectOrReason = category.StartsWith("Projekt:", StringComparison.OrdinalIgnoreCase)
            || category.StartsWith("Grund:", StringComparison.OrdinalIgnoreCase);

        if (SessionState == WorkSessionState.Working && isProjectOrReason)
        {
            return WorkAction.SwitchContext;
        }

        if (category.StartsWith("Pause - Kurz", StringComparison.OrdinalIgnoreCase))
        {
            return WorkAction.StartFlexibleBreak;
        }

        if (category.StartsWith("Pause - Mittag", StringComparison.OrdinalIgnoreCase))
        {
            return WorkAction.StartFixedBreak;
        }

        if (SessionState is WorkSessionState.BreakFlexible or WorkSessionState.BreakFixed)
        {
            return WorkAction.ResumeWork;
        }

        return WorkAction.StartWork;
    }

    private WorkRuleContext CreateRuleContext()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return new WorkRuleContext(
            Today: today,
            StateDay: _stateDay,
            HasStartedToday: _hasStartedToday,
            FixedBreakStartedAtUtc: _fixedBreakStartedAtUtc,
            MinFixedBreakDuration: TimeSpan.FromMinutes(30),
            AllowEndDuringBreak: true,
            CurrentTimestampUtc: DateTimeOffset.UtcNow);
    }

    private void RebuildSessionStateFromEntries()
    {
        _stateDay = DateOnly.FromDateTime(DateTime.Now);
        _fixedBreakStartedAtUtc = null;

        if (Entries.Count == 0)
        {
            _hasStartedToday = false;
            SessionState = WorkSessionState.OffDuty;
            IsTracking = false;
            CurrentCategory = "Nicht gestartet";
            StatusText = "Bereit";
            return;
        }

        _hasStartedToday = Entries.Any(static e => !e.IsDeleted && !IsBreakCategory(e.Category));
        var active = Entries.LastOrDefault(static e => !e.IsDeleted && e.EndTime is null);

        if (active is null)
        {
            SessionState = WorkSessionState.Ended;
            IsTracking = false;
            CurrentCategory = "Nicht gestartet";
            StatusText = "Arbeitstag beendet";
            return;
        }

        IsTracking = true;
        CurrentCategory = active.Category;
        StatusText = $"Tracking: {active.Category}";

        if (active.Category.StartsWith("Pause - Mittag", StringComparison.OrdinalIgnoreCase))
        {
            SessionState = WorkSessionState.BreakFixed;
            _fixedBreakStartedAtUtc = active.StartTime;
            return;
        }

        SessionState = active.Category.StartsWith("Pause - Kurz", StringComparison.OrdinalIgnoreCase)
            ? WorkSessionState.BreakFlexible
            : WorkSessionState.Working;
    }

    private void ApplyStateSideEffects(WorkAction action)
    {
        switch (action)
        {
            case WorkAction.StartWork:
                _hasStartedToday = true;
                break;
            case WorkAction.StartFixedBreak:
                _fixedBreakStartedAtUtc = DateTimeOffset.UtcNow;
                break;
            case WorkAction.ResumeWork:
            case WorkAction.EndWork:
                _fixedBreakStartedAtUtc = null;
                break;
        }
    }

    private static bool IsBreakCategory(string category) =>
        category.StartsWith("Pause - Kurz", StringComparison.OrdinalIgnoreCase)
        || category.StartsWith("Pause - Mittag", StringComparison.OrdinalIgnoreCase);
}

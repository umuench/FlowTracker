using FlowTracker.Domain;
using FlowTracker.Repositories;
using FlowTracker.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace FlowTracker.ViewModels;

/// <summary>
/// Liefert Dashboard-Daten, Kennzahlen und Exportfunktionen für einen Benutzer.
/// </summary>
public sealed class DashboardViewModel(
    ITimeEntryRepository repository,
    IBreakPolicyRepository breakPolicyRepository,
    ReportExportService reportExportService,
    string userId) : ViewModelBase
{
    private readonly ITimeEntryRepository _repository = repository;
    private readonly IBreakPolicyRepository _breakPolicyRepository = breakPolicyRepository;
    private readonly ReportExportService _reportExportService = reportExportService;
    private readonly string _userId = userId;
    private ReportingPeriod _selectedPeriod = ReportingPeriod.Tag;
    private string _statusMessage = "Bereit";
    private string _totalDurationText = "0.00h";
    private string _entryCountText = "0 Einträge";
    private string _activeSpanText = "-";
    private string _breakStatusText = "-";
    private string _targetDurationText = "0.00h";
    private string _overtimeText = "0.00h";
    private string _missingText = "0.00h";
    private ReportSummary _lastSummary = ReportSummary.Empty;

    /// <summary>
    /// Editierbare Eintragsliste für die Chronik.
    /// </summary>
    public ObservableCollection<EditableTimeEntryRow> Entries { get; } = [];

    /// <summary>
    /// Tagesweise Saldenzeilen inkl. kumulierter Werte.
    /// </summary>
    public ObservableCollection<DailyBalanceRow> DailyBalances { get; } = [];

    public IReadOnlyList<ReportingPeriod> AvailablePeriods { get; } =
    [
        ReportingPeriod.Tag,
        ReportingPeriod.Woche,
        ReportingPeriod.Monat,
        ReportingPeriod.Jahr
    ];

    public ReportingPeriod SelectedPeriod
    {
        get => _selectedPeriod;
        set => SetProperty(ref _selectedPeriod, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string TotalDurationText
    {
        get => _totalDurationText;
        private set => SetProperty(ref _totalDurationText, value);
    }

    public string EntryCountText
    {
        get => _entryCountText;
        private set => SetProperty(ref _entryCountText, value);
    }

    public string ActiveSpanText
    {
        get => _activeSpanText;
        private set => SetProperty(ref _activeSpanText, value);
    }

    public string BreakStatusText
    {
        get => _breakStatusText;
        private set => SetProperty(ref _breakStatusText, value);
    }

    public string TargetDurationText
    {
        get => _targetDurationText;
        private set => SetProperty(ref _targetDurationText, value);
    }

    public string OvertimeText
    {
        get => _overtimeText;
        private set => SetProperty(ref _overtimeText, value);
    }

    public string MissingText
    {
        get => _missingText;
        private set => SetProperty(ref _missingText, value);
    }

    /// <summary>
    /// Lädt Daten für den ausgewählten Zeitraum und berechnet alle Dashboard-KPIs.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // EVA:
        // E: SelectedPeriod und aktuelle Zeit.
        // V: Daten laden, KPI/Saldo/Break/Targets berechnen, Summary aufbauen.
        // A: ObservableCollections und Text-KPIs für das UI aktualisieren.
        var (fromUtc, toUtc) = CalculateRange(SelectedPeriod, DateTimeOffset.Now);
        var entries = await _repository.GetEntriesAsync(_userId, fromUtc, toUtc, includeDeleted: false, cancellationToken).ConfigureAwait(false);

        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(new EditableTimeEntryRow(entry));
        }

        var total = entries.Where(static e => e.EndTime is not null)
            .Aggregate(TimeSpan.Zero, static (acc, next) => acc + (next.EndTime!.Value - next.StartTime));

        TotalDurationText = $"{total.TotalHours:F2}h";
        EntryCountText = $"{entries.Count} Einträge";
        ActiveSpanText = entries.Count == 0
            ? "-"
            : $"{entries.First().StartTime.ToLocalTime():HH:mm} - {entries.Last().EndTime?.ToLocalTime().ToString("HH:mm") ?? "offen"}";

        var policy = await _breakPolicyRepository.GetDefaultPolicyAsync(cancellationToken).ConfigureAwait(false);
        var pauseActual = entries
            .Where(static e => e.EndTime is not null && e.Category.StartsWith("Pause", StringComparison.OrdinalIgnoreCase))
            .Aggregate(TimeSpan.Zero, static (acc, next) => acc + (next.EndTime!.Value - next.StartTime));
        var targetMinutes = policy?.DurationMinutes ?? 60;
        BreakStatusText = $"{pauseActual.TotalMinutes:F0} / {targetMinutes} min";

        var productiveDuration = entries
            .Where(static e => e.EndTime is not null && !e.Category.StartsWith("Pause", StringComparison.OrdinalIgnoreCase))
            .Aggregate(TimeSpan.Zero, static (acc, next) => acc + (next.EndTime!.Value - next.StartTime));
        var targetPerWorkday = TimeSpan.FromMinutes(policy?.TargetWorkMinutes ?? 480);
        var targetDuration = CalculateTargetDuration(fromUtc, toUtc, targetPerWorkday);
        var balance = productiveDuration - targetDuration;
        var overtime = balance > TimeSpan.Zero ? balance : TimeSpan.Zero;
        var missing = balance < TimeSpan.Zero ? -balance : TimeSpan.Zero;

        TargetDurationText = $"{targetDuration.TotalHours:F2}h";
        OvertimeText = $"{overtime.TotalHours:F2}h";
        MissingText = $"{missing.TotalHours:F2}h";
        RebuildDailyBalances(entries, fromUtc, toUtc, targetPerWorkday);
        _lastSummary = new ReportSummary(
            ProductiveDuration: productiveDuration,
            TargetDuration: targetDuration,
            OvertimeDuration: overtime,
            MissingDuration: missing,
            EntryCount: entries.Count,
            PeriodLabel: SelectedPeriod.ToString());

        StatusMessage = $"{entries.Count} Einträge geladen (Saldo {balance.TotalHours:+0.00;-0.00;0.00}h)";
    }

    /// <summary>
    /// Validiert und speichert einen bearbeiteten Chronik-Eintrag.
    /// </summary>
    public async Task<bool> SaveAsync(EditableTimeEntryRow row, CancellationToken cancellationToken = default)
    {
        if (!row.TryBuildTimeEntry(out var entry))
        {
            StatusMessage = "Ungültiges Datum/Zeitformat. Erwartet: yyyy-MM-dd HH:mm";
            return false;
        }

        await _repository.UpdateEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        StatusMessage = $"Eintrag #{entry.Id} gespeichert";
        return true;
    }

    /// <summary>
    /// Löscht einen Eintrag per Soft Delete.
    /// </summary>
    public async Task DeleteAsync(EditableTimeEntryRow row, CancellationToken cancellationToken = default)
    {
        await _repository.DeleteEntryAsync(row.Id, _userId, cancellationToken).ConfigureAwait(false);
        Entries.Remove(row);
        StatusMessage = $"Eintrag #{row.Id} gelöscht";
    }

    /// <summary>
    /// Exportiert die aktuelle Sicht als CSV-Datei.
    /// </summary>
    public async Task ExportCsvAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var entries = BuildDomainEntriesForExport();
        await _reportExportService.ExportCsvAsync(filePath, entries, _lastSummary, cancellationToken).ConfigureAwait(false);
        StatusMessage = $"CSV exportiert: {Path.GetFileName(filePath)}";
    }

    /// <summary>
    /// Exportiert die aktuelle Sicht als PDF-Bericht.
    /// </summary>
    public async Task ExportPdfAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var entries = BuildDomainEntriesForExport();
        var title = $"FlowTracker Report ({SelectedPeriod})";
        await _reportExportService.ExportPdfAsync(filePath, title, entries, _lastSummary, cancellationToken).ConfigureAwait(false);
        StatusMessage = $"PDF exportiert: {Path.GetFileName(filePath)}";
    }

    private IReadOnlyList<TimeEntry> BuildDomainEntriesForExport()
    {
        var list = new List<TimeEntry>(Entries.Count);
        foreach (var row in Entries)
        {
            if (row.TryBuildTimeEntry(out var entry))
            {
                list.Add(entry);
            }
        }

        return list;
    }

    /// <summary>
    /// Ermittelt UTC-Zeitgrenzen für den gewählten Reporting-Zeitraum.
    /// </summary>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) CalculateRange(ReportingPeriod period, DateTimeOffset nowLocal)
    {
        var localDate = nowLocal.Date;
        return period switch
        {
            ReportingPeriod.Tag => (ToUtc(localDate), ToUtc(localDate.AddDays(1))),
            ReportingPeriod.Woche => BuildWeekRange(localDate),
            ReportingPeriod.Monat => (ToUtc(new DateTime(localDate.Year, localDate.Month, 1)), ToUtc(new DateTime(localDate.Year, localDate.Month, 1).AddMonths(1))),
            ReportingPeriod.Jahr => (ToUtc(new DateTime(localDate.Year, 1, 1)), ToUtc(new DateTime(localDate.Year + 1, 1, 1))),
            _ => (ToUtc(localDate), ToUtc(localDate.AddDays(1)))
        };
    }

    private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) BuildWeekRange(DateTime localDate)
    {
        var dayOffset = ((int)localDate.DayOfWeek + 6) % 7;
        var start = localDate.AddDays(-dayOffset);
        var end = start.AddDays(7);
        return (ToUtc(start), ToUtc(end));
    }

    private static DateTimeOffset ToUtc(DateTime localDate)
    {
        var local = new DateTimeOffset(localDate, TimeZoneInfo.Local.GetUtcOffset(localDate));
        return local.ToUniversalTime();
    }

    /// <summary>
    /// Berechnet die Soll-Arbeitszeit im Zeitraum auf Basis von Werktagen.
    /// </summary>
    public static TimeSpan CalculateTargetDuration(DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeSpan targetPerWorkday)
    {
        var fromLocal = fromUtc.ToLocalTime().Date;
        var toLocal = toUtc.ToLocalTime().Date;
        var workdays = CountWorkdays(fromLocal, toLocal);
        return TimeSpan.FromTicks(targetPerWorkday.Ticks * workdays);
    }

    /// <summary>
    /// Baut eine tägliche Saldochronik für den Zeitraum.
    /// </summary>
    public static IReadOnlyList<DailyBalanceRow> BuildDailyBalances(
        IReadOnlyList<TimeEntry> entries,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan targetPerWorkday)
    {
        var productiveByDay = entries
            .Where(static e => e.EndTime is not null && !e.Category.StartsWith("Pause", StringComparison.OrdinalIgnoreCase))
            .GroupBy(static e => e.StartTime.ToLocalTime().Date)
            .ToDictionary(
                static g => g.Key,
                static g => g.Aggregate(TimeSpan.Zero, (acc, entry) => acc + (entry.EndTime!.Value - entry.StartTime)));

        var rows = new List<DailyBalanceRow>();
        var cumulative = TimeSpan.Zero;
        var fromLocal = fromUtc.ToLocalTime().Date;
        var toLocalExclusive = toUtc.ToLocalTime().Date;

        for (var day = fromLocal; day < toLocalExclusive; day = day.AddDays(1))
        {
            var productive = productiveByDay.GetValueOrDefault(day, TimeSpan.Zero);
            var target = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? TimeSpan.Zero
                : targetPerWorkday;
            var balance = productive - target;
            cumulative += balance;

            rows.Add(new DailyBalanceRow(
                Day: day,
                Productive: productive,
                Target: target,
                Balance: balance,
                Cumulative: cumulative));
        }

        return rows;
    }

    private void RebuildDailyBalances(IReadOnlyList<TimeEntry> entries, DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeSpan targetPerWorkday)
    {
        DailyBalances.Clear();
        foreach (var row in BuildDailyBalances(entries, fromUtc, toUtc, targetPerWorkday))
        {
            DailyBalances.Add(row);
        }
    }

    /// <summary>
    /// Zählt Werktage (Mo-Fr) im übergebenen lokalen Datumsbereich.
    /// </summary>
    public static int CountWorkdays(DateTime fromLocalInclusive, DateTime toLocalExclusive)
    {
        if (toLocalExclusive <= fromLocalInclusive)
        {
            return 0;
        }

        var count = 0;
        for (var day = fromLocalInclusive.Date; day < toLocalExclusive.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>
/// Repräsentiert eine Zeile der täglichen Soll-/Ist-Bilanz im Dashboard.
/// </summary>
public sealed record DailyBalanceRow(
    DateTime Day,
    TimeSpan Productive,
    TimeSpan Target,
    TimeSpan Balance,
    TimeSpan Cumulative)
{
    public string DayText => Day.ToString("yyyy-MM-dd");
    public string ProductiveText => $"{Productive.TotalHours:F2}h";
    public string TargetText => $"{Target.TotalHours:F2}h";
    public string BalanceText => $"{Balance.TotalHours:+0.00;-0.00;0.00}h";
    public string CumulativeText => $"{Cumulative.TotalHours:+0.00;-0.00;0.00}h";
}

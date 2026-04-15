using FlowTracker.Infrastructure;
using FlowTracker.Domain;
using FlowTracker.Interop;
using FlowTracker.Repositories;
using FlowTracker.Services;
using FlowTracker.ViewModels;
using FlowTracker.Views;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace FlowTracker;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private const string SingleInstanceMutexName = @"Global\FlowTracker.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ToolStripMenuItem? _ghostModeMenuItem;
    private IdleMonitorService? _idleMonitor;
    private OrbitalWindow? _orbitalWindow;
    private TrackingViewModel? _trackingViewModel;
    private DashboardViewModel? _dashboardViewModel;
    private DashboardWindow? _dashboardWindow;
    private TrayFlyoutWindow? _trayFlyoutWindow;
    private ReminderDotWindow? _reminderDotWindow;
    private SettingsWindow? _settingsWindow;
    private ITimeEntryRepository? _timeEntryRepository;
    private IBreakPolicyRepository? _breakPolicyRepository;
    private ReportExportService? _reportExportService;
    private AppSettingsService? _appSettingsService;
    private AppSettings _appSettings = new();
    private AppLogger? _logger;
    private IReadOnlyList<RadialMenuItemDefinition> _radialMenuItems = [];
    private Icon? _activeTrayIcon;
    private Icon? _idleTrayIcon;
    private Icon? _ghostTrayIcon;
    private Icon? _orangeTrayIcon;
    private readonly Dictionary<string, Icon> _taskTrayIcons = new(StringComparer.OrdinalIgnoreCase);
    private bool _isIdleState;
    private bool _isGhostMode;
    private DateTimeOffset _orbitalSelectionUntilUtc;
    private DateTimeOffset _orbitalSuppressedUntilUtc;
    private DateTimeOffset _orbitalInteractionPinnedUntilUtc;
    private DateTimeOffset _awaitingPostSelectionUntilUtc;
    private bool _isOrbitalInteractionPinned;
    private bool _awaitingPostSelectionActivity;
    private bool _isShuttingDown;
    private int _ignoredReminderCount;
    private int _lastQuickDismissStreak;
    private DateTimeOffset _lastReminderEscalationUtc;
    private OrbitalReminderLevel _reminderLevel = OrbitalReminderLevel.DotOnly;
    private readonly Dictionary<OrbitalNoShowReason, int> _noShowCounters = [];
    private static readonly string[] DefaultFocusSuppressedProcesses =
    [
        "devenv",
        "code",
        "rider64",
        "winword",
        "excel",
        "powerpnt",
        "teams",
        "ms-teams"
    ];
    private readonly HashSet<string> _focusSuppressedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "devenv",
        "code",
        "rider64",
        "winword",
        "excel",
        "powerpnt",
        "teams",
        "ms-teams"
    };
    private readonly Dictionary<string, WinForms.ToolStripMenuItem> _profileMenuItems = new(StringComparer.OrdinalIgnoreCase);
    private OrbitalBehaviorProfile _behaviorProfile = OrbitalBehaviorProfile.Balanced;
    private int _lastMouseX;
    private int _lastMouseY;
    private bool _hasMousePosition;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        InitializeLogging();
        RegisterGlobalExceptionHandlers();

        try
        {
            if (!AcquireSingleInstance())
            {
                System.Windows.MessageBox.Show(
                    "FlowTracker läuft bereits. Bitte die bestehende Instanz im Tray verwenden.",
                    "FlowTracker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            InitializeDataLayer();
            InitializeTrayIcon();
            InitializeIdleMonitor();
            _ = _logger?.InfoAsync("Startup abgeschlossen.");
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Startup fehlgeschlagen.", ex);
            System.Windows.MessageBox.Show(
                "FlowTracker konnte nicht gestartet werden. Details stehen im Log unter %LocalAppData%\\FlowTracker\\logs\\app.log",
                "FlowTracker Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _isShuttingDown = true;

        try
        {
            if (_idleMonitor is not null)
            {
                _idleMonitor.IdleStateChanged -= OnIdleStateChanged;
                _idleMonitor.MousePositionChanged -= OnMousePositionChanged;
                _idleMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _idleMonitor = null;
            }
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Fehler beim Beenden des IdleMonitors.", ex);
        }

        try
        {
            if (_orbitalWindow is not null)
            {
                _orbitalWindow.MenuItemInvoked -= OnMenuItemInvoked;
                _orbitalWindow.OverlayTelemetry -= OnOrbitalOverlayTelemetry;
                _orbitalWindow.OverlayClosed -= OnOrbitalOverlayClosed;
                _orbitalWindow.Close();
            }
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Fehler beim Schließen des Orbital-Fensters.", ex);
        }

        try
        {
            _dashboardWindow?.Close();
            _trayFlyoutWindow?.Close();
            _settingsWindow?.Close();
            if (_reminderDotWindow is not null)
            {
                _reminderDotWindow.DotClicked -= OnReminderDotClicked;
                _reminderDotWindow.Close();
            }
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Fehler beim Schließen von Dashboard/Flyout.", ex);
        }
        _trayFlyoutWindow = null;
        _reminderDotWindow = null;
        _settingsWindow = null;
        _dashboardWindow = null;
        _orbitalWindow = null;
        _dashboardViewModel = null;
        _trackingViewModel = null;
        _timeEntryRepository = null;
        _breakPolicyRepository = null;
        _reportExportService = null;
        _logger = null;

        _activeTrayIcon?.Dispose();
        _activeTrayIcon = null;
        _idleTrayIcon?.Dispose();
        _idleTrayIcon = null;
        _ghostTrayIcon?.Dispose();
        _ghostTrayIcon = null;
        _orangeTrayIcon?.Dispose();
        _orangeTrayIcon = null;
        foreach (var icon in _taskTrayIcons.Values)
        {
            icon.Dispose();
        }
        _taskTrayIcons.Clear();

        try
        {
            if (_trayIcon is not null)
            {
                _trayIcon.MouseClick -= OnTrayMouseClick;
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Fehler beim Dispose des Tray-Icons.", ex);
        }

        _trayIcon = null;
        _ghostModeMenuItem = null;
        ReleaseSingleInstance();

        base.OnExit(e);
    }

    private void InitializeTrayIcon()
    {
        LoadTrayIcons();

        var contextMenu = new WinForms.ContextMenuStrip();
        var dashboardItem = new WinForms.ToolStripMenuItem("Dashboard öffnen");
        dashboardItem.Click += (_, _) => Dispatcher.Invoke(OpenDashboard);

        var stopItem = new WinForms.ToolStripMenuItem("Tracking stoppen");
        stopItem.Click += (_, _) => _ = StopTrackingFromTrayAsync();

        var ghostModeItem = new WinForms.ToolStripMenuItem("Ghost Mode")
        {
            CheckOnClick = true
        };
        ghostModeItem.Click += (_, _) => Dispatcher.Invoke(() => _ = ToggleGhostModeAsync(ghostModeItem.Checked, fromFlyout: false));
        _ghostModeMenuItem = ghostModeItem;

        var reminderProfileItem = new WinForms.ToolStripMenuItem("Reminder-Profil");
        AddProfileMenuItem(reminderProfileItem, "quiet", "Quiet");
        AddProfileMenuItem(reminderProfileItem, "balanced", "Balanced");
        AddProfileMenuItem(reminderProfileItem, "strict", "Strict");
        UpdateProfileMenuChecks(_behaviorProfile.Name);

        var settingsItem = new WinForms.ToolStripMenuItem("Einstellungen");
        settingsItem.Click += (_, _) => Dispatcher.Invoke(OpenSettingsWindow);

        var exitItem = new WinForms.ToolStripMenuItem("Beenden");
        exitItem.Click += (_, _) => Dispatcher.BeginInvoke(RequestShutdown);

        contextMenu.Items.Add(dashboardItem);
        contextMenu.Items.Add(stopItem);
        contextMenu.Items.Add(ghostModeItem);
        contextMenu.Items.Add(reminderProfileItem);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "FlowTracker",
            Icon = _idleTrayIcon ?? SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = contextMenu
        };
        _trayIcon.MouseClick += OnTrayMouseClick;

        UpdateTrayFromViewModel();
        _ = _logger?.InfoAsync("Tray initialisiert (NotifyIcon.Visible=true).");
    }

    private void InitializeIdleMonitor()
    {
        if (_idleMonitor is not null)
        {
            _idleMonitor.IdleStateChanged -= OnIdleStateChanged;
            _idleMonitor.MousePositionChanged -= OnMousePositionChanged;
            _idleMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _idleMonitor = null;
        }

        _idleMonitor = new IdleMonitorService(PollInterval, _behaviorProfile.IdleThreshold);
        _idleMonitor.IdleStateChanged += OnIdleStateChanged;
        _idleMonitor.MousePositionChanged += OnMousePositionChanged;
        _idleMonitor.Start();
        _ = _logger?.InfoAsync($"Reminder-Profil aktiv: {_behaviorProfile.Name} (idle={_behaviorProfile.IdleThreshold.TotalSeconds:F0}s).");
    }

    private void InitializeDataLayer()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowTracker");

        var settingsPath = Path.Combine(appDataDirectory, "settings.json");
        _appSettingsService = new AppSettingsService(settingsPath);
        _appSettings = _appSettingsService.LoadAsync().GetAwaiter().GetResult();
        var configuredProfile = string.IsNullOrWhiteSpace(_appSettings.ReminderProfile)
            ? Environment.GetEnvironmentVariable("FLOWTRACKER_REMINDER_PROFILE")
            : _appSettings.ReminderProfile;
        ApplyBehaviorProfile(configuredProfile, persist: false, restartIdleMonitor: false);
        ApplyFocusSuppressionProcesses(_appSettings.FocusSuppressedProcesses, persist: false);

        var databasePath = Path.Combine(
            appDataDirectory,
            "flowtracker.db");

        var options = new SqliteOptions(databasePath);
        var connectionFactory = new SqliteConnectionFactory(options);
        var initializer = new DatabaseInitializer(connectionFactory);
        initializer.InitializeAsync().GetAwaiter().GetResult();

        _timeEntryRepository = new TimeEntryRepository(connectionFactory);
        _breakPolicyRepository = new BreakPolicyRepository(connectionFactory);
        _reportExportService = new ReportExportService();
        _trackingViewModel = new TrackingViewModel(_timeEntryRepository, Environment.UserName);
        _dashboardViewModel = new DashboardViewModel(_timeEntryRepository, _breakPolicyRepository, _reportExportService, Environment.UserName);
        var menuFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowTracker",
            "radial-menu.json");
        _radialMenuItems = new RadialMenuService(menuFilePath).LoadOrCreate();
        _trackingViewModel.LoadTodayEntriesAsync().GetAwaiter().GetResult();
        _ = _logger?.InfoAsync("Datenbank und ViewModels initialisiert.");
    }

    private void OnIdleStateChanged(object? sender, IdleStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // EVA:
            // E: Idle-Event (IsIdle, Dauer, Timestamp) + aktueller App-/Tracking-Kontext.
            // V: Policy auswerten (Suppression, Fokuskontext, AllowedActions, ReminderLevel).
            // A: Dot/Orbital anzeigen oder unterdrücken, Telemetrie-/NoShow-Reason loggen.
            if (_trayIcon is null)
            {
                return;
            }

            var wasIdle = _isIdleState;
            if (_isGhostMode)
            {
                _isOrbitalInteractionPinned = false;
                _orbitalWindow?.HideOverlay(OrbitalHideReason.GhostMode);
                HideReminderDot();
                RegisterNoShow(OrbitalNoShowReason.GhostMode, "ghost mode active");
                UpdateTrayFromViewModel();
                return;
            }

            SetTrayText(e.IsIdle
                ? $"FlowTracker - Idle ({e.IdleDuration.TotalSeconds:F1}s)"
                : "FlowTracker - Aktiv");
            _isIdleState = e.IsIdle;

            if (!e.IsIdle)
            {
                if (wasIdle)
                {
                    _awaitingPostSelectionActivity = false;
                }

                HideReminderDot();

                if (_isOrbitalInteractionPinned && DateTimeOffset.UtcNow <= _orbitalInteractionPinnedUntilUtc && _orbitalWindow?.IsReadyForInteraction == true)
                {
                    UpdateTrayFromViewModel();
                    return;
                }

                _isOrbitalInteractionPinned = false;
                var isInSelectionWindow = DateTimeOffset.UtcNow <= _orbitalSelectionUntilUtc;
                if (!isInSelectionWindow || _orbitalWindow is null || !_orbitalWindow.IsReadyForInteraction)
                {
                    _orbitalWindow?.HideOverlay(OrbitalHideReason.AppForced);
                }

                UpdateTrayFromViewModel();
                return;
            }

            var decision = EvaluateDisplayDecision(e);
            if (decision.NoShowReason is not null)
            {
                HideReminderDot();
                RegisterNoShow(decision.NoShowReason.Value, decision.ReasonText);
            }

            if (decision.Mode == OrbitalDisplayMode.ShowDot)
            {
                ShowReminderDot(decision.ReasonText);
            }
            else if (decision.Mode == OrbitalDisplayMode.ShowOrbital)
            {
                HideReminderDot();
                ShowOrbitalMenu(decision.AllowedActions);
            }
            else
            {
                HideReminderDot();
            }

            UpdateTrayFromViewModel();
        });
    }

    private void OnMousePositionChanged(object? sender, MousePositionChangedEventArgs e)
    {
        _lastMouseX = e.X;
        _lastMouseY = e.Y;
        _hasMousePosition = true;
    }

    private async void OnMenuItemInvoked(object? sender, RadialMenuInvokedEventArgs e)
    {
        if (_trackingViewModel is null || _isGhostMode)
        {
            return;
        }

        try
        {
            switch (e.ActionKey)
            {
                case "start_work":
                    await _trackingViewModel.SelectCategoryAsync("Arbeit", e.Label);
                    break;
                case "end_work":
                    await _trackingViewModel.StopTrackingAsync();
                    break;
                case "break_flexible":
                    await _trackingViewModel.SelectCategoryAsync("Pause - Kurz", e.Label);
                    break;
                case "break_lunch":
                    await _trackingViewModel.SelectCategoryAsync("Pause - Mittag", e.Label);
                    break;
                case "resume_work":
                    await _trackingViewModel.SelectCategoryAsync("Arbeit", e.Label);
                    break;
                default:
                    if (e.ActionKey.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
                    {
                        var project = e.ActionKey["project:".Length..];
                        await _trackingViewModel.SelectCategoryAsync($"Projekt: {project}", e.Label);
                    }
                    else if (e.ActionKey.StartsWith("reason:", StringComparison.OrdinalIgnoreCase))
                    {
                        var reason = e.ActionKey["reason:".Length..];
                        await _trackingViewModel.SelectCategoryAsync($"Grund: {reason}", e.Label);
                    }
                    break;
            }

            UpdateTrayFromViewModel();
            _ = _logger?.InfoAsync($"Radial-Aktion gewählt: {e.ActionKey} ({e.Label})");
            ApplyDynamicSuppression("menu-invoked");
            _isOrbitalInteractionPinned = false;
            _awaitingPostSelectionActivity = true;
            _awaitingPostSelectionUntilUtc = DateTimeOffset.UtcNow.Add(_behaviorProfile.PostSelectionActivityWindow);
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Fehler bei Radial-Aktion.", ex);
        }
    }

    private async Task StopTrackingFromTrayAsync()
    {
        if (_trackingViewModel is null)
        {
            return;
        }

        try
        {
            await _trackingViewModel.StopTrackingAsync();
            await Dispatcher.InvokeAsync(UpdateTrayFromViewModel);
            _ = _logger?.InfoAsync("Tracking gestoppt.");
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync("Fehler beim Stoppen des Trackings.", ex);
        }
    }

    private void UpdateTrayFromViewModel()
    {
        if (_trayIcon is null || _trackingViewModel is null)
        {
            return;
        }

        var hours = _trackingViewModel.TodayLoggedDuration.TotalHours;
        var status = _isGhostMode
            ? "Ghost Mode"
            : _trackingViewModel.IsTracking ? _trackingViewModel.CurrentCategory : "Pausiert";

        _trayIcon.Icon = ResolveTrayIcon();
        SetTrayText($"FlowTracker - {status} ({hours:F2}h heute)");
        _trayFlyoutWindow?.UpdateSummary(
            hours,
            status,
            _isGhostMode,
            canStopTracking: _trackingViewModel.GetAllowedActions().Contains(WorkAction.EndWork));
    }

    private void LoadTrayIcons()
    {
        var iconsDirectory = Path.Combine(AppContext.BaseDirectory, "Icons");
        var activeIconPath = Path.Combine(iconsDirectory, "TimeTrackerGreen.ico");
        var activeFallbackPath = Path.Combine(iconsDirectory, "TimeTrackerSchwarz.ico");
        var orangeIconPath = Path.Combine(iconsDirectory, "TimeTrackerOrange.ico");
        var grayIconPath = Path.Combine(iconsDirectory, "TimeTrackerGrau.ico");
        var transparentIconPath = Path.Combine(iconsDirectory, "TimeTrackerTransparent.ico");

        _activeTrayIcon = TryLoadIcon(activeIconPath) ?? TryLoadIcon(activeFallbackPath);
        _orangeTrayIcon = TryLoadIcon(orangeIconPath);
        _ghostTrayIcon = TryLoadIcon(grayIconPath) ?? TryLoadIcon(transparentIconPath);
        _taskTrayIcons.Clear();

        RegisterTaskIcon("meeting", Path.Combine(iconsDirectory, "TimeTrackerRed.ico"));
        RegisterTaskIcon("admin", Path.Combine(iconsDirectory, "TimeTrackerPurple.ico"));
        RegisterTaskIcon("support", Path.Combine(iconsDirectory, "TimeTrackerBlue.ico"));
        RegisterTaskIcon("projekt", Path.Combine(iconsDirectory, "TimeTrackerBlue.ico"));
        RegisterTaskIcon("arbeit", Path.Combine(iconsDirectory, "TimeTrackerGreen.ico"));
        RegisterTaskIcon("pause", Path.Combine(iconsDirectory, "TimeTrackerOrange.ico"));

        // Wichtige UX-Regel: kein unsichtbares Tray-Icon im Normalbetrieb.
        _idleTrayIcon = _orangeTrayIcon ?? _activeTrayIcon ?? SystemIcons.Application;
        _ghostTrayIcon ??= _orangeTrayIcon ?? _activeTrayIcon ?? SystemIcons.Application;
        _activeTrayIcon ??= SystemIcons.Application;

        _ = _logger?.InfoAsync(
            "Tray-Icons geladen: " +
            $"active={DescribeIconSource(_activeTrayIcon, activeIconPath, activeFallbackPath)}, " +
            $"idle={DescribeIconSource(_idleTrayIcon, orangeIconPath, activeIconPath, activeFallbackPath)}, " +
            $"ghost={DescribeIconSource(_ghostTrayIcon, grayIconPath, transparentIconPath, orangeIconPath, activeIconPath, activeFallbackPath)}, " +
            $"taskIcons={_taskTrayIcons.Count}.");
    }

    private Icon ResolveTrayIcon()
    {
        if (_isGhostMode)
        {
            return _ghostTrayIcon ?? _activeTrayIcon ?? SystemIcons.Information;
        }

        if (_isIdleState || _trackingViewModel is null || !_trackingViewModel.IsTracking)
        {
            return _idleTrayIcon ?? _activeTrayIcon ?? SystemIcons.Information;
        }

        var category = _trackingViewModel.CurrentCategory;
        foreach (var pair in _taskTrayIcons)
        {
            if (category.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return _activeTrayIcon ?? SystemIcons.Information;
    }

    private void RegisterTaskIcon(string key, string iconPath)
    {
        var icon = TryLoadIcon(iconPath);
        if (icon is not null)
        {
            _taskTrayIcons[key] = icon;
        }
    }

    private void OpenDashboard()
    {
        if (_dashboardViewModel is null || _trackingViewModel is null)
        {
            return;
        }

        if (_dashboardWindow is not null)
        {
            _dashboardWindow.Activate();
            return;
        }

        _dashboardWindow = new DashboardWindow(_dashboardViewModel, RefreshAfterDashboardChangeAsync);
        _dashboardWindow.Closed += (_, _) => _dashboardWindow = null;
        _dashboardWindow.Show();
        _ = _logger?.InfoAsync("Dashboard geöffnet.");
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var snapshot = new AppSettings
        {
            ReminderProfile = _appSettings.ReminderProfile,
            FocusSuppressedProcesses = [.. _focusSuppressedProcesses]
        };

        var window = new SettingsWindow(snapshot);
        _settingsWindow = window;
        window.Closed += (_, _) => _settingsWindow = null;
        var result = window.ShowDialog();

        if (result == true && window.ResultSettings is { } updated)
        {
            ApplyBehaviorProfile(updated.ReminderProfile, persist: true, restartIdleMonitor: true);
            ApplyFocusSuppressionProcesses(updated.FocusSuppressedProcesses, persist: true);
            _ = _logger?.InfoAsync("Einstellungen gespeichert.");
        }
    }

    private async Task RefreshAfterDashboardChangeAsync()
    {
        if (_trackingViewModel is null)
        {
            return;
        }

        await _trackingViewModel.LoadTodayEntriesAsync();
        await Dispatcher.InvokeAsync(UpdateTrayFromViewModel);
    }

    private void OnTrayMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button != WinForms.MouseButtons.Left)
        {
            return;
        }

        Dispatcher.Invoke(OnTrayLeftClick);
    }

    private void OnTrayLeftClick()
    {
        if (_trackingViewModel is null)
        {
            return;
        }

        _trayFlyoutWindow ??= new TrayFlyoutWindow(
            OpenDashboard,
            StopTrackingFromTrayAsync,
            ToggleGhostModeFromFlyoutAsync);

        if (_trayFlyoutWindow.IsVisible)
        {
            _trayFlyoutWindow.Hide();
            return;
        }

        PositionTrayFlyout(_trayFlyoutWindow);
        UpdateTrayFromViewModel();
        _trayFlyoutWindow.Show();
        _trayFlyoutWindow.Activate();
    }

    private static void PositionTrayFlyout(Window flyout)
    {
        const double margin = 12;
        var workArea = SystemParameters.WorkArea;

        if (NativeMethods.TryGetCursorPosition(out var cursor))
        {
            var targetLeft = cursor.X - flyout.Width + 24;
            var targetTop = cursor.Y - flyout.Height - 8;

            flyout.Left = Math.Clamp(targetLeft, workArea.Left + margin, workArea.Right - flyout.Width - margin);
            flyout.Top = Math.Clamp(targetTop, workArea.Top + margin, workArea.Bottom - flyout.Height - margin);
            return;
        }

        flyout.Left = workArea.Right - flyout.Width - margin;
        flyout.Top = workArea.Bottom - flyout.Height - margin;
    }

    private async Task ToggleGhostModeFromFlyoutAsync()
    {
        await ToggleGhostModeAsync(!_isGhostMode, fromFlyout: true);
    }

    private async Task ToggleGhostModeAsync(bool enabled, bool fromFlyout)
    {
        _isGhostMode = enabled;
        if (_ghostModeMenuItem is not null)
        {
            _ghostModeMenuItem.Checked = _isGhostMode;
        }

        if (_isGhostMode)
        {
            _isOrbitalInteractionPinned = false;
            _orbitalWindow?.HideOverlay(OrbitalHideReason.GhostMode);
            HideReminderDot();
            await StopTrackingFromTrayAsync();
        }

        UpdateTrayFromViewModel();
        _ = _logger?.InfoAsync(_isGhostMode
            ? fromFlyout ? "Ghost Mode aktiviert (Flyout)." : "Ghost Mode aktiviert."
            : fromFlyout ? "Ghost Mode deaktiviert (Flyout)." : "Ghost Mode deaktiviert.");
    }

    private void AddProfileMenuItem(WinForms.ToolStripMenuItem parent, string profileName, string label)
    {
        var item = new WinForms.ToolStripMenuItem(label)
        {
            CheckOnClick = false
        };
        item.Click += (_, _) => Dispatcher.Invoke(() => ApplyBehaviorProfile(profileName, persist: true, restartIdleMonitor: true));
        parent.DropDownItems.Add(item);
        _profileMenuItems[profileName] = item;
    }

    private void UpdateProfileMenuChecks(string activeProfileName)
    {
        foreach (var pair in _profileMenuItems)
        {
            pair.Value.Checked = string.Equals(pair.Key, activeProfileName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ApplyBehaviorProfile(string? profileName, bool persist, bool restartIdleMonitor)
    {
        var normalized = NormalizeProfileName(profileName);
        _behaviorProfile = normalized switch
        {
            "quiet" => OrbitalBehaviorProfile.Quiet,
            "strict" => OrbitalBehaviorProfile.Strict,
            _ => OrbitalBehaviorProfile.Balanced
        };

        _appSettings.ReminderProfile = _behaviorProfile.Name;
        UpdateProfileMenuChecks(_behaviorProfile.Name);

        if (persist && _appSettingsService is not null)
        {
            _ = _appSettingsService.SaveAsync(_appSettings);
        }

        if (restartIdleMonitor)
        {
            InitializeIdleMonitor();
        }

        _ = _logger?.InfoAsync($"Reminder-Profil gesetzt: {_behaviorProfile.Name}.");
    }

    private void ApplyFocusSuppressionProcesses(IEnumerable<string>? processes, bool persist)
    {
        _focusSuppressedProcesses.Clear();
        var source = processes?.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).ToList();
        if (source is null || source.Count == 0)
        {
            source = [.. DefaultFocusSuppressedProcesses];
        }

        foreach (var process in source.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _focusSuppressedProcesses.Add(process);
        }

        _appSettings.FocusSuppressedProcesses = [.. _focusSuppressedProcesses.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)];
        if (persist && _appSettingsService is not null)
        {
            _ = _appSettingsService.SaveAsync(_appSettings);
        }
    }

    private static string NormalizeProfileName(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return "balanced";
        }

        return profileName.Trim().ToLowerInvariant();
    }

    private OrbitalDisplayDecision EvaluateDisplayDecision(IdleStateChangedEventArgs e)
    {
        // API-Vertrag:
        // Gibt eine deterministische Anzeigeentscheidung für den aktuellen Idle-Zeitpunkt zurück.
        // Side-Effects sind auf minimale Korrekturen von internen Flags begrenzt.
        if (_isShuttingDown)
        {
            return OrbitalDisplayDecision.None(OrbitalNoShowReason.Shutdown, "shutdown active");
        }

        if (!e.IsIdle)
        {
            return OrbitalDisplayDecision.None(OrbitalNoShowReason.NotIdle, "user active");
        }

        if (_awaitingPostSelectionActivity && DateTimeOffset.UtcNow > _awaitingPostSelectionUntilUtc)
        {
            _awaitingPostSelectionActivity = false;
        }

        if (_awaitingPostSelectionActivity)
        {
            return OrbitalDisplayDecision.None(OrbitalNoShowReason.AwaitingPostSelection, "awaiting post-selection activity");
        }

        if (DateTimeOffset.UtcNow < _orbitalSuppressedUntilUtc)
        {
            return OrbitalDisplayDecision.None(
                OrbitalNoShowReason.Suppressed,
                $"suppressed-until={_orbitalSuppressedUntilUtc:O}");
        }

        if (ShouldSuppressForForegroundContext(out var focusReason))
        {
            return OrbitalDisplayDecision.None(OrbitalNoShowReason.FocusSensitiveContext, focusReason);
        }

        if (_orbitalWindow?.IsReadyForInteraction == true)
        {
            return OrbitalDisplayDecision.None(OrbitalNoShowReason.AlreadyVisible, "orbital already visible");
        }

        var allowedActions = _trackingViewModel?.GetAllowedActions() ?? [];
        if (allowedActions.Count == 0)
        {
            _orbitalWindow?.HideOverlay(OrbitalHideReason.AppForced);
            return OrbitalDisplayDecision.None(OrbitalNoShowReason.NoAllowedActions, "no allowed actions");
        }

        if (!_hasMousePosition)
        {
            return OrbitalDisplayDecision.None(OrbitalNoShowReason.NoMousePosition, "missing cursor anchor");
        }

        var needsStrongReminder = RequiresStrongReminder(allowedActions);
        var nextMode = SelectReminderMode(needsStrongReminder);
        var reasonText = needsStrongReminder
            ? "required action pending"
            : "gentle reminder";
        return new OrbitalDisplayDecision(nextMode, reasonText, allowedActions, null);
    }

    private OrbitalDisplayMode SelectReminderMode(bool needsStrongReminder)
    {
        if (needsStrongReminder)
        {
            if (_reminderLevel == OrbitalReminderLevel.DotOnly)
            {
                return OrbitalDisplayMode.ShowDot;
            }

            return OrbitalDisplayMode.ShowOrbital;
        }

        return OrbitalDisplayMode.ShowDot;
    }

    private bool RequiresStrongReminder(IReadOnlyList<WorkAction> allowedActions)
    {
        if (_trackingViewModel is null)
        {
            return false;
        }

        if (_trackingViewModel.NeedsContextReminder)
        {
            return true;
        }

        return allowedActions.Contains(WorkAction.StartWork) || allowedActions.Contains(WorkAction.ResumeWork);
    }

    private void ShowOrbitalMenu(IReadOnlyList<WorkAction> allowedActions)
    {
        EnsureOrbitalWindow();
        var visibleMenuItems = GetVisibleRadialMenuItems(allowedActions);
        if (visibleMenuItems.Count == 0)
        {
            _ = _logger?.InfoAsync("Keine sichtbaren Orbital-Aktionen nach Filterung; Fallback auf Rohmenü.");
            visibleMenuItems = _radialMenuItems;
        }

        if (visibleMenuItems.Count == 0)
        {
            RegisterNoShow(OrbitalNoShowReason.NoVisibleMenuItems, "no visible menu items");
            return;
        }

        _orbitalWindow?.ShowAt(_lastMouseX, _lastMouseY, visibleMenuItems);
        _orbitalSelectionUntilUtc = DateTimeOffset.UtcNow.Add(_behaviorProfile.SelectionWindow);
        _orbitalInteractionPinnedUntilUtc = DateTimeOffset.UtcNow.Add(_behaviorProfile.InteractionPinWindow);
        _isOrbitalInteractionPinned = true;
    }

    private void EnsureReminderDotWindow()
    {
        if (_reminderDotWindow is not null)
        {
            return;
        }

        _reminderDotWindow = new ReminderDotWindow();
        _reminderDotWindow.DotClicked += OnReminderDotClicked;
    }

    private void ShowReminderDot(string reason)
    {
        EnsureReminderDotWindow();
        var wasVisible = _reminderDotWindow?.IsDotVisible == true;
        _reminderDotWindow?.ShowAtBottomRight();
        if (!wasVisible)
        {
            _ = _logger?.InfoAsync($"ReminderDot angezeigt: reason={reason}, level={_reminderLevel}, ignored={_ignoredReminderCount}.");
        }
    }

    private void HideReminderDot()
    {
        _reminderDotWindow?.HideDot();
    }

    private void OnReminderDotClicked(object? sender, EventArgs e)
    {
        _reminderLevel = OrbitalReminderLevel.Orbital;
        _lastReminderEscalationUtc = DateTimeOffset.UtcNow;
        _orbitalSuppressedUntilUtc = DateTimeOffset.MinValue;
        HideReminderDot();
        _ = _logger?.InfoAsync("ReminderDot geklickt; eskaliere auf Orbital.");
    }

    private void RegisterNoShow(OrbitalNoShowReason reason, string details)
    {
        _noShowCounters[reason] = _noShowCounters.TryGetValue(reason, out var count) ? count + 1 : 1;
        var reasonCount = _noShowCounters[reason];
        if (reasonCount == 1 || reasonCount % 10 == 0)
        {
            _ = _logger?.InfoAsync($"Orbital no-show: reason={reason}, count={reasonCount}, details={details}.");
        }
    }

    private void ApplyDynamicSuppression(string source)
    {
        var seconds = _behaviorProfile.ReopenCooldown.TotalSeconds
            + (_lastQuickDismissStreak * 9)
            + (_ignoredReminderCount * 6);
        _orbitalSuppressedUntilUtc = DateTimeOffset.UtcNow.AddSeconds(seconds);
        _ = _logger?.InfoAsync($"Orbital suppression: source={source}, seconds={seconds:F0}, streak={_lastQuickDismissStreak}, ignored={_ignoredReminderCount}.");
    }

    private bool ShouldSuppressForForegroundContext(out string reason)
    {
        reason = string.Empty;
        if (!_behaviorProfile.EnableFocusSuppression)
        {
            return false;
        }

        if (NativeMethods.IsForegroundWindowFullscreen())
        {
            reason = "foreground fullscreen";
            return true;
        }

        if (!NativeMethods.TryGetForegroundWindowProcessId(out var processId))
        {
            return false;
        }

        try
        {
            var process = Process.GetProcessById(processId);
            var name = process.ProcessName;
            if (!_focusSuppressedProcesses.Contains(name))
            {
                return false;
            }

            reason = $"focus process suppressed ({name})";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<RadialMenuItemDefinition> GetVisibleRadialMenuItems(IReadOnlyList<WorkAction>? precomputedAllowedActions = null)
    {
        if (_trackingViewModel is null)
        {
            return [];
        }

        var allowedActions = precomputedAllowedActions ?? _trackingViewModel.GetAllowedActions();
        return FilterRadialMenuItems(_radialMenuItems, allowedActions);
    }

    private static IReadOnlyList<RadialMenuItemDefinition> FilterRadialMenuItems(
        IReadOnlyList<RadialMenuItemDefinition> items,
        IReadOnlyList<WorkAction> allowedActions)
    {
        var filtered = new List<RadialMenuItemDefinition>();

        foreach (var item in items)
        {
            var filteredChildren = FilterRadialMenuItems(item.Children, allowedActions);
            var isDirectActionAllowed = IsMenuActionAllowed(item.ActionKey, allowedActions);

            if (!isDirectActionAllowed && filteredChildren.Count == 0)
            {
                continue;
            }

            filtered.Add(new RadialMenuItemDefinition
            {
                Label = item.Label,
                ActionKey = item.ActionKey,
                ColorHex = item.ColorHex,
                Children = [.. filteredChildren]
            });
        }

        return filtered;
    }

    private static bool IsMenuActionAllowed(string actionKey, IReadOnlyList<WorkAction> allowedActions)
    {
        if (string.IsNullOrWhiteSpace(actionKey))
        {
            return false;
        }

        return actionKey switch
        {
            "start_work" => allowedActions.Contains(WorkAction.StartWork),
            "end_work" => allowedActions.Contains(WorkAction.EndWork),
            "break_flexible" => allowedActions.Contains(WorkAction.StartFlexibleBreak),
            "break_lunch" => allowedActions.Contains(WorkAction.StartFixedBreak),
            "resume_work" => allowedActions.Contains(WorkAction.ResumeWork),
            "pause" or "project" or "reason" => false,
            _ when actionKey.StartsWith("project:", StringComparison.OrdinalIgnoreCase) =>
                allowedActions.Contains(WorkAction.StartWork)
                || allowedActions.Contains(WorkAction.ResumeWork)
                || allowedActions.Contains(WorkAction.SwitchContext),
            _ when actionKey.StartsWith("reason:", StringComparison.OrdinalIgnoreCase) =>
                allowedActions.Contains(WorkAction.StartWork)
                || allowedActions.Contains(WorkAction.ResumeWork)
                || allowedActions.Contains(WorkAction.SwitchContext),
            _ => false
        };
    }

    private void EnsureOrbitalWindow()
    {
        if (_orbitalWindow is not null)
        {
            return;
        }

        _orbitalWindow = new OrbitalWindow();
        _orbitalWindow.MenuItemInvoked += OnMenuItemInvoked;
        _orbitalWindow.OverlayTelemetry += OnOrbitalOverlayTelemetry;
        _orbitalWindow.OverlayClosed += OnOrbitalOverlayClosed;
    }

    private void OnOrbitalOverlayClosed(object? sender, OrbitalOverlayClosedEventArgs e)
    {
        _isOrbitalInteractionPinned = false;
        _orbitalInteractionPinnedUntilUtc = DateTimeOffset.MinValue;
        _lastQuickDismissStreak = e.QuickDismissStreak;

        if (e.Reason == OrbitalHideReason.ItemInvoked)
        {
            _ignoredReminderCount = 0;
            _reminderLevel = OrbitalReminderLevel.DotOnly;
            _lastReminderEscalationUtc = DateTimeOffset.MinValue;
        }
        else if (e.Reason is OrbitalHideReason.PointerLeftThreshold or OrbitalHideReason.InactivityTimeout)
        {
            _ignoredReminderCount = Math.Min(_ignoredReminderCount + 1, 8);
            _reminderLevel = OrbitalReminderLevel.DotOnly;
            ApplyDynamicSuppression("overlay-dismissed");
        }

        _ = _logger?.InfoAsync(
            $"Orbital geschlossen: reason={e.Reason}, visibleMs={e.VisibleDuration.TotalMilliseconds:F0}, " +
            $"distance={e.LastDistanceFromAnchor:F1}, quickDismissStreak={e.QuickDismissStreak}, ignored={_ignoredReminderCount}.");
    }

    private void OnOrbitalOverlayTelemetry(object? sender, OrbitalTelemetryEventArgs e)
    {
        if (e.EventKind == OrbitalTelemetryEventKind.ShowSkippedAlreadyVisible)
        {
            _ = _logger?.InfoAsync(
                $"Orbital telemetry: showSkipped visibleOrAnimating=true anchor=({e.AnchorX},{e.AnchorY}) items={e.ItemCount}.");
            return;
        }

        if (e.EventKind == OrbitalTelemetryEventKind.Hidden && e.QuickDismissStreak >= 3)
        {
            _ = _logger?.WarnAsync(
                $"Orbital telemetry: frequent quick dismiss detected (streak={e.QuickDismissStreak}, " +
                $"durationMs={e.VisibleDuration.TotalMilliseconds:F0}, distance={e.DistanceFromAnchor:F1}, details={e.Details}).");
        }

        if (DateTimeOffset.UtcNow - _lastReminderEscalationUtc >= _behaviorProfile.ReminderEscalationCooldown && _ignoredReminderCount > 0)
        {
            _reminderLevel = OrbitalReminderLevel.Orbital;
            _lastReminderEscalationUtc = DateTimeOffset.UtcNow;
            _ = _logger?.InfoAsync("Reminder-Eskalation: Dot -> Orbital nach Cooldown.");
        }
    }

    private Icon? TryLoadIcon(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return new Icon(path);
        }
        catch (Exception ex)
        {
            _ = _logger?.ErrorAsync($"Icon konnte nicht geladen werden: {path}", ex);
            return null;
        }
    }

    private static string DescribeIconSource(Icon? icon, params string[] preferredPaths)
    {
        if (icon is null)
        {
            return "none";
        }

        foreach (var path in preferredPaths)
        {
            if (File.Exists(path))
            {
                return Path.GetFileName(path);
            }
        }

        return "system";
    }

    private void SetTrayText(string text)
    {
        if (_trayIcon is null)
        {
            return;
        }

        var safeText = text.Length <= 63 ? text : text[..63];
        _trayIcon.Text = safeText;
    }

    private bool AcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, out var createdNew);
        _ownsMutex = createdNew;
        return _ownsMutex;
    }

    private void ReleaseSingleInstance()
    {
        if (_singleInstanceMutex is null)
        {
            return;
        }

        if (_ownsMutex)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // Ignorieren: Shutdown läuft bereits.
            }
            _ownsMutex = false;
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
    }

    private void InitializeLogging()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowTracker",
            "logs");
        var logPath = Path.Combine(logDirectory, "app.log");
        _logger = new AppLogger(logPath);
        _ = _logger.InfoAsync("Logger initialisiert.");
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            _ = _logger?.ErrorAsync("DispatcherUnhandledException", args.Exception);
            if (_isShuttingDown)
            {
                args.Handled = true;
                return;
            }

            System.Windows.MessageBox.Show(
                "Ein unerwarteter UI-Fehler ist aufgetreten. FlowTracker wird beendet. Details siehe Log.",
                "FlowTracker Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex && _logger is not null)
            {
                _ = _logger.ErrorAsync("AppDomain.UnhandledException", ex);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _ = _logger?.ErrorAsync("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private void RequestShutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        Shutdown();
    }

    private enum OrbitalDisplayMode
    {
        None,
        ShowDot,
        ShowOrbital
    }

    private enum OrbitalReminderLevel
    {
        DotOnly,
        Orbital
    }

    private enum OrbitalNoShowReason
    {
        Shutdown,
        NotIdle,
        GhostMode,
        AwaitingPostSelection,
        Suppressed,
        FocusSensitiveContext,
        AlreadyVisible,
        NoAllowedActions,
        NoMousePosition,
        NoVisibleMenuItems
    }

    private readonly record struct OrbitalDisplayDecision(
        OrbitalDisplayMode Mode,
        string ReasonText,
        IReadOnlyList<WorkAction> AllowedActions,
        OrbitalNoShowReason? NoShowReason)
    {
        public static OrbitalDisplayDecision None(OrbitalNoShowReason reason, string details) =>
            new(OrbitalDisplayMode.None, details, [], reason);
    }
}

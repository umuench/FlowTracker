using FlowTracker.Interop;

namespace FlowTracker.Services;

/// <summary>
/// Überwacht in einem Hintergrund-Loop den System-Idle-Zustand und Mauspositionsänderungen.
/// </summary>
/// <remarks>
/// Der Service arbeitet polling-basiert mit <see cref="PeriodicTimer"/> und sendet nur Events bei Zustandsänderungen.
/// </remarks>
public sealed class IdleMonitorService(TimeSpan pollInterval, TimeSpan idleThreshold) : IAsyncDisposable
{
    private readonly TimeSpan _pollInterval = pollInterval;
    private readonly TimeSpan _idleThreshold = idleThreshold;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();
    private Task? _monitorTask;
    private bool _isIdle;
    private NativeMethods.Point _lastPoint;
    private bool _hasLastPoint;

    /// <summary>
    /// Wird ausgelöst, wenn der Idle-Zustand von aktiv zu idle (oder zurück) wechselt.
    /// </summary>
    public event EventHandler<IdleStateChangedEventArgs>? IdleStateChanged;

    /// <summary>
    /// Wird ausgelöst, wenn sich die Cursorposition seit dem letzten Poll geändert hat.
    /// </summary>
    public event EventHandler<MousePositionChangedEventArgs>? MousePositionChanged;

    /// <summary>
    /// Startet den Monitoring-Loop einmalig.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            _monitorTask ??= Task.Run(() => MonitorLoopAsync(_cts.Token));
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            // EVA:
            // E: Aktuelle Cursorposition und Idle-Dauer aus Win32.
            // V: Delta gegen letzten Poll bilden und Idle-Schwelle vergleichen.
            // A: Nur bei Änderung die jeweiligen Events publizieren.
            if (NativeMethods.TryGetCursorPosition(out var point))
            {
                var moved = !_hasLastPoint || point.X != _lastPoint.X || point.Y != _lastPoint.Y;
                if (moved)
                {
                    _lastPoint = point;
                    _hasLastPoint = true;
                    MousePositionChanged?.Invoke(this, new(point.X, point.Y, DateTimeOffset.UtcNow));
                }
            }

            if (!NativeMethods.TryGetIdleDuration(out var idleDuration))
            {
                continue;
            }

            var newIdleState = idleDuration >= _idleThreshold;
            if (newIdleState == _isIdle)
            {
                continue;
            }

            _isIdle = newIdleState;
            IdleStateChanged?.Invoke(this, new(_isIdle, idleDuration, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Stoppt den Monitoring-Loop und wartet auf geordnetes Beenden.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        Task? monitorTask;
        lock (_gate)
        {
            monitorTask = _monitorTask;
            _monitorTask = null;
        }

        if (monitorTask is not null)
        {
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Erwartetes Verhalten beim Shutdown.
            }
        }

        _cts.Dispose();
    }
}

/// <summary>
/// Eventdaten für einen Idle-Zustandswechsel.
/// </summary>
public readonly record struct IdleStateChangedEventArgs(
    bool IsIdle,
    TimeSpan IdleDuration,
    DateTimeOffset TimestampUtc);

/// <summary>
/// Eventdaten für eine Cursorpositionsänderung.
/// </summary>
public readonly record struct MousePositionChangedEventArgs(
    int X,
    int Y,
    DateTimeOffset TimestampUtc);

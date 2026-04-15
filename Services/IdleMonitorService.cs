using FlowTracker.Interop;

namespace FlowTracker.Services;

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

    public event EventHandler<IdleStateChangedEventArgs>? IdleStateChanged;
    public event EventHandler<MousePositionChangedEventArgs>? MousePositionChanged;

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

public readonly record struct IdleStateChangedEventArgs(
    bool IsIdle,
    TimeSpan IdleDuration,
    DateTimeOffset TimestampUtc);

public readonly record struct MousePositionChangedEventArgs(
    int X,
    int Y,
    DateTimeOffset TimestampUtc);

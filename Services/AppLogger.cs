using System.Text;
using System.IO;

namespace FlowTracker.Services;

public sealed class AppLogger
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppLogger(string logFilePath)
    {
        _logFilePath = logFilePath;
        var directory = Path.GetDirectoryName(_logFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string LogFilePath => _logFilePath;

    public Task InfoAsync(string message, CancellationToken cancellationToken = default) =>
        WriteCoreAsync("INFO", message, exception: null, cancellationToken);

    public Task WarnAsync(string message, CancellationToken cancellationToken = default) =>
        WriteCoreAsync("WARN", message, exception: null, cancellationToken);

    public Task ErrorAsync(string message, Exception exception, CancellationToken cancellationToken = default) =>
        WriteCoreAsync("ERROR", message, exception, cancellationToken);

    private async Task WriteCoreAsync(string level, string message, Exception? exception, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
        sb.Append(" [");
        sb.Append(level);
        sb.Append("] ");
        sb.AppendLine(message);

        if (exception is not null)
        {
            sb.AppendLine(exception.ToString());
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_logFilePath, sb.ToString(), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}

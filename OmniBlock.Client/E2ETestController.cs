using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Client;

internal sealed class E2ETestController : IDisposable
{
    public const int FailedExitCode = 1;
    public const int TimedOutExitCode = 124;

    private readonly object _gate = new();
    private readonly E2ETestLaunchOptions _options;
    private readonly Action _requestShutdown;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly System.Threading.Timer? _watchdog;
    private E2ETestResult? _result;

    public E2ETestController(E2ETestLaunchOptions options, Action requestShutdown)
    {
        _options = options;
        _requestShutdown = requestShutdown;
        _watchdog = new System.Threading.Timer(
            _ => Complete("timeout", $"Timed out after {options.Timeout.TotalSeconds:g} seconds", TimedOutExitCode),
            null,
            options.Timeout,
            System.Threading.Timeout.InfiniteTimeSpan);
    }

    public int ExitCode
    {
        get
        {
            lock (_gate)
            {
                return _result?.ExitCode ?? FailedExitCode;
            }
        }
    }

    public bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                return _result != null;
            }
        }
    }

    public void Pass() => Complete("passed", null, 0);

    public void Fail(string reason, int exitCode = FailedExitCode) =>
        Complete("failed", string.IsNullOrWhiteSpace(reason) ? "Test failed" : reason, exitCode);

    public void EnsureCompleted()
    {
        Complete("failed", "Client exited before OMNI.test.pass() was called", FailedExitCode);
    }

    private void Complete(string status, string? reason, int exitCode)
    {
        E2ETestResult result;
        lock (_gate)
        {
            if (_result != null)
            {
                return;
            }

            result = new E2ETestResult(
                status,
                reason,
                exitCode,
                _stopwatch.Elapsed.TotalSeconds,
                _options.Script.Path);
            _result = result;
        }

        _watchdog?.Change(System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
        ILogger logger = Log.Instance.For<E2ETestController>();
        if (exitCode == 0)
            logger.LogInformation("E2E test passed in {Duration:F3}s", result.DurationSeconds);
        else
            logger.LogError("E2E test {Status}: {Reason} (exit code {ExitCode})", status, reason, exitCode);
        WriteArtifacts(result);
        _requestShutdown();
    }

    private void WriteArtifacts(E2ETestResult result)
    {
        try
        {
            Directory.CreateDirectory(_options.ArtifactsPath);
            string json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            File.WriteAllText(Path.Combine(_options.ArtifactsPath, "result.json"), json + Environment.NewLine);

            StringBuilder log = new();
            foreach (LogEntry entry in Log.Instance.GetRecentEntries())
            {
                log.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(entry.Level).Append("] ")
                    .Append(entry.Category).Append(": ").AppendLine(entry.Message);
                if (entry.Exception != null)
                {
                    log.AppendLine(entry.Exception.ToString());
                }
            }
            File.WriteAllText(Path.Combine(_options.ArtifactsPath, "client.log"), log.ToString());
        }
        catch (Exception ex)
        {
            Log.Instance.For<E2ETestController>().LogError(ex, "Failed to write E2E artifacts to {Path}", _options.ArtifactsPath);
        }
    }

    public void Dispose() => _watchdog?.Dispose();
}

internal sealed record E2ETestResult(
    string Status,
    string? Reason,
    int ExitCode,
    double DurationSeconds,
    string ScriptPath);

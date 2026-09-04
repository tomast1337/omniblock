using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmniBlock.Client.Diagnostics;

internal readonly record struct DebugSystemSnapshot(
    string GpuName,
    string GpuVram,
    string OpenGlVersion,
    string GlslVersion,
    string DriverVersion,
    string CpuName,
    int CpuCoreCount,
    string OsDescription,
    string DotNetRuntime)
{
    public static DebugSystemSnapshot Empty { get; } = new(
        DebugTelemetry.UnknownValue,
        DebugTelemetry.UnknownValue,
        DebugTelemetry.UnknownValue,
        DebugTelemetry.UnknownValue,
        DebugTelemetry.UnknownValue,
        DebugTelemetry.UnknownValue,
        Environment.ProcessorCount,
        DebugTelemetry.UnknownValue,
        DebugTelemetry.UnknownValue);
}

internal readonly record struct DebugFrameStatsSnapshot(
    int SampleCount,
    double AverageFrameTimeMs,
    double MinFps,
    double MaxFps)
{
    public static DebugFrameStatsSnapshot Empty { get; } = new(0, 0.0D, 0.0D, 0.0D);
    public bool HasData => SampleCount > 0;
}

internal sealed class DebugTelemetry
{
    internal const string UnknownValue = "N/A";
    private const int FrameHistorySize = 600;

    private readonly double[] _frameTimesMs = new double[FrameHistorySize];

    private int _frameSampleCount;
    private int _nextFrameIndex;

    public DebugSystemSnapshot SystemSnapshot { get; } = DebugSystemSnapshot.Empty;

    public void RecordFrameTime(double frameTimeMs)
    {
        if (double.IsNaN(frameTimeMs) || double.IsInfinity(frameTimeMs) || frameTimeMs <= 0.0D)
        {
            return;
        }

        _frameTimesMs[_nextFrameIndex] = frameTimeMs;
        _nextFrameIndex = (_nextFrameIndex + 1) % _frameTimesMs.Length;
        if (_frameSampleCount < _frameTimesMs.Length)
        {
            _frameSampleCount++;
        }
    }

    public DebugFrameStatsSnapshot GetFrameStatsSnapshot()
    {
        if (_frameSampleCount == 0)
        {
            return DebugFrameStatsSnapshot.Empty;
        }

        var samples = new double[_frameSampleCount];
        var start = (_nextFrameIndex - _frameSampleCount + _frameTimesMs.Length) % _frameTimesMs.Length;
        for (var i = 0; i < _frameSampleCount; i++)
        {
            samples[i] = _frameTimesMs[(start + i) % _frameTimesMs.Length];
        }

        var totalMs = 0.0D;
        var minFrameTimeMs = double.MaxValue;
        var maxFrameTimeMs = double.MinValue;
        foreach (var sample in samples)
        {
            totalMs += sample;
            if (sample < minFrameTimeMs)
            {
                minFrameTimeMs = sample;
            }

            if (sample > maxFrameTimeMs)
            {
                maxFrameTimeMs = sample;
            }
        }

        var averageFrameMs = totalMs / _frameSampleCount;

        return new DebugFrameStatsSnapshot(
            _frameSampleCount,
            averageFrameMs,
            ToFps(maxFrameTimeMs),
            ToFps(minFrameTimeMs));
    }

    private static double ToFps(double frameTimeMs)
    {
        if (frameTimeMs <= 0.0D || double.IsNaN(frameTimeMs) || double.IsInfinity(frameTimeMs))
        {
            return 0.0D;
        }

        return 1000.0D / frameTimeMs;
    }

    private static string GetCpuName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var processorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            if (!string.IsNullOrWhiteSpace(processorIdentifier))
            {
                return processorIdentifier.Trim();
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var cpuName = TryReadValueFromFile("/proc/cpuinfo", "model name");
            if (!string.IsNullOrWhiteSpace(cpuName))
            {
                return cpuName;
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var cpuName = TryRunCommand("sysctl", "-n machdep.cpu.brand_string");
            if (!string.IsNullOrWhiteSpace(cpuName))
            {
                return cpuName;
            }
        }

        return UnknownValue;
    }

    private static string? TryReadValueFromFile(string path, string key)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var separator = line.IndexOf(':');
                return separator >= 0 ? line[(separator + 1)..].Trim() : line.Trim();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryRunCommand(string fileName, string arguments)
    {
        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!process.Start())
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(1500))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }

                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            var value = stdout.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeValue(string? value) => string.IsNullOrWhiteSpace(value) ? UnknownValue : value.Trim();
}

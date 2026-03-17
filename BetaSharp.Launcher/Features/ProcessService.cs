using System;
using System.Diagnostics;
using System.IO;

namespace BetaSharp.Launcher.Features;

internal sealed class ProcessService
{
    public Process StartAsync(string directory, string file, params string[] args)
    {
        var info = new ProcessStartInfo
        {
            Arguments = string.Join(" ", args),
            CreateNoWindow = false,
            FileName = Path.Combine(directory, $"{nameof(BetaSharp)}.{file}"),
            WorkingDirectory = directory
        };

        var process = Process.Start(info);

        ArgumentNullException.ThrowIfNull(process);

        return process;
    }
}

using System;
using System.Diagnostics;
using System.IO;

namespace BetaSharp.Launcher.Features;

internal sealed class ProcessService
{
    public Process StartAsync(string directory, string path, params string[] args)
    {
        var info = new ProcessStartInfo
        {
            Arguments = string.Join(" ", args),
            CreateNoWindow = true,
            FileName = Path.Combine(directory, path),
            WorkingDirectory = directory
        };

        var process = Process.Start(info);

        ArgumentNullException.ThrowIfNull(process);

        return process;
    }
}

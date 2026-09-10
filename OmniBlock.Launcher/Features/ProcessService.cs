using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace OmniBlock.Launcher.Features;

internal sealed class ProcessService(MinecraftService minecraftService)
{
    public async Task<Process> StartAsync(Kind kind, params string[] args)
    {
        var suffix = kind.ToString();
        var directory = Path.Combine(AppContext.BaseDirectory, suffix);

        var redirect = kind is Kind.Server;

        await minecraftService.DownloadAsync(directory);

        var info = new ProcessStartInfo
        {
            CreateNoWindow = true,
            FileName = Path.Combine(directory, $"{nameof(OmniBlock)}.{suffix}"),
            RedirectStandardInput = redirect,
            RedirectStandardOutput = redirect,
            WorkingDirectory = directory
        };

        // ArgumentList quotes each element itself; a session token or player name could contain a
        // space, which a naive string.Join(" ", args) would split into two arguments.
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        var process = Process.Start(info);

        ArgumentNullException.ThrowIfNull(process);

        return process;
    }
}

internal enum Kind
{
    Client,
    Server
}

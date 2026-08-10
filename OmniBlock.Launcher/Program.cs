using System;
using Avalonia;
using Serilog;

namespace OmniBlock.Launcher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppBuilder
                .Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "An unhandled exception occurred");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}

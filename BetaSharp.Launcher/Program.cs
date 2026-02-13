using Avalonia;
using System;
using BetaSharp.Avalonia;

Start(args);

return;

[STAThread]
static void Start(string[] args)
{
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(args);
}
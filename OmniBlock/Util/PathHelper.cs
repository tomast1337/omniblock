using System.Runtime.InteropServices;

namespace OmniBlock.Util;

public static class PathHelper
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool s_isMacOs = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string GetAppDir(string appName)
    {
        var dataHome = Environment.GetEnvironmentVariable("OMNIBLOCK_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(dataHome))
        {
            var isolatedPath = Path.Combine(Path.GetFullPath(dataHome), appName);
            Directory.CreateDirectory(isolatedPath);
            return isolatedPath;
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userHome))
            userHome = ".";

        string path;
        if (s_isWindows)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            path = Path.Combine(appData, "." + appName);
        }
        else if (s_isMacOs)
        {
            path = Path.Combine(userHome, "Library", "Application Support", appName);
        }
        else
        {
            var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

            if (!string.IsNullOrEmpty(xdgData))
            {
                path = Path.Combine(xdgData, appName);
            }
            else
            {
                path = Path.Combine(userHome, ".local", "share", appName);
            }

            MigrateLegacyLinuxDir(userHome, appName, path);
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static void MigrateLegacyLinuxDir(string userHome, string appName, string newPath)
    {
        var oldPath = Path.Combine(userHome, "." + appName);

        if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            Directory.Move(oldPath, newPath);
        }
    }
}

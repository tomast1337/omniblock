using System.Globalization;
using System.Text;

namespace OmniBlock.Client;

/// <summary>Command-line configuration resolved before the client is constructed.</summary>
internal sealed record ClientLaunchOptions(
    string Username,
    string SessionToken,
    bool Debug,
    StartupScript? StartupScript,
    E2ETestLaunchOptions? E2ETest)
{
    public static ClientLaunchOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? username = null;
        string? token = null;
        var debug = false;
        string? startupScriptPath = null;
        string? e2eScriptPath = null;
        double e2eTimeoutSeconds = 60;
        var e2eTimeoutSpecified = false;
        string? e2eArtifactsPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--username":
                    username = ReadValue(args, ref i, "--username");
                    break;
                case "--token":
                    token = ReadValue(args, ref i, "--token");
                    break;
                case "--startup-script":
                    startupScriptPath = ReadValue(args, ref i, "--startup-script");
                    break;
                case "--e2e-script":
                    e2eScriptPath = ReadValue(args, ref i, "--e2e-script");
                    break;
                case "--e2e-timeout":
                    e2eTimeoutSpecified = true;
                    var timeout = ReadValue(args, ref i, "--e2e-timeout");
                    if (!double.TryParse(timeout, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out e2eTimeoutSeconds) ||
                        !double.IsFinite(e2eTimeoutSeconds) || e2eTimeoutSeconds <= 0 || e2eTimeoutSeconds > 86_400)
                    {
                        throw new ArgumentException("--e2e-timeout requires a finite number greater than zero and no more than 86400.", nameof(args));
                    }

                    break;
                case "--e2e-artifacts":
                    e2eArtifactsPath = ReadValue(args, ref i, "--e2e-artifacts");
                    break;
                case "--debug":
                    debug = true;
                    break;
            }
        }

        username ??= $"Player{Random.Shared.Next()}";
        token ??= "-";
        PlayerNameValidator.Validate(username);

        if (startupScriptPath != null && e2eScriptPath != null)
        {
            throw new ArgumentException("--startup-script and --e2e-script cannot be used together.", nameof(args));
        }

        if (e2eScriptPath == null && (e2eArtifactsPath != null || e2eTimeoutSpecified))
        {
            throw new ArgumentException("--e2e-timeout and --e2e-artifacts require --e2e-script.", nameof(args));
        }

        var startupScript = startupScriptPath == null
            ? null
            : LoadStartupScript(startupScriptPath);

        var e2eTest = e2eScriptPath == null
            ? null
            : new E2ETestLaunchOptions(
                LoadStartupScript(e2eScriptPath),
                TimeSpan.FromSeconds(e2eTimeoutSeconds),
                Path.GetFullPath(e2eArtifactsPath ?? "e2e-artifacts"));

        return new ClientLaunchOptions(username, token, debug, startupScript, e2eTest);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.", nameof(args));
        }

        return args[++index];
    }

    private static StartupScript LoadStartupScript(string path)
    {
        var fullPath = Path.GetFullPath(path);

        try
        {
            return new StartupScript(fullPath, File.ReadAllText(fullPath, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"Could not load startup script '{fullPath}': {ex.Message}", nameof(path), ex);
        }
    }
}

internal sealed record StartupScript(string Path, string Source);

internal sealed record E2ETestLaunchOptions(StartupScript Script, TimeSpan Timeout, string ArtifactsPath);

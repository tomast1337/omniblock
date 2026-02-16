using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace BetaSharp.Launcher.Features;

// More decoupling and overall cleaning.
internal sealed class AuthenticationService
{
    private readonly SystemWebViewOptions _webViewOptions;
    private readonly IPublicClientApplication _application;

    // Need better way for storing the HTML responses.
    public AuthenticationService()
    {
        const string success = """
                               <!DOCTYPE html><html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>BetaSharp</title><style>body{margin:0;padding:0;background-color:#000;display:flex;justify-content:center;align-items:center;height:100vh;font-family:Arial,sans-serif}p{color:#fff;font-size:.85rem;font-weight:400;text-align:center;opacity:.5}</style></head><body><p>You can close this tab now</p></body></html>
                               """;

        const string failure = """
                               <!DOCTYPE html><html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>BetaSharp</title><style>body{margin:0;padding:0;background-color:#000;display:flex;justify-content:center;align-items:center;height:100vh;font-family:Arial,sans-serif}p{color:orange;font-size:1rem;font-weight:400;text-align:center}a{color:#58a6ff;text-decoration:none}a:hover{text-decoration:underline}</style></head><body><p>Failed to authenticate please raise an issue <a href="https://github.com/Fazin85/betasharp/issues" target="_blank">here</a></p></body></html>
                               """;

        _webViewOptions = new SystemWebViewOptions { HtmlMessageSuccess = success, HtmlMessageError = failure };

        // Probably not the best idea to use Prism's ID?
        var builder = PublicClientApplicationBuilder
            .Create("C36A9FB6-4F2A-41FF-90BD-AE7CC92031EB")
            .WithAuthority("https://login.microsoftonline.com/consumers")
            .WithRedirectUri("http://localhost");

        _application = builder.Build();
    }

    public async Task InitializeAsync()
    {
        string path = Path.Combine(MsalCacheHelper.UserRootDirectory, "betasharp.launcher.cache");

        var properties = new StorageCreationPropertiesBuilder(Path.GetFileName(path), Path.GetDirectoryName(path))
            .WithLinuxKeyring(
                "betasharp.launcher",
                MsalCacheHelper.LinuxKeyRingDefaultCollection,
                "MSAL token cache for BetaSharp's launcher",
                default,
                default)
            .WithMacKeyChain("betasharp.launcher", "betasharp")
            .Build();

        var helper = await MsalCacheHelper.CreateAsync(properties);
        helper.RegisterCache(_application.UserTokenCache);
    }

    public async Task<string> AuthenticateAsync()
    {
        string[] scopes = ["XboxLive.signin offline_access"];

        AuthenticationResult? result;

        try
        {
            var accounts = await _application.GetAccountsAsync();

            // Let user choose which account to authenticate with?
            result = await _application
                .AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                .ExecuteAsync();

            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            // Log it?
        }

        // Find out a way to use system brokers.
        result = await _application
            .AcquireTokenInteractive(scopes)
            .WithUseEmbeddedWebView(false)
            .WithSystemWebViewOptions(_webViewOptions)
            .ExecuteAsync();

        return result.AccessToken;
    }

    public async Task SignOutAsync()
    {
        var accounts = await _application.GetAccountsAsync();

        foreach (var account in accounts)
        {
            await _application.RemoveAsync(account);
        }
    }
}

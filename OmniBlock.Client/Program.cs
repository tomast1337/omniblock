using OmniBlock;
using OmniBlock.Util;
using OmniBlock.Luau;
using Microsoft.Extensions.Logging;

Log.Instance.Initialize(PathHelper.GetAppDir(nameof(OmniBlock)));
AssetManager.Initialize(AssetManager.AssetProfile.Full);

try
{
    if (!LuauQuickRun.IsAvailable())
    {
        throw new DllNotFoundException(
            "Required Luau runtime 'omniblock_luau' was not found. " +
            "For a source checkout, run native/luau/build-local.sh and rebuild the client. " +
            "Packaged builds must include the native library for their runtime identifier.");
    }

    OmniBlock.Client.OmniBlock.Startup(args);
}
catch (Exception exception)
{
    Log.Instance.For<Program>().LogError(exception, "Unhandled exception occured!");
    Environment.ExitCode = 1;

#if DEBUG
    throw;
#endif
}

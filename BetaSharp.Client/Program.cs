using OmniBlock;
using OmniBlock.Util;
using Microsoft.Extensions.Logging;

Log.Instance.Initialize(PathHelper.GetAppDir(nameof(OmniBlock)));
AssetManager.Initialize(AssetManager.AssetProfile.Full);

try
{
    OmniBlock.Client.OmniBlock.Startup(args);
}
catch (Exception exception)
{
    Log.Instance.For<Program>().LogError(exception, "Unhandled exception occured!");

#if DEBUG
    throw;
#endif
}

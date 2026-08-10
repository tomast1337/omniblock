namespace OmniBlock.Launcher.Features.Home;

internal sealed class OmniBlockRelease(string name, string date, string url)
{
    public string Name => name;

    public string Date => date;

    public string Url => url;
}

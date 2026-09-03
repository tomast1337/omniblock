using OmniBlock.Client.Rendering;
using OmniBlock.Client.UI.Screens;
using OmniBlock.Stats;
using OmniBlock.Registries;

namespace OmniBlock.Client.Network;

public sealed class ClientNetworkContext(
    IClientPlayerHost playerHost,
    IWorldHost worldHost,
    IScreenNavigator navigator,
    Session session,
    StatFileWriter statFileWriter,
    ParticleManager particleManager,
    Action<string> addChatMessage,
    IClientNetworkFactory factory,
    string chunkCacheDirectory,
    ContentRuntime content,
    Action<ContentRuntime>? stageContent = null)
{
    /// <summary>
    ///     Where per-server chunk caches live. Passed in rather than read from a static so a test can
    ///     point it somewhere disposable, and so nothing in the network layer has to know how the
    ///     client lays out its data directory.
    /// </summary>
    public string ChunkCacheDirectory => chunkCacheDirectory;

    public IClientPlayerHost PlayerHost => playerHost;
    public IWorldHost WorldHost => worldHost;
    public IScreenNavigator Navigator => navigator;
    public Session Session => session;
    public StatFileWriter StatFileWriter => statFileWriter;
    public ParticleManager ParticleManager => particleManager;
    public Action<string> AddChatMessage => addChatMessage;
    public IClientNetworkFactory Factory => factory;
    public ContentRuntime Content => content;
    public Action<ContentRuntime> StageContent => stageContent ?? (_ => { });
}

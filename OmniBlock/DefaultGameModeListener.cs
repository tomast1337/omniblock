using Microsoft.Extensions.Logging;
using OmniBlock.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using OmniBlock.Registries;
using OmniBlock.Server;

namespace OmniBlock;

internal sealed class DefaultGameModeListener(OmniBlockServer server) : IRegistryReloadListener
{
    private static readonly ILogger<DefaultGameModeListener> s_logger = Log.Instance.For<DefaultGameModeListener>();

    public void OnRegistriesRebuilt(RegistryAccess registryAccess)
    {
        var resolved = ResolveDefaultGameMode(
            registryAccess.GetOrThrow(RegistryKeys.GameModes),
            server.config.GetDefaultGamemode("survival"));

        if (resolved == null)
        {
            s_logger.LogError("No game modes are registered.");
        }
        else
        {
            server.DefaultGameMode = resolved;
        }
    }

    public Packet[] GetSyncPackets(RegistryAccess registries, ServerPlayerEntity player)
    {
        var gameModes = registries.GetOrThrow(RegistryKeys.GameModes).AsAssetLoader();

        if (gameModes.TryGetHolder(player.GameMode.Name, out var updated))
        {
            player.GameModeHolder = updated;
        }
        else
        {
            player.GameModeHolder = server.DefaultGameMode;
        }

        player.ConnectedNetworkHandler.SendMessage(new PlayerGameModeUpdateMessage
        {
            GameModeNamespace = player.GameMode.Namespace.ToString(),
            GameModeName = player.GameMode.Name
        });

        return [];
    }

    /// <summary>
    ///     Resolves which game mode should be the server default.
    ///     Tries <paramref name="configuredName" /> first, then "survival", then "default",
    ///     then the first registered entry. Returns <c>null</c> if no game modes exist.
    /// </summary>
    internal static Holder<GameMode>? ResolveDefaultGameMode(
        IReadableRegistry<GameMode> registry, string configuredName)
    {
        var loader = registry.AsAssetLoader();

        if (!string.IsNullOrEmpty(configuredName) && loader.TryGetHolder(configuredName, out var named))
            return named;

        if (loader.TryGetHolder("survival", out var survival))
            return survival;

        if (loader.TryGetHolder("default", out var defaultMode))
            return defaultMode;

        var firstKey = registry.Keys.FirstOrDefault();
        return firstKey != null ? registry.Get(firstKey) : null;
    }
}

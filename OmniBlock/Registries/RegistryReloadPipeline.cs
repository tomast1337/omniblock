using OmniBlock.Entities;
using OmniBlock.Network.Messages;

namespace OmniBlock.Registries;

/// <summary>
///     Orchestrates the full reload-sync pipeline.
/// </summary>
public static class RegistryReloadPipeline
{
    /// <summary>
    ///     Sends all reloadable registry data messages and listener migration packets to
    ///     each connected player.
    /// </summary>
    public static void SyncToPlayers(
        RegistryAccess registries,
        IReadOnlyList<IRegistryReloadListener> listeners,
        IEnumerable<ServerPlayerEntity> players)
    {
        List<RegistryDataMessage> syncMessages = [.. registries.BuildSyncMessages()];

        foreach (var player in players)
        {
            // Send registry data messages directly — no bundling needed since messages
            // are individually framed and the stream alignment guarantee comes from the
            // length prefix, not from a wrapper.
            foreach (var message in syncMessages)
            {
                player.ConnectedNetworkHandler.SendMessage(message);
            }

            // Collect per-player migration packets from each listener
            foreach (var listener in listeners)
            {
                var packets = listener.GetSyncPackets(registries, player);

                foreach (var packet in packets)
                {
                    player.ConnectedNetworkHandler.SendPacket(packet);
                }
            }

            player.ConnectedNetworkHandler.SendMessage(new FinishConfigurationMessage());
        }
    }
}

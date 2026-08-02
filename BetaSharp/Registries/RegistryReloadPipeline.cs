using BetaSharp.Entities;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Server;

namespace BetaSharp.Registries;

/// <summary>
/// Orchestrates the full reload-sync pipeline.
/// </summary>
public static class RegistryReloadPipeline
{
    /// <summary>
    /// Sends all reloadable registry data messages and listener migration packets to
    /// each connected player.
    /// </summary>
    public static void SyncToPlayers(
        RegistryAccess registries,
        IReadOnlyList<IRegistryReloadListener> listeners,
        IEnumerable<ServerPlayerEntity> players)
    {
        List<RegistryDataMessage> syncMessages = [.. registries.BuildSyncMessages()];

        foreach (ServerPlayerEntity player in players)
        {
            // Send registry data messages directly — no bundling needed since messages
            // are individually framed and the stream alignment guarantee comes from the
            // length prefix, not from a wrapper.
            foreach (RegistryDataMessage message in syncMessages)
            {
                player.NetworkHandler.SendMessage(message);
            }

            // Collect per-player migration packets from each listener
            foreach (IRegistryReloadListener listener in listeners)
            {
                Packet[] packets = listener.GetSyncPackets(registries, player);

                foreach (Packet packet in packets)
                {
                    player.NetworkHandler.SendPacket(packet);
                }
            }

            player.NetworkHandler.SendMessage(new FinishConfigurationMessage());
        }
    }
}

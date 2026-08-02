using BetaSharp.Client.Network;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Tests;

public class RaceConditionTests
{
    [Fact]
    public void RegistrySync_Invalidates_Removed_Holders_Leading_To_Potential_Crash()
    {
        var registries = new ClientRegistryAccess();
        RegistryKey<GameMode> key = RegistryKeys.GameModes;

        var message1 = RegistryDataMessage.FromRegistry(key, BuildRegistry("survival", "deleted_mode"));
        registries.Accumulate(message1);

        Holder<GameMode> survivalHolder = registries.Get(key, "survival")!;
        Holder<GameMode> deletedHolder = registries.Get(key, "deleted_mode")!;

        Assert.NotNull(survivalHolder.Value);
        Assert.NotNull(deletedHolder.Value);

        var message2 = RegistryDataMessage.FromRegistry(key, BuildRegistry("survival"));
        registries.Accumulate(message2);

        _ = registries.Get(key, "survival");

        Assert.True(deletedHolder.IsInvalid);
        Assert.Throws<InvalidOperationException>(() => _ = deletedHolder.Value);
    }

    [Fact]
    public void Sequential_Message_Delivery_Prevents_RaceCondition_By_Updating_State_Atomically()
    {
        var registries = new ClientRegistryAccess();
        RegistryKey<GameMode> key = RegistryKeys.GameModes;

        registries.Accumulate(RegistryDataMessage.FromRegistry(key, BuildRegistry("survival", "deleted_mode")));
        Holder<GameMode> initialHolder = registries.Get(key, "deleted_mode")!;
        Holder<GameMode> currentPlayerHolder = initialHolder;

        // Simulate sequential message delivery: registry data arrives, then migration packet.
        registries.Accumulate(RegistryDataMessage.FromRegistry(key, BuildRegistry("survival")));

        var migrationPacket = PlayerGameModeUpdateS2CPacket.Get(new GameMode { Name = "survival", Namespace = Namespace.BetaSharp });

        // This simulates ClientNetworkHandler.onPlayerGameModeUpdate
        Holder<GameMode> updated = registries.Get(key, migrationPacket.GameModeName)!;
        currentPlayerHolder = updated;

        Assert.True(initialHolder.IsInvalid, "The old holder should have been invalidated during the merge.");
        Assert.False(currentPlayerHolder.IsInvalid, "The current holder should be the newly acquired valid one.");
        Assert.Equal("survival", currentPlayerHolder.Value.Name);
    }

    private static DataAssetLoader<GameMode> BuildRegistry(params string[] names)
    {
        var loader = new DataAssetLoader<GameMode>("gamemode", LoadLocations.None, allowUnhandled: false);
        foreach (string name in names)
        {
            var rl = new ResourceLocation(Namespace.BetaSharp, name);
            loader.Assets.Add(rl, new Holder<GameMode>(new GameMode { Name = name }));
        }
        return loader;
    }
}

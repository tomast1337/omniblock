using OmniBlock.Client.Network;
using OmniBlock.Network.Messages;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests;

public class RaceConditionTests
{
    [Fact]
    public void RegistrySync_Invalidates_Removed_Holders_Leading_To_Potential_Crash()
    {
        var registries = new ClientRegistryAccess(ContentRuntime.Current.Items);
        var key = RegistryKeys.GameModes;

        var message1 = RegistryDataMessage.FromRegistry(key, BuildRegistry("survival", "deleted_mode"));
        registries.Accumulate(message1);

        var survivalHolder = registries.Get(key, "survival")!;
        var deletedHolder = registries.Get(key, "deleted_mode")!;

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
        var registries = new ClientRegistryAccess(ContentRuntime.Current.Items);
        var key = RegistryKeys.GameModes;

        registries.Accumulate(RegistryDataMessage.FromRegistry(key, BuildRegistry("survival", "deleted_mode")));
        var initialHolder = registries.Get(key, "deleted_mode")!;
        var currentPlayerHolder = initialHolder;

        // Simulate sequential message delivery: registry data arrives, then migration packet.
        registries.Accumulate(RegistryDataMessage.FromRegistry(key, BuildRegistry("survival")));

        var migrationMessage = new PlayerGameModeUpdateMessage
        {
            GameModeNamespace = Namespace.OmniBlock.ToString(),
            GameModeName = "survival"
        };

        // This simulates ClientNetworkHandler.onPlayerGameModeUpdate
        var updated = registries.Get(key, migrationMessage.GameModeName)!;
        currentPlayerHolder = updated;

        Assert.True(initialHolder.IsInvalid, "The old holder should have been invalidated during the merge.");
        Assert.False(currentPlayerHolder.IsInvalid, "The current holder should be the newly acquired valid one.");
        Assert.Equal("survival", currentPlayerHolder.Value.Name);
    }

    private static DataAssetLoader<GameMode> BuildRegistry(params string[] names)
    {
        var loader = new DataAssetLoader<GameMode>("gamemode", LoadLocations.None, false);
        foreach (var name in names)
        {
            var rl = new ResourceLocation(Namespace.OmniBlock, name);
            loader.Assets.Add(rl, new Holder<GameMode>(new GameMode
            {
                Name = name
            }));
        }

        return loader;
    }
}

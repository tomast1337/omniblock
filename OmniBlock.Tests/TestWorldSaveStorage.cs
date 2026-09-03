using System.Collections.Generic;
using System.IO;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;
using Xunit;

namespace OmniBlock.Tests;

public class TestWorldSaveStorage
{
    [Fact]
    public void DifferentPlayerNamesDoNotShareSavedData()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "OmniBlockTestWorldPerPlayer");
        if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);

        var storage = new RegionWorldStorage(baseDir, "world", true);
        FakeWorldContext world = new();

        var alice = new TestEntityPlayer(world) { Name = "Alice" };
        alice.Inventory.Main[0] = new ItemStack(ContentRuntime.Current.Items.Get("omniblock:diamond"), 5);
        storage.SavePlayerData(alice);

        var bob = new TestEntityPlayer(world) { Name = "Bob" };
        bob.Inventory.Main[0] = new ItemStack(ContentRuntime.Current.Items.Get("omniblock:arrow"), 1);
        storage.SavePlayerData(bob);

        var aliceReload = new TestEntityPlayer(world) { Name = "Alice" };
        storage.LoadPlayerData(aliceReload);
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:diamond"), aliceReload.Inventory.Main[0]?.GetItem());
        Assert.Equal(5, aliceReload.Inventory.Main[0]?.Count);

        var bobReload = new TestEntityPlayer(world) { Name = "Bob" };
        storage.LoadPlayerData(bobReload);
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:arrow"), bobReload.Inventory.Main[0]?.GetItem());
        Assert.Equal(1, bobReload.Inventory.Main[0]?.Count);

        Directory.Delete(baseDir, true);
    }

    [Fact]
    public void TestSavePlayerDataFallback()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "OmniBlockTestWorld");
        if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);

        var storage = new RegionWorldStorage(baseDir, "world", true);
        var props = new WorldProperties(1234, "TestWorld");

        // Save dummy player data to players/TestPlayer.dat
        var playersDir = new DirectoryInfo(Path.Combine(baseDir, "world", "players"));
        playersDir.Create();
        var dummyPlayerNbt = new NBTTagCompound();
        dummyPlayerNbt.SetString("TestMarker", "ImHere");

        var mockPos = new NBTTagList();
        mockPos.SetTag(new NBTTagDouble(1.0D));
        mockPos.SetTag(new NBTTagDouble(64.0D));
        mockPos.SetTag(new NBTTagDouble(2.0D));
        dummyPlayerNbt.SetTag("Pos", mockPos);

        using (var stream = File.Create(Path.Combine(playersDir.FullName, "TestPlayer.dat")))
        {
            NbtIo.WriteCompressed(dummyPlayerNbt, stream);
        }

        // Save world without players to trigger fallback
        storage.Save(props, new List<EntityPlayer>());

        // Load level.dat and verify player data
        string levelDat = Path.Combine(baseDir, "world", "level.dat");
        using (var stream = File.OpenRead(levelDat))
        {
            var rootTag = NbtIo.ReadCompressed(stream);
            var dataTag = rootTag.GetCompoundTag("Data");
            var playerTag = dataTag.GetCompoundTag("Player");
            Assert.NotNull(playerTag);
            Assert.Equal("ImHere", playerTag.GetString("TestMarker"));

            var savedPos = playerTag.GetTagList("Pos");
            Assert.NotNull(savedPos);
            Assert.Equal(3, savedPos.TagCount());
            Assert.Equal(1.0D, ((NBTTagDouble)savedPos.TagAt(0)).Value);
            Assert.Equal(67.24D, ((NBTTagDouble)savedPos.TagAt(1)).Value);
            Assert.Equal(2.0D, ((NBTTagDouble)savedPos.TagAt(2)).Value);
        }

        Directory.Delete(baseDir, true);
    }
}

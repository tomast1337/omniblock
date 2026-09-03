using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Entities;

public sealed class MobSpawnerCompatibilityTests
{
    [Fact]
    public void Unknown_target_resource_id_survives_nbt_round_trip()
    {
        NBTTagCompound saved = new();
        saved.SetString("id", "MobSpawner");
        saved.SetString("EntityId", "example:missing_mob");
        saved.SetShort("Delay", 20);
        BlockEntityMobSpawner spawner = new();

        ((IEntity)spawner).Read(saved);
        NBTTagCompound rewritten = new();
        spawner.WriteNbt(rewritten);

        Assert.Equal("example:missing_mob", spawner.GetSpawnedEntityId());
        Assert.Equal("example:missing_mob", rewritten.GetString("EntityId"));
    }
}

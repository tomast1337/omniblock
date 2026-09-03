using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class RuntimeEntityTypeRegistryTests
{
    [Fact]
    public void Frozen_indexes_resolve_all_entity_identity_spaces()
    {
        RuntimeEntityTypeRegistry entities = ContentRuntime.Current.EntityTypes;

        EntityType zombie = entities.Get("omniblock:zombie");
        Assert.Same(zombie, entities.GetByProtocolId(54));
        Assert.Same(entities.Get("omniblock:arrow"), entities.GetBySpawnObjectId(60));
        Assert.Same(entities.Get("omniblock:lightningbolt"), entities.GetByGlobalSpawnId(1));
        Assert.Null(entities.GetBySpawnObjectId(999));
        Assert.Null(entities.GetByGlobalSpawnId(999));
        Assert.Null(entities.GetByRuntimeType(typeof(EntityCreature)));
        Assert.Same(entities.Get("omniblock:player"), entities.GetByRuntimeType(typeof(ServerPlayerEntity)));
    }

    [Fact]
    public void Creation_and_identity_use_the_worlds_runtime()
    {
        FakeWorldContext world = new(ContentRuntime.Current);

        Entity zombie = world.Content.EntityTypes.Create("omniblock:zombie", world);

        Assert.Same(world.Content.EntityTypes.Get("omniblock:zombie"), zombie.Type);
        Assert.Equal(54, world.Content.EntityTypes.GetProtocolId(zombie));
        Assert.Equal(ResourceLocation.Parse("omniblock:zombie"), world.Content.EntityTypes.GetKey(zombie));
    }

    [Fact]
    public void Nbt_reconstruction_is_an_instance_registry_operation()
    {
        FakeWorldContext sourceWorld = new(ContentRuntime.Current);
        Entity source = sourceWorld.Content.EntityTypes.Create("omniblock:cow", sourceWorld);
        source.SetPositionAndAngles(4.5, 65, 8.5, 0, 0);
        NBTTagCompound nbt = new();
        Assert.True(source.SaveSelfNbt(nbt));

        FakeWorldContext targetWorld = new(ContentRuntime.Current);
        Entity loaded = Assert.IsType<EntityCreature>(
            targetWorld.Content.EntityTypes.ReadFromNbt(nbt, targetWorld));

        Assert.Same(targetWorld.Content.EntityTypes.Get("omniblock:cow"), loaded.Type);
        Assert.Equal(source.X, loaded.X);
        Assert.Equal(source.Y, loaded.Y);
        Assert.Equal(source.Z, loaded.Z);
    }

    [Fact]
    public void Independently_built_runtimes_have_independent_entity_registries_and_types()
    {
        ContentRuntimeBuilder firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        ContentRuntimeBuilder secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        firstBuilder.AddEntityDefinition(Definition("isolated", 20));
        secondBuilder.AddEntityDefinition(Definition("isolated", 20));

        ContentRuntime first = firstBuilder.Build();
        ContentRuntime second = secondBuilder.Build();

        Assert.NotSame(first.EntityTypes, second.EntityTypes);
        Assert.NotSame(first.EntityTypes.Get("omniblock:isolated"), second.EntityTypes.Get("omniblock:isolated"));
    }

    [Fact]
    public void Missing_ids_fail_or_try_get_without_constructing_anything()
    {
        RuntimeEntityTypeRegistry entities = ContentRuntime.Current.EntityTypes;
        FakeWorldContext world = new();

        Assert.False(entities.TryGet("example:missing", out _));
        Assert.False(entities.TryGetByProtocolId(127, out _));
        Assert.False(entities.TryCreate("example:missing", world, out _));
        Assert.False(entities.TryCreate(127, world, out _));
        Assert.Throws<KeyNotFoundException>(() => entities.Get("example:missing"));
        Assert.Throws<KeyNotFoundException>(() => entities.GetByProtocolId(127));
    }

    private static EntityDefinition Definition(string name, int protocolId) => new()
    {
        Name = name,
        Namespace = Namespace.OmniBlock,
        ProtocolId = protocolId
    };
}

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Registries;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities;

/// <summary>Entity identities visible while a candidate catalog is being assembled.</summary>
public interface IEntityTypeBuildView
{
    EntityType Get(ResourceLocation key);
    bool TryGet(ResourceLocation key, [NotNullWhen(true)] out EntityType? type);
}

/// <summary>Catalog dependencies available while constructing entity types.</summary>
public readonly record struct EntityBuildContext(
    IBlockRuntimeView Blocks,
    IItemRuntimeView Items,
    IEntityTypeBuildView EntityTypes);

public interface IEntityBehaviorProvider
{
    object Build(ResourceLocation type, JsonElement definition, in EntityBehaviorBuildContext context);
}

public interface IEntityBehaviorProviderRegistry
{
    void Register(ResourceLocation type, IEntityBehaviorProvider provider);
    object Build(ResourceLocation type, JsonElement definition, in EntityBehaviorBuildContext context);
}

public interface IEntityConstructorProvider
{
    Type RuntimeType { get; }
    Entity Create(IWorldContext world, EntityType type);
}

public interface IEntityConstructorProviderRegistry
{
    void Register(ResourceLocation type, IEntityConstructorProvider provider);
    IEntityConstructorProvider Get(ResourceLocation type);
}

public sealed class EntityConstructorProviderRegistry : IEntityConstructorProviderRegistry
{
    private readonly Dictionary<ResourceLocation, IEntityConstructorProvider> _providers = [];

    public EntityConstructorProviderRegistry()
    {
        Register("omniblock:object", new Provider<EntityObject>(static (world, type) => new EntityObject(world, type)));
        Register("omniblock:living", new Provider<EntityLiving>(static (world, type) => new EntityLiving(world, type)));
        Register("omniblock:creature", new Provider<EntityCreature>(static (world, type) => new EntityCreature(world, type)));
    }

    public void Register(ResourceLocation type, IEntityConstructorProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!_providers.TryAdd(type, provider))
            throw new ArgumentException($"Entity constructor provider '{type}' is already registered.");
    }

    public IEntityConstructorProvider Get(ResourceLocation type) =>
        _providers.TryGetValue(type, out IEntityConstructorProvider? provider)
            ? provider
            : throw new KeyNotFoundException($"Unknown entity constructor provider '{type}'.");

    private sealed class Provider<T>(Func<IWorldContext, EntityType, T> factory) : IEntityConstructorProvider
        where T : Entity
    {
        public Type RuntimeType => typeof(T);
        public Entity Create(IWorldContext world, EntityType type) => factory(world, type);
    }
}

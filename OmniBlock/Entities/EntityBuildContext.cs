using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Registries;

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

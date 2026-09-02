using System.Text.Json;

namespace OmniBlock.Items.Behaviors;

/// <summary>Builds runtime item behavior from declarative definitions and injected dependencies.</summary>
public interface IItemBehaviorProviderRegistry
{
    IItemBehavior Build(ResourceLocation type, JsonElement definition, in ItemBuildContext context);
}

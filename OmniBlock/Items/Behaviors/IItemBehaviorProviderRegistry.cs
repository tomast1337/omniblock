namespace OmniBlock.Items.Behaviors;

/// <summary>Builds runtime item behavior from declarative definitions and injected dependencies.</summary>
public interface IItemBehaviorProviderRegistry
{
    IItemBehavior Build(ItemBehaviorDefinition definition, ItemBuildContext context);
}

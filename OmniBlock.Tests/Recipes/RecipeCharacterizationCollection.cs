namespace OmniBlock.Tests.Recipes;

// The legacy implementation stores compiled recipes in process-global dictionaries. Keep these
// characterization tests isolated until RuntimeProcessRegistry replaces those globals.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RecipeCharacterizationCollection
{
    public const string Name = "Recipe characterization";
}

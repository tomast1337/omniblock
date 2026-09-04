namespace OmniBlock.Client.Rendering.Entities.Models;

/// <summary>
///     Maps the <c>"Model"</c> name in an entity definition to the class that builds it, so a
///     definition can name its model without the client hard-coding which entity gets which.
///     <para>
///         Factories, not instances: two renderers naming the same model each need their own, and a
///         model carries per-frame animation state that must not be shared between them.
///     </para>
/// </summary>
internal static class EntityModelRegistry
{
    private static readonly Dictionary<string, Func<ModelBase>> s_factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["biped"] = () => new ModelBiped(),
        ["chicken"] = () => new ModelChicken(),
        ["cow"] = () => new ModelCow(),
        ["creeper"] = () => new ModelCreeper(),
        ["creeper_charged"] = () => new ModelCreeper(2.0F),
        ["ghast"] = () => new ModelGhast(),
        ["pig"] = () => new ModelPig(),
        ["pig_saddle"] = () => new ModelPig(0.5F),
        ["sheep"] = () => new ModelSheep(),
        ["sheep_fur"] = () => new ModelSheepFur(),
        ["skeleton"] = () => new Skeleton(),
        ["slime"] = () => new ModelSlime(),
        ["slime_cube"] = () => new ModelSlimeCube(),
        ["spider"] = () => new ModelSpider(),
        ["squid"] = () => new ModelSquid(),
        ["wolf"] = () => new ModelWolf(),
        ["zombie"] = () => new Zombie()
    };

    public static ModelBase Create(string name) =>
        s_factories.TryGetValue(name, out var factory)
            ? factory()
            : throw new ArgumentException($"Unknown entity model '{name}'.", nameof(name));
}

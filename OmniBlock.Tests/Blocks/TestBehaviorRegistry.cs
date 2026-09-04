using System.Text.Json;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Keeps older focused behavior tests concise while production construction uses an explicitly
///     owned <see cref="ContentRuntimeBuilder" />. Delete this shim as those tests gain custom contexts.
/// </summary>
internal static class BehaviorRegistry
{
    public static object Build(string type, JsonElement definition) =>
        ContentRuntime.Current.BlockBehaviorProviders.Build(ResourceLocation.Parse(type), definition, default);
}

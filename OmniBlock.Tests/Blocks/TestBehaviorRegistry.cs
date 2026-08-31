using System.Text.Json;
using OmniBlock.Registries;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
/// Keeps older focused behavior tests concise while production construction uses an explicitly
/// owned <see cref="ContentRuntimeBuilder" />. Delete this shim as those tests gain custom contexts.
/// </summary>
internal static class BehaviorRegistry
{
    private static readonly ContentRuntimeBuilder s_content = ContentRuntimeBuilder.CreateBuiltIns();

    public static object Build(string type, JsonElement definition) =>
        s_content.BuildBlockBehavior(ResourceLocation.Parse(type), definition);
}

using System.Text.Json;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
/// Resolves namespaced behavior provider keys during catalog construction. Implementations may
/// source providers from built-in C# code or, later, registrations made during Luau's Registry
/// phase; callers do not need to distinguish between them.
/// </summary>
public interface IBlockBehaviorProviderRegistry
{
    object Build(ResourceLocation type, JsonElement definition, in BehaviorBuildContext context);
}

using System.Text.Json;

namespace OmniBlock.Processes;

/// <summary>Compiles and validates the provider-specific JSON for one process type.</summary>
public interface IProcessProvider
{
    ICompiledProcess Build(
        ResourceLocation id,
        JsonElement definition,
        in ProcessBuildContext context);

    /// <summary>Validates conflicts that are meaningful only within this provider's schema.</summary>
    void Validate(IReadOnlyList<ICompiledProcess> processes)
    {
    }
}

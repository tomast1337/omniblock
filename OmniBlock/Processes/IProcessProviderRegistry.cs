using System.Text.Json;

namespace OmniBlock.Processes;

public interface IProcessProviderRegistry
{
    ICompiledProcess Build(
        ResourceLocation providerType,
        ResourceLocation processId,
        JsonElement definition,
        in ProcessBuildContext context);
}

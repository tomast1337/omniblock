using OmniBlock.Processes;
using OmniBlock.Registries;

namespace OmniBlock.Server;

internal sealed class ProcessReloadListener(OmniBlockServer server) : IRegistryReloadListener
{
    public void OnRegistriesRebuilt(RegistryAccess registryAccess)
    {
        IReadableRegistry<ProcessDefinition> definitions =
            registryAccess.GetOrThrow(RegistryKeys.Recipes);
        if (!definitions.Any())
            throw new InvalidOperationException("Cannot publish an empty process catalog.");

        // WithProcesses performs the complete build and validation before this candidate is queued.
        ContentRuntime candidate = server.Content.WithProcesses(definitions);
        server.StageContent(candidate);
    }
}

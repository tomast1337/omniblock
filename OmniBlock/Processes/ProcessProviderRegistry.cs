using System.Collections.Frozen;
using System.Text.Json;

namespace OmniBlock.Processes;

/// <summary>Immutable namespaced dispatch table for native and future mod process providers.</summary>
public sealed class ProcessProviderRegistry : IProcessProviderRegistry
{
    private readonly FrozenDictionary<ResourceLocation, IProcessProvider> _providers;

    public ProcessProviderRegistry(IEnumerable<KeyValuePair<ResourceLocation, IProcessProvider>> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var entries = new Dictionary<ResourceLocation, IProcessProvider>();
        foreach (var (type, provider) in providers)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(provider);
            if (!entries.TryAdd(type, provider))
                throw new ArgumentException($"Duplicate process provider type '{type}'.", nameof(providers));
        }

        _providers = entries.ToFrozenDictionary();
    }

    public int Count => _providers.Count;
    public IEnumerable<ResourceLocation> Types => _providers.Keys;

    public ICompiledProcess Build(
        ResourceLocation providerType,
        ResourceLocation processId,
        JsonElement definition,
        in ProcessBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        ArgumentNullException.ThrowIfNull(processId);
        if (!_providers.TryGetValue(providerType, out var provider))
        {
            throw new ArgumentException(
                $"Process '{processId}' uses unknown process provider '{providerType}'.",
                nameof(providerType));
        }

        try
        {
            var process = provider.Build(processId, definition, context)
                          ?? throw new InvalidOperationException("Provider returned null.");
            if (process.Id != processId)
            {
                throw new InvalidOperationException(
                    $"Provider returned process id '{process.Id}', expected '{processId}'.");
            }

            if (process.ProviderType != providerType)
            {
                throw new InvalidOperationException(
                    $"Provider returned type '{process.ProviderType}', expected '{providerType}'.");
            }

            return process;
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"Process '{processId}' of type '{providerType}' failed construction: {error.Message}",
                error);
        }
    }

    public void Validate(IEnumerable<ICompiledProcess> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        foreach (var group in processes.GroupBy(process => process.ProviderType))
        {
            if (!_providers.TryGetValue(group.Key, out var provider))
                throw new InvalidOperationException($"No provider remains registered for process type '{group.Key}'.");
            var entries = group.ToArray();
            try
            {
                provider.Validate(entries);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"Process type '{group.Key}' failed conflict validation: {error.Message}", error);
            }
        }
    }
}

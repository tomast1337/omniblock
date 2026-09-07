using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OmniBlock.Network.Messages;
using OmniBlock.Processes;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Client.Network;

/// <summary>
///     Accumulates <see cref="RegistryDataMessage" />s received during the login configuration
///     phase and provides typed, holder-based access to the deserialized data.
/// </summary>
internal sealed class ClientRegistryAccess(ContentRuntime content, Action<ContentRuntime> stageContent)
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Dictionary<ResourceLocation, object> _cache = [];

    private readonly ILogger<ClientRegistryAccess> _logger = Log.Instance.For<ClientRegistryAccess>();

    // Raw JSON strings pending deserialization: registryId -> (name -> json).
    private readonly Dictionary<ResourceLocation, Dictionary<ResourceLocation, string?>> _raw = [];
    private ContentRuntime? _pendingContent;

    /// <summary>
    ///     Stores the entries from a <see cref="RegistryDataMessage" />.
    /// </summary>
    public void Accumulate(RegistryDataMessage packet)
    {
        var entries = new Dictionary<ResourceLocation, string?>(packet.Entries.Count);
        foreach (var entry in packet.Entries)
        {
            entries[entry.Key] = entry.JsonData;
        }

        _raw[packet.RegistryId] = entries;

        _logger.LogDebug($"Received {packet.Entries.Count} entries for {packet.RegistryId}");

        if (packet.RegistryId.IsVanilla && packet.RegistryId.Path == "recipe")
        {
            var definitions =
                GetAll<ProcessDefinition>(packet.RegistryId).Values.Select(holder => holder.Value);
            var candidate = content.WithProcesses(definitions);
            content = candidate;
            _pendingContent = candidate;
        }
    }

    public void CompleteConfiguration(string? requiredFingerprint = null)
    {
        var candidate = Interlocked.Exchange(ref _pendingContent, null);
        var effective = candidate ?? content;
        if (!string.IsNullOrEmpty(requiredFingerprint)
            && !string.Equals(requiredFingerprint, effective.Manifest.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Server content catalog {requiredFingerprint} does not match compiled client catalog {effective.Manifest.Fingerprint}.");
        }

        if (candidate is not null) stageContent(candidate);
    }

    /// <summary>
    ///     Returns a holder for a single entry by name, or <c>null</c> if not found.
    ///     The holder is stable across resyncs.
    /// </summary>
    public Holder<T>? Get<T>(RegistryKey<T> key, string name) where T : class, IDataAsset, new()
        => GetAll(key).GetValueOrDefault(name);

    /// <summary>
    ///     Returns a holder for a single entry by resource location, or <c>null</c> if not found.
    ///     The holder is stable across resyncs.
    /// </summary>
    public Holder<T>? Get<T>(RegistryKey<T> key, ResourceLocation item) where T : class, IDataAsset, new()
        => GetAll(key).GetValueOrDefault(item);

    /// <summary>
    ///     Returns all entries for a registry as a name -> holder dictionary.
    /// </summary>
    public IReadOnlyDictionary<ResourceLocation, Holder<T>> GetAll<T>(RegistryKey<T> key) where T : class, IDataAsset, new() => GetAll<T>(key.Location);

    /// <summary>
    ///     Returns all entries for a registry as a name -> holder dictionary.
    /// </summary>
    public IReadOnlyDictionary<ResourceLocation, Holder<T>> GetAll<T>(ResourceLocation key) where T : class, IDataAsset, new()
    {
        if (!_raw.TryGetValue(key, out var raw))
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                return (Dictionary<ResourceLocation, Holder<T>>)existing;
            }

            return new Dictionary<ResourceLocation, Holder<T>>();
        }

        _raw.Remove(key);

        if (_cache.TryGetValue(key, out var cached))
        {
            var existing = (Dictionary<ResourceLocation, Holder<T>>)cached;
            MergeIntoHolders(existing, raw); //
            return existing;
        }

        var dict = DeserializeToHolders<T>(raw);
        _cache[key] = dict;
        return dict;
    }

    private static Dictionary<ResourceLocation, Holder<T>> DeserializeToHolders<T>(
        Dictionary<ResourceLocation, string?> raw)
        where T : class, IDataAsset, new()
    {
        var result = new Dictionary<ResourceLocation, Holder<T>>(raw.Count);
        foreach (var (key, json) in raw)
        {
            if (json is null) continue;
            var value = JsonSerializer.Deserialize<T>(json, s_options);
            if (value is null) continue;
            value.Name = key.Path;
            value.Namespace = key.Namespace;
            result[key] = new Holder<T>(value);
        }

        return result;
    }

    private static void MergeIntoHolders<T>(
        Dictionary<ResourceLocation, Holder<T>> holders,
        Dictionary<ResourceLocation, string?> raw)
        where T : class, IDataAsset, new()
    {
        foreach (var (key, json) in raw)
        {
            if (json is null) continue;
            var value = JsonSerializer.Deserialize<T>(json, s_options);
            if (value is null) continue;
            value.Name = key.Path;
            value.Namespace = key.Namespace;

            if (holders.TryGetValue(key, out var holder))
            {
                holder.Update(value);
            }
            else
            {
                holders[key] = new Holder<T>(value);
            }
        }

        foreach (var k in holders.Keys.Except(raw.Keys).ToList())
        {
            var stale = holders[k];
            holders.Remove(k);
            stale.Invalidate();
        }
    }
}

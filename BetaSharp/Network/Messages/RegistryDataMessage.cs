using System.Text.Json;
using System.Text.Json.Serialization;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Network.Messages;

/// <summary>
///     Carries the contents of one data-driven registry from server to client during
///     login or reload. The client accumulates these and, on receiving
///     <see cref="FinishConfigurationMessage" />, validates and swaps in the new set.
/// </summary>
public sealed class RegistryDataMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.BetaSharp, "registry_data");

    public override ResourceLocation Key => Id;

    /// <summary>One registry worth of entries before it strains the envelope.</summary>
    public const int MaxEntries = 8192;

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    ///     Stands in until <see cref="FromRegistry" /> or <see cref="Read" /> supplies the real one.
    ///     <para>
    ///         A placeholder rather than <c>null!</c> because a default-constructed message has to be
    ///         writable: that is what lets the generated-message tests round-trip every registered
    ///         type without knowing how to populate any of them, and a null here fails inside
    ///         <c>Write</c> with a <c>NullReferenceException</c> rather than at the point the field
    ///         was left unset.
    ///     </para>
    /// </summary>
    private static readonly ResourceLocation s_unset = new(Namespace.BetaSharp, "unset");

    public ResourceLocation RegistryId { get; set; } = s_unset;

    /// <summary>The entries, each a key and an optional JSON payload. Null JSON means the entry was
    /// deleted — the key is present so the client knows which one to remove.</summary>
    public List<Entry> Entries { get; } = [];

    /// <summary>
    ///     Builds a sync message for every entry in a registry.
    /// </summary>
    public static RegistryDataMessage FromRegistry<T>(RegistryKey<T> key, IReadableRegistry<T> registry)
        where T : class, IDataAsset
    {
        List<Entry> entries = new();
        foreach (ResourceLocation entryKey in registry.Keys)
        {
            T? value = registry.GetValue(entryKey);
            if (value is null)
            {
                continue;
            }

            entries.Add(new Entry(entryKey, JsonSerializer.Serialize(value, s_writeOptions)));
        }

        RegistryDataMessage message = new()
        {
            RegistryId = key.Location
        };
        message.Entries.AddRange(entries);
        return message;
    }

    public override void Read(Stream stream)
    {
        RegistryId = stream.ReadResourceLocation();

        int count = stream.ReadVarInt();
        if (count < 0 || count > MaxEntries)
        {
            throw new InvalidDataException(
                $"Registry data declares {count} entries; the limit is {MaxEntries}.");
        }

        Entries.Clear();
        Entries.Capacity = count;
        for (int i = 0; i < count; i++)
        {
            ResourceLocation key = stream.ReadResourceLocation();
            string? json = stream.ReadBoolean() ? stream.ReadString() : null;
            Entries.Add(new Entry(key, json));
        }
    }

    public override void Write(Stream stream)
    {
        stream.WriteResourceLocation(RegistryId);
        stream.WriteVarInt(Entries.Count);
        foreach (Entry entry in Entries)
        {
            stream.WriteResourceLocation(entry.Key);
            stream.WriteBoolean(entry.JsonData is not null);
            if (entry.JsonData is not null)
            {
                stream.WriteString(entry.JsonData);
            }
        }
    }

    public override int Size()
    {
        int size = StreamExtensions.ResourceLocationSize(RegistryId)
                   + StreamExtensions.VarIntSize(Entries.Count);
        foreach (Entry entry in Entries)
        {
            size += StreamExtensions.ResourceLocationSize(entry.Key)
                    + 1
                    + (entry.JsonData is not null
                        ? 2 + global::BetaSharp.Util.ModifiedUtf8.GetByteCount(entry.JsonData)
                        : 0);
        }

        return size;
    }

    public readonly record struct Entry(ResourceLocation Key, string? JsonData);
}

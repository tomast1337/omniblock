using System.Text.Json.Serialization;

namespace BetaSharp.Entities.State;

/// <summary>Value kinds a declared synced property may take, mapping onto <see cref="Util.SyncedDataType" />.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SyncedValueKind>))]
public enum SyncedValueKind
{
    Bool,
    Byte,
    Short,
    Int,
    Float,
    String
}

/// <summary>
///     One entry in an entity's <c>SyncedProperties</c> JSON array.
///     <para>
///         <see cref="Id" /> is a <b>protocol fact</b>, exactly like <c>ProtocolId</c>: it is written
///         into the datawatcher header as <c>(type &lt;&lt; 5) | (id &amp; 31)</c>, and the client reads these
///         same asset files. Changing one server-side without the client desynchronises that entity's
///         state. Ids are unique per entity and must fit 0..31; 0 is reserved for
///         <see cref="Entity" />'s shared flags byte.
///     </para>
/// </summary>
/// <param name="Name">How behaviors refer to it; resolved to <see cref="Id" /> once at load.</param>
/// <param name="Default">Initial value. Numbers are read as doubles and narrowed to <see cref="Kind" />.</param>
/// <param name="Nbt">
///     NBT key to save this property under. When set, the value round-trips automatically and the
///     entity needs no persistence behavior — which covers every synced property that maps
///     one-to-one onto a save key.
/// </param>
public sealed record SyncedPropertyDefinition(
    string Name,
    int Id,
    SyncedValueKind Kind,
    double Default = 0.0D,
    string? DefaultString = null,
    string? Nbt = null);

using OmniBlock.Worlds.Lod;
using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Declares the authoritative distant-terrain cache identity for one dimension. World and
///     generator fingerprints are opaque to the client; content, reduction schema, and material
///     rules are independently checked before transport is enabled.
/// </summary>
public sealed class TerrainLodIdentityMessage : Message
{
    private const int MaximumFingerprintLength = 128;
    public static readonly ResourceLocation Id = new(
        Namespace.Get("omniblock"), "terrain_lod_identity_v2");

    public string WorldFingerprint { get; set; } = "";
    public int Dimension { get; set; }
    public string ContentFingerprint { get; set; } = "";
    public string GeneratorFingerprint { get; set; } = "";
    public int ReductionSchemaVersion { get; set; }
    public string MaterialRulesFingerprint { get; set; } = "";
    public int MaximumSpatialLevel { get; set; }
    public int QualityPolicyVersion { get; set; }
    public override ResourceLocation Key => Id;
    public override int SchemaVersion => 2;

    public TerrainLodCacheIdentity ToIdentity() => new(
        WorldFingerprint,
        Dimension,
        ContentFingerprint,
        GeneratorFingerprint,
        ReductionSchemaVersion,
        MaterialRulesFingerprint,
        MaximumSpatialLevel,
        QualityPolicyVersion);

    public static TerrainLodIdentityMessage Of(TerrainLodCacheIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new TerrainLodIdentityMessage
        {
            WorldFingerprint = identity.WorldFingerprint,
            Dimension = identity.Dimension,
            ContentFingerprint = identity.ContentFingerprint,
            GeneratorFingerprint = identity.GeneratorFingerprint,
            ReductionSchemaVersion = identity.ReductionSchemaVersion,
            MaterialRulesFingerprint = identity.MaterialRulesFingerprint,
            MaximumSpatialLevel = identity.MaximumSpatialLevel,
            QualityPolicyVersion = identity.QualityPolicyVersion
        };
    }

    public override void Read(Stream stream)
    {
        WorldFingerprint = stream.ReadString(MaximumFingerprintLength);
        Dimension = stream.ReadInt();
        ContentFingerprint = stream.ReadString(MaximumFingerprintLength);
        GeneratorFingerprint = stream.ReadString(MaximumFingerprintLength);
        ReductionSchemaVersion = stream.ReadVarInt();
        MaterialRulesFingerprint = stream.ReadString(MaximumFingerprintLength);
        MaximumSpatialLevel = stream.ReadVarInt();
        QualityPolicyVersion = stream.ReadVarInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteString(WorldFingerprint);
        stream.WriteInt(Dimension);
        stream.WriteString(ContentFingerprint);
        stream.WriteString(GeneratorFingerprint);
        stream.WriteVarInt(ReductionSchemaVersion);
        stream.WriteString(MaterialRulesFingerprint);
        stream.WriteVarInt(MaximumSpatialLevel);
        stream.WriteVarInt(QualityPolicyVersion);
    }

    public override int Size() =>
        sizeof(ushort) + ModifiedUtf8.GetByteCount(WorldFingerprint) + sizeof(int) +
        sizeof(ushort) + ModifiedUtf8.GetByteCount(ContentFingerprint) +
        sizeof(ushort) + ModifiedUtf8.GetByteCount(GeneratorFingerprint) +
        StreamExtensions.VarIntSize(ReductionSchemaVersion) +
        sizeof(ushort) + ModifiedUtf8.GetByteCount(MaterialRulesFingerprint) +
        StreamExtensions.VarIntSize(MaximumSpatialLevel) +
        StreamExtensions.VarIntSize(QualityPolicyVersion);
}

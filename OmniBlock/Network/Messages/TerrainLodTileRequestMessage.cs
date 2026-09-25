using OmniBlock.Worlds.Lod;
using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Requests already-generated coarse terrain records. The server validates every key against
///     the player's dimension and position; this message is never authority to generate or reveal
///     arbitrary terrain.
/// </summary>
public sealed class TerrainLodTileRequestMessage : Message
{
    public const int MaximumKeys = TerrainLodScaleBudget.MaximumRequestKeys;
    private const int MaximumIdentityLength = 128;
    public static readonly ResourceLocation Id = new(
        Namespace.Get("omniblock"), "terrain_lod_tile_request_v4");

    public int Dimension { get; set; }
    public string CacheIdentity { get; set; } = "";
    public int MaximumSpatialLevel { get; set; } =
        TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel;
    public int QualityPolicyVersion { get; set; } =
        TerrainLodSpatialPolicy.CurrentQualityPolicyVersion;
    public TerrainLodTileKey[] Keys { get; set; } = [];
    public override ResourceLocation Key => Id;
    public override int SchemaVersion => 4;
    public override SendPriority Priority => SendPriority.Bulk;

    public override void Read(Stream stream)
    {
        Dimension = stream.ReadInt();
        CacheIdentity = stream.ReadString(MaximumIdentityLength);
        MaximumSpatialLevel = stream.ReadVarInt();
        QualityPolicyVersion = stream.ReadVarInt();
        if (MaximumSpatialLevel is < TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel or
            > TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel)
            throw new InvalidDataException(
                $"Terrain LOD request advertises unsupported maximum spatial level " +
                $"{MaximumSpatialLevel}.");
        if (QualityPolicyVersion <= 0)
            throw new InvalidDataException(
                $"Terrain LOD request advertises invalid quality-policy version " +
                $"{QualityPolicyVersion}.");
        var count = stream.ReadVarInt();
        if (count is < 0 or > MaximumKeys)
            throw new InvalidDataException($"Terrain LOD request contains {count} keys.");
        Keys = new TerrainLodTileKey[count];
        for (var index = 0; index < Keys.Length; index++)
        {
            var key = new TerrainLodTileKey(
                stream.ReadVarInt(), stream.ReadInt(), stream.ReadInt());
            if (key.Level < TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel ||
                key.Level > MaximumSpatialLevel)
                throw new InvalidDataException(
                    $"Terrain LOD request contains spatial level {key.Level} outside its " +
                    $"declared range {TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel}-" +
                    $"{MaximumSpatialLevel}.");
            Keys[index] = key;
        }
    }

    public override void Write(Stream stream)
    {
        if (Keys.Length > MaximumKeys)
            throw new InvalidOperationException(
                $"Terrain LOD request cannot contain more than {MaximumKeys} keys.");
        if (MaximumSpatialLevel is < TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel or
            > TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel)
            throw new InvalidOperationException(
                $"Terrain LOD request maximum spatial level {MaximumSpatialLevel} exceeds this " +
                $"peer's supported range.");
        if (QualityPolicyVersion != TerrainLodSpatialPolicy.CurrentQualityPolicyVersion)
            throw new InvalidOperationException(
                $"Terrain LOD request quality-policy version {QualityPolicyVersion} is unsupported.");
        if (Keys.Any(key => key.Level < TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel ||
                            key.Level > MaximumSpatialLevel))
            throw new InvalidOperationException(
                "Terrain LOD request contains a spatial level outside its negotiated range.");
        stream.WriteInt(Dimension);
        stream.WriteString(CacheIdentity);
        stream.WriteVarInt(MaximumSpatialLevel);
        stream.WriteVarInt(QualityPolicyVersion);
        stream.WriteVarInt(Keys.Length);
        foreach (var key in Keys)
        {
            stream.WriteVarInt(key.Level);
            stream.WriteInt(key.X);
            stream.WriteInt(key.Z);
        }
    }

    public override int Size() => sizeof(int) + sizeof(ushort) +
                                  ModifiedUtf8.GetByteCount(CacheIdentity) +
                                  StreamExtensions.VarIntSize(MaximumSpatialLevel) +
                                  StreamExtensions.VarIntSize(QualityPolicyVersion) +
                                  StreamExtensions.VarIntSize(Keys.Length) +
                                  Keys.Sum(static key =>
                                      StreamExtensions.VarIntSize(key.Level) + sizeof(int) * 2);

}

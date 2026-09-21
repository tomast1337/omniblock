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
        Namespace.Get("omniblock"), "terrain_lod_tile_request_v2");

    public int Dimension { get; set; }
    public string CacheIdentity { get; set; } = "";
    public TerrainLodTileKey[] Keys { get; set; } = [];
    public override ResourceLocation Key => Id;
    public override int SchemaVersion => 2;

    public override void Read(Stream stream)
    {
        Dimension = stream.ReadInt();
        CacheIdentity = stream.ReadString(MaximumIdentityLength);
        var count = stream.ReadVarInt();
        if (count is < 0 or > MaximumKeys)
            throw new InvalidDataException($"Terrain LOD request contains {count} keys.");
        Keys = new TerrainLodTileKey[count];
        for (var index = 0; index < Keys.Length; index++)
        {
            var key = new TerrainLodTileKey(
                stream.ReadVarInt(), stream.ReadInt(), stream.ReadInt());
            if (!IsSupportedRemoteKey(key))
                throw new InvalidDataException(
                    $"Terrain LOD request contains unsupported spatial level {key.Level}.");
            Keys[index] = key;
        }
    }

    public override void Write(Stream stream)
    {
        if (Keys.Length > MaximumKeys)
            throw new InvalidOperationException(
                $"Terrain LOD request cannot contain more than {MaximumKeys} keys.");
        if (Keys.Any(static key => !IsSupportedRemoteKey(key)))
            throw new InvalidOperationException(
                "Terrain LOD request contains a spatial level outside the supported range.");
        stream.WriteInt(Dimension);
        stream.WriteString(CacheIdentity);
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
                                  StreamExtensions.VarIntSize(Keys.Length) +
                                  Keys.Sum(static key =>
                                      StreamExtensions.VarIntSize(key.Level) + sizeof(int) * 2);

    private static bool IsSupportedRemoteKey(TerrainLodTileKey key) =>
        key.Level >= TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel &&
        key.Level <= TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel;
}

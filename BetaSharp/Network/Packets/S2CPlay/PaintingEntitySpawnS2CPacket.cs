using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Network.Packets.S2CPlay;

public class PaintingEntitySpawnS2CPacket() : Packet(PacketId.PaintingEntitySpawnS2C)
{
    public int Direction { get; private set; }
    public int EntityId { get; private set; }
    public string Title { get; private set; } = "";
    public int XPosition { get; private set; }
    public int YPosition { get; private set; }
    public int ZPosition { get; private set; }

    public static PaintingEntitySpawnS2CPacket Get(Entity paint)
    {
        HangingArtBehavior hanging = paint.Behaviors.Find<HangingArtBehavior>()!;
        PaintingEntitySpawnS2CPacket p = Get<PaintingEntitySpawnS2CPacket>(PacketId.PaintingEntitySpawnS2C);
        p.EntityId = paint.ID;
        p.XPosition = hanging.TileX(paint);
        p.YPosition = hanging.TileY(paint);
        p.ZPosition = hanging.TileZ(paint);
        p.Direction = hanging.Direction(paint);
        p.Title = hanging.Art(paint)!.Title;
        return p;
    }

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Title = stream.ReadLongString((ushort)Painting.MaxArtTitleLength);
        XPosition = stream.ReadInt();
        YPosition = stream.ReadInt();
        ZPosition = stream.ReadInt();
        Direction = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteLongString(Title);
        stream.WriteInt(XPosition);
        stream.WriteInt(YPosition);
        stream.WriteInt(ZPosition);
        stream.WriteInt(Direction);
    }

    public override void Apply(NetHandler handler) => handler.onPaintingEntitySpawn(this);

    public override int Size() => 24;
}

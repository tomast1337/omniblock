using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Worlds.Storage;

namespace OmniBlock.Worlds.Maps;

public class MapState(string id) : PersistentState(id)
{
    private readonly Dictionary<EntityPlayer, MapUpdateTracker> _updateTrackers = new();
    public List<MapIcon> Icons { get; } = [];
    public int CenterX { get; set; }
    public int CenterZ { get; set; }
    public byte[] Colors { get; set; } = new byte[128 * 128];
    public sbyte Dimension { get; set; }
    public int InventoryTicks { get; set; }
    public sbyte Scale { get; set; }

    public override void ReadNBT(NBTTagCompound nbt)
    {
        Dimension = nbt.GetByte("dimension");
        CenterX = nbt.GetInteger("xCenter");
        CenterZ = nbt.GetInteger("zCenter");
        Scale = nbt.GetByte("scale");

        if (Scale < 0)
        {
            Scale = 0;
        }

        if (Scale > 4)
        {
            Scale = 4;
        }

        var nbtWidth = nbt.GetShort("width");
        var nbtHeight = nbt.GetShort("height");

        if (nbtWidth == 128 && nbtHeight == 128)
        {
            Colors = nbt.GetByteArray("colors");
        }
        else
        {
            var rawColors = nbt.GetByteArray("colors");
            Colors = new byte[128 * 128];
            var offsetX = (128 - nbtWidth) / 2;
            var offsetZ = (128 - nbtHeight) / 2;

            for (var y = 0; y < nbtHeight; ++y)
            {
                var targetZ = y + offsetZ;
                if (targetZ >= 0 && targetZ < 128)
                {
                    for (var x = 0; x < nbtWidth; ++x)
                    {
                        var targetX = x + offsetX;
                        if (targetX >= 0 && targetX < 128)
                        {
                            Colors[targetX + targetZ * 128] = rawColors[x + y * nbtWidth];
                        }
                    }
                }
            }
        }
    }

    public override void WriteNBT(NBTTagCompound nbt)
    {
        nbt.SetByte("dimension", Dimension);
        nbt.SetInteger("xCenter", CenterX);
        nbt.SetInteger("zCenter", CenterZ);
        nbt.SetByte("scale", Scale);
        nbt.SetShort("width", 128);
        nbt.SetShort("height", 128);
        nbt.SetByteArray("colors", Colors);
    }

    public void Update(EntityPlayer viewer, ItemStack mapItem)
    {
        if (!_updateTrackers.ContainsKey(viewer))
        {
            _updateTrackers[viewer] = new MapUpdateTracker(this, viewer);
        }

        Icons.Clear();

        foreach (var mapInfo in _updateTrackers.Values.ToList())
        {
            if (!mapInfo.Player.Dead && mapInfo.Player.Inventory.Contains(mapItem))
            {
                var relX = (float)(mapInfo.Player.X - CenterX) / (1 << Scale);
                var relZ = (float)(mapInfo.Player.Z - CenterZ) / (1 << Scale);
                byte limitX = 64;
                byte limitZ = 64;

                if (relX >= -limitX && relZ >= -limitZ && relX <= limitX && relZ <= limitZ)
                {
                    byte iconType = 0;
                    var iconX = (byte)(int)(relX * 2.0F + 0.5D);
                    var iconZ = (byte)(int)(relZ * 2.0F + 0.5D);
                    var iconRot = (byte)(int)(viewer.Yaw * 16.0F / 360.0F + 0.5D);

                    if (Dimension < 0)
                    {
                        var randomTick = InventoryTicks / 10;
                        iconRot = (byte)(((randomTick * randomTick * 34187121 + randomTick * 121) >> 15) & 15);
                    }

                    if (mapInfo.Player.DimensionId == Dimension)
                    {
                        Icons.Add(new MapIcon(iconType, iconX, iconZ, iconRot));
                    }
                }
            }
            else
            {
                _updateTrackers.Remove(mapInfo.Player);
            }
        }
    }

    public byte[]? GetPlayerMarkerPacket(EntityPlayer player) => _updateTrackers.GetValueOrDefault(player)?.getUpdateData();

    public void MarkDirty(int xColumn, int minZ, int maxZ)
    {
        MarkDirty();

        foreach (var mapInfo in _updateTrackers.Values)
        {
            if (mapInfo.StartZ[xColumn] < 0 || mapInfo.StartZ[xColumn] > minZ)
            {
                mapInfo.StartZ[xColumn] = minZ;
            }

            if (mapInfo.EndZ[xColumn] < 0 || mapInfo.EndZ[xColumn] < maxZ)
            {
                mapInfo.EndZ[xColumn] = maxZ;
            }
        }
    }

    public void UpdateData(byte[] packet)
    {
        if (packet[0] == 0)
        {
            var columnIndex = packet[1] & 255;
            var startZ = packet[2] & 255;

            for (var i = 0; i < packet.Length - 3; ++i)
            {
                Colors[(i + startZ) * 128 + columnIndex] = packet[i + 3];
            }

            MarkDirty();
        }
        else if (packet[0] == 1)
        {
            Icons.Clear();

            for (var i = 0; i < (packet.Length - 1) / 3; ++i)
            {
                var type = (byte)(packet[i * 3 + 1] % 16);
                var x = packet[i * 3 + 2];
                var z = packet[i * 3 + 3];
                var rot = (byte)(packet[i * 3 + 1] / 16);
                Icons.Add(new MapIcon(type, x, z, rot));
            }
        }
    }
}

using OmniBlock.Entities;

namespace OmniBlock.Worlds.Maps;

internal class MapUpdateTracker
{
    private int _colorsUpdateInterval;
    private byte[]? _iconsData;

    private int _nextDirtyPixel;

    public MapUpdateTracker(MapState state, EntityPlayer player)
    {
        State = state;
        StartZ = new int[128];
        EndZ = new int[128];
        _nextDirtyPixel = 0;
        _colorsUpdateInterval = 0;
        Player = player;

        Array.Fill(EndZ, 127);
    }

    public MapState State { get; }
    public EntityPlayer Player { get; }

    public int[] StartZ { get; }
    public int[] EndZ { get; }

    public byte[]? getUpdateData()
    {
        if (--_colorsUpdateInterval < 0)
        {
            _colorsUpdateInterval = 4;
            var data = new byte[State.Icons.Count * 3 + 1];
            data[0] = 1;

            for (var iconIndex = 0; iconIndex < State.Icons.Count; iconIndex++)
            {
                var icon = State.Icons[iconIndex];
                data[iconIndex * 3 + 1] = (byte)(icon.Type + (icon.Rotation & 15) * 16);
                data[iconIndex * 3 + 2] = icon.X;
                data[iconIndex * 3 + 3] = icon.Z;
            }

            var isUnchanged = true;
            if (_iconsData != null && _iconsData.Length == data.Length)
            {
                for (var i = 0; i < data.Length; i++)
                {
                    if (data[i] != _iconsData[i])
                    {
                        isUnchanged = false;
                        break;
                    }
                }
            }
            else
            {
                isUnchanged = false;
            }

            if (!isUnchanged)
            {
                _iconsData = data;
                return data;
            }
        }

        for (var i = 0; i < 10; i++)
        {
            var dirtyPixel = _nextDirtyPixel * 11 % 128;
            _nextDirtyPixel++;
            if (StartZ[dirtyPixel] >= 0)
            {
                var stripLength = EndZ[dirtyPixel] - StartZ[dirtyPixel] + 1;
                var startZCoord = StartZ[dirtyPixel];
                var packetData = new byte[stripLength + 3];
                packetData[0] = 0;
                packetData[1] = (byte)dirtyPixel;
                packetData[2] = (byte)startZCoord;

                for (var pixelOffset = 0; pixelOffset < packetData.Length - 3; pixelOffset++)
                {
                    packetData[pixelOffset + 3] = State.Colors[(pixelOffset + startZCoord) * 128 + dirtyPixel];
                }

                EndZ[dirtyPixel] = -1;
                StartZ[dirtyPixel] = -1;
                return packetData;
            }
        }

        return null;
    }
}

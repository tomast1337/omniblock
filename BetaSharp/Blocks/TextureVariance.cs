namespace BetaSharp.Blocks;

[Flags]
public enum TextureVariance : byte
{
    None      = 0,
    Rotate90  = 1 << 0,
    Rotate180 = 1 << 1,
    Rotate270 = 1 << 2,
    FlipU     = 1 << 3,
    FlipV     = 1 << 4,

    FlipBoth  = FlipU | FlipV,
    Rotations = Rotate90 | Rotate180 | Rotate270,
    All       = Rotations | FlipBoth
}

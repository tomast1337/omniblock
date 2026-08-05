namespace BetaSharp.Blocks;

/// <summary>
///     Sprite indices into <c>/terrain.png</c> for block rendering (Beta 1.7.3–style atlas).
///     Each index selects a 16×16 tile in a 16-column-wide grid: row <c>index / 16</c>, column <c>index % 16</c>.
///     TODO: This should probably be moved to the BetaSharp.Client.Rendering.Core.TexturesAtlas class were we can have
///     methods to name and retrieve ids
/// </summary>
public static class BlockTextures
{
    public const int GrassTop = 0;
    public const int Dirt = 2;
    public const int GrassSide = 3;
    public const int OakPlanks = 4;
    public const int StoneSlabSide = 5;
    public const int StoneSlabTop = 6;
    public const int TntSide = 8;
    public const int TntTop = 9;
    public const int TntBottom = 10;
    public const int Cobblestone = 16;
    public const int LogOakSide = 20;
    public const int LogTop = 21;
    public const int LogPineSide = 116;
    public const int LogBirchSide = 117;
    public const int Bookshelf = 35;
    public const int FurnaceFrontUnlit = 44;
    public const int LeavesOak = 52;
    public const int FurnaceFrontLit = 61;
    public const int FurnaceTop = 62;
    public const int SaplingOak = 15;
    public const int SaplingPine = 63;
    public const int SaplingBirch = 79;
    public const int GrassSideSnowy = 68;
    public const int CactusTop = 69;
    public const int CactusSide = 70;
    public const int CactusBottom = 71;
    public const int FarmlandWet = 86;
    public const int FarmlandDry = 87;
    public const int RedstoneTorchLit = 99;
    public const int RedstoneTorchUnlit = 115;
    public const int Cake = 121;
    public const int BedTopFoot = 134;
    public const int BedTopHead = 135;
    public const int BedEndFoot = 149;
    public const int BedSideFoot = 150;
    public const int BedSideHead = 151;
    public const int BedEndHead = 152;
    public const int PoweredRailOff = 163;
    public const int RailCorner = 112;
    public const int RepeaterTopUnlit = 131;
    public const int RepeaterTopLit = 147;
    public const int SandstoneSide = 192;
    public const int SandstoneBottom = 208;
    public const int SandstoneTop = 176;
    public const int WoolColoredPaletteBase = 113;
}

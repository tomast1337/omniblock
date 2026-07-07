namespace BetaSharp.Blocks;

/// <summary>Composable capability for neighbor updates and placement/growth rules.</summary>
public interface IBlockPhysics
{
    void NeighborUpdate(Block block, OnTickEvent @event) { }

    /// <summary>
    /// Additional placement restriction. Combined with (never replaces) the base
    /// replaceability check — returning true keeps the base verdict.
    /// </summary>
    bool CanPlaceAt(Block block, CanPlaceAtContext @event) => true;

    bool CanGrow(Block block, OnTickEvent @event) => true;
}

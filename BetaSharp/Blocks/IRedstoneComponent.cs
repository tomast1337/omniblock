using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks;

/// <summary>
///     Composable capability for redstone power emission.
///     Side values use the engine's face indices (0 = down ... 5 = east), matching
///     <see cref="Block.IsPoweringSide" />.
/// </summary>
public interface IRedstoneComponent
{
    bool CanEmitRedstonePower(Block block) => false;
    bool IsPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side) => false;
    bool IsStrongPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side) => false;
}

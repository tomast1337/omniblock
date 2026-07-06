namespace BetaSharp.Blocks;

public interface IBlockInteractable
{
    /// <summary>
    /// Called when a player right-clicks the block.
    /// Return true if the interaction was handled (e.g., opened a GUI, pushed a button), false otherwise.
    /// </summary>
    bool OnUse(Block block, OnUseEvent @event) => false;
}

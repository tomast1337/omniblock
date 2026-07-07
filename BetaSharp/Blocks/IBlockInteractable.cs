namespace BetaSharp.Blocks;

public interface IBlockInteractable
{
    /// <summary>
    /// Called when a player right-clicks the block.
    /// Return true if the interaction was handled (e.g., opened a GUI, pushed a button), false otherwise.
    /// </summary>
    bool OnUse(Block block, OnUseEvent @event) => false;

    /// <summary>Called when a player starts breaking (left-clicks) the block.</summary>
    void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event) { }

    /// <summary>Called when an entity intersects the block's space.</summary>
    void OnEntityCollision(Block block, OnEntityCollisionEvent @event) { }

    /// <summary>Called when an entity walks on top of the block.</summary>
    void OnSteppedOn(Block block, OnEntityStepEvent @event) { }
}

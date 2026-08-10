namespace OmniBlock.Blocks.Behaviors;

public sealed class WorkbenchInteractBehavior : IBlockInteractable
{
    public bool OnUse(Block block, OnUseEvent ctx)
    {
        if (ctx.World.IsRemote) return true;
        ctx.Player.openCraftingScreen(ctx.X, ctx.Y, ctx.Z);
        return true;
    }
}

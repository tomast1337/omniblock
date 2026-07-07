namespace BetaSharp.Blocks.Behaviors;

/// <summary>Cobweb: slows any entity that intersects it. Opacity, collision, and drop are declarative fluent setters.</summary>
internal sealed class WebBehavior : IBlockInteractable
{
    public void OnEntityCollision(Block block, OnEntityCollisionEvent @event) => @event.Entity.Slowed = true;
}

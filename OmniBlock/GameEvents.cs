namespace OmniBlock;

public readonly record struct BlockBreakEvent(int X, int Y, int Z, int BlockId);

public readonly record struct BlockPlacedEvent(int X, int Y, int Z, int BlockId, int Meta);

public readonly record struct EntityHurtEvent(int EntityId, int? AttackerId, int Amount);

/// <summary>
///     Dynamic, multi-subscriber dispatch for gameplay events, additive alongside the single-dispatch
///     <c>IBlockLifecycle</c>/<c>IEntityLifecycle</c> behavior slots. Payloads are primitive-only —
///     no <c>IWorldContext</c>/<c>Entity</c> references — per CLAUDE.md's IDs-over-objects rule for
///     the eventual Luau FFI boundary.
/// </summary>
public static class GameEvents
{
    public static event Action<BlockBreakEvent>? BlockBreak;
    public static event Action<BlockPlacedEvent>? BlockPlaced;
    public static event Action<EntityHurtEvent>? EntityHurt;

    internal static void PublishBlockBreak(BlockBreakEvent e) => BlockBreak?.Invoke(e);
    internal static void PublishBlockPlaced(BlockPlacedEvent e) => BlockPlaced?.Invoke(e);
    internal static void PublishEntityHurt(EntityHurtEvent e) => EntityHurt?.Invoke(e);
}

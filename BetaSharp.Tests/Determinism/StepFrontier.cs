namespace BetaSharp.Tests.Determinism;

/// <summary>
///     The machine-readable definition of "in the <c>Step()</c> path", and of what may not appear
///     there. Kept as data, separate from the analysis in <see cref="CallGraph" />, so that moving
///     the frontier is a one-line edit rather than a change to the engine.
///     <para>See <c>docs/deterministic-movement-extraction.md</c> §1.</para>
/// </summary>
internal static class StepFrontier
{
    /// <summary>
    ///     The physics core: displacement, collision resolution and the state it derives. This is
    ///     what becomes the body of <c>Step()</c>, and it is held to the purity rule <em>now</em>.
    ///     <para>
    ///         Overloads collapse: a name listed here seeds every overload of it, which is the safe
    ///         direction for a purity test.
    ///     </para>
    /// </summary>
    public static readonly string[] CoreRoots =
    [
        "BetaSharp.Entities.EntityLiving.Travel",
        "BetaSharp.Entities.EntityPlayer.Travel",
        "BetaSharp.Entities.EntityLiving.Jump",
        "BetaSharp.Entities.Entity.Move",
        "BetaSharp.Entities.Entity.MoveNonSolid",
        "BetaSharp.Entities.Entity.ResolveCollisions",
        "BetaSharp.Entities.Entity.TryStepUp",
        "BetaSharp.Entities.Entity.ShortenStepOverLedge",
        "BetaSharp.Entities.Entity.PushOutOfBlocks",
        "BetaSharp.Entities.Entity.IsInFluid",
        "BetaSharp.Entities.Entity.GetEntitiesInside",
        "BetaSharp.Entities.Entity.SyncPositionToBoundingBox",
        "BetaSharp.Entities.Entity.UpdateBoundingBox",
        "BetaSharp.Entities.Entity.IsInsideWall",
    ];

    /// <summary>
    ///     The effect tail: everything the movement tick currently does <em>besides</em> physics —
    ///     block callbacks, footsteps, fire and water, and the AI invoked mid-tick from
    ///     <c>TickMovement</c>. Known impure, measured rather than asserted, and expected to shrink
    ///     to nothing across phases 4–6 as Cuts 1–3 land.
    ///     <para>
    ///         Kept under test because its <em>size</em> is the progress metric:
    ///         <see cref="StepPurityTests.Effect_tail_only_shrinks" /> ratchets it downward.
    ///     </para>
    /// </summary>
    public static readonly string[] TailRoots =
    [
        "BetaSharp.Entities.EntityPlayer.TickMovement",
        "BetaSharp.Entities.EntityLiving.TickMovement",
        "BetaSharp.Entities.Entity.AccumulateWalkDistance",
        "BetaSharp.Entities.Entity.NotifyBlocksOfCollision",
        "BetaSharp.Entities.Entity.ApplyFireAndWater",
        "BetaSharp.Entities.Entity.Fall",
    ];

    /// <summary>
    ///     Calls the walk records but does not traverse. Each is a place where movement hands off to
    ///     a subsystem that is <em>by design</em> on the far side of the boundary — the call itself
    ///     is the finding, and what the subsystem does internally is not movement's problem.
    ///     <para>
    ///         Without these the graph is useless rather than merely large: one edge,
    ///         <c>Entity.NotifyBlocksOfCollision → Block.onEntityCollision</c>, reaches
    ///         <c>IBlockInteractable</c>, thence every block behavior, thence <c>Block.OnTick</c> and
    ///         the entire world-tick and redstone system — 2,515 of BetaSharp's 7,673 methods, a
    ///         third of the engine, from one call at <c>Entity.Movement.cs:413</c>.
    ///     </para>
    ///     <para>
    ///         A cut point is an assertion that the edge is a deferral boundary, so adding one is a
    ///         design decision, not a way to quiet the test. Each must name what will sever it.
    ///     </para>
    /// </summary>
    public static readonly CutPoint[] CutPoints =
    [
        new("BetaSharp.Blocks.Block.onEntityCollision",
            "§3 risk #7 — block callbacks become StepEffects entries in phase 5"),
        new("BetaSharp.Worlds.Core.Systems.WorldEventBroadcaster.",
            "already banned at the call site; its internals are not movement's concern"),
        new("BetaSharp.Entities.EntityLiving.TickLiving",
            "§3 risk #8 — the AI, invoked mid-tick; hoisted out of the movement phase in Cut 1"),
        new("BetaSharp.Entities.EntityPlayer.PickupAndInventorySubtick",
            "§3 risk #9 — inventory mutation; leaves the movement phase in Cut 1"),
        new("BetaSharp.Entities.EntityPlayer.CollideWithPickupEntities",
            "§3 risk #9 — inventory mutation; leaves the movement phase in Cut 1"),
        new("BetaSharp.Entities.EntityPlayer.IncreaseStat",
            "§3 risk #10 — stat counters become StepEffects entries in phase 5"),
        new("BetaSharp.Entities.ServerPlayerEntity.IncreaseStat",
            "§3 risk #10 — stat counters become StepEffects entries in phase 5"),
    ];

    /// <summary>
    ///     Symbols that must not be reachable from <see cref="Roots" />. A pattern ending in
    ///     <c>'.'</c> bans every member of that type; otherwise the match is exact.
    /// </summary>
    public static readonly BannedSymbol[] Banned =
    [
        // Transcendentals: platform libm, not bit-guaranteed across OS or runtime version.
        new("System.Math.Sin", "libm — not bit-identical across platforms; use MathHelper's table"),
        new("System.Math.Cos", "libm — use MathHelper.Cos (table lookup)"),
        new("System.Math.Tan", "libm — no deterministic equivalent yet; see §5.2"),
        new("System.Math.Asin", "libm"),
        new("System.Math.Acos", "libm"),
        new("System.Math.Atan", "libm"),
        new("System.Math.Atan2", "libm — see §5.2 before replacing; facing feeds MoveNonSolid"),
        new("System.Math.Pow", "libm"),
        new("System.Math.Exp", "libm"),
        new("System.Math.Log", "libm"),
        new("System.Math.Log10", "libm"),
        new("System.Math.Cbrt", "libm"),
        new("System.MathF.Sin", "libm"),
        new("System.MathF.Cos", "libm"),
        new("System.MathF.Tan", "libm"),
        new("System.MathF.Atan", "libm"),
        new("System.MathF.Atan2", "libm"),
        new("System.MathF.Pow", "libm"),
        new("System.MathF.Exp", "libm"),
        new("System.MathF.Log", "libm"),

        // Randomness: the hazard is generator *state* advancing, not the value returned.
        // Re-simulation draws extra times and desyncs client from server permanently.
        new("BetaSharp.Util.Maths.JavaRandom.", "advances per-entity generator state; inject a stateless RNG keyed on (tick, entityId)"),
        new("System.Random.", "nondeterministic; and Random.Shared is process-global"),

        // Side effects: must be recorded into StepEffects, not executed inline, or reconciliation
        // replays them once per re-simulated tick.
        new("BetaSharp.Worlds.Core.Systems.WorldEventBroadcaster.", "side effect — route through StepEffects"),

        // Ambient nondeterminism.
        new("System.DateTime.Now", "wall clock"),
        new("System.DateTime.UtcNow", "wall clock"),
        new("System.DateTimeOffset.UtcNow", "wall clock"),
        new("System.Environment.TickCount", "wall clock"),
        new("System.Environment.TickCount64", "wall clock"),
        new("System.Diagnostics.Stopwatch.", "wall clock"),
        new("System.Guid.NewGuid", "nondeterministic"),
        new("System.Threading.Thread.", "no threading inside Step"),
        new("System.Threading.Tasks.Task.", "no async inside Step"),
        new("System.Object.GetHashCode", "reference hash codes vary per process"),
        new("System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode", "reference hash code"),
    ];

    /// <summary>
    ///     Reachable-and-known-impure sites that have not been fixed yet, each pinned to the
    ///     document section that tracks it. Every entry is a bug the plan has already identified;
    ///     the list exists so the test fails on <em>new</em> violations while the known ones are
    ///     being worked through in phases 5–6.
    ///     <para>
    ///         This list must only ever shrink. <see cref="StepPurityTests.Waivers_do_not_rot" />
    ///         fails when an entry stops matching anything, so a fixed site cannot be quietly left
    ///         waived.
    ///     </para>
    /// </summary>
    public static readonly Waiver[] Waivers =
    [
        new("BetaSharp.Entities.Entity.PushOutOfBlocks", "BetaSharp.Util.Maths.JavaRandom.",
            "§3 risk #1 — affects returned velocity; fixed in phase 6 by RNG injection"),
        new("BetaSharp.Entities.Entity.ApplyFireAndWater", "BetaSharp.Util.Maths.JavaRandom.",
            "§3 risk #4 — fizz sound pitch; fixed in phase 5 by StepEffects"),
        new("BetaSharp.Entities.Entity.ApplyFireAndWater", "BetaSharp.Worlds.Core.Systems.WorldEventBroadcaster.",
            "§3 risk #4 — fizz sound; fixed in phase 5 by StepEffects"),
        new("BetaSharp.Entities.Entity.AccumulateWalkDistance", "BetaSharp.Worlds.Core.Systems.WorldEventBroadcaster.",
            "§3 risk #5 — footstep sounds; fixed in phase 5 by StepEffects"),
    ];

    /// <summary>
    ///     Diagnostics tolerated in the synthesised compilation. Only for cases where the missing
    ///     piece provably cannot affect the call graph.
    /// </summary>
    public static readonly string[] ToleratedDiagnostics =
    [
        // [GeneratedRegex] partials. The source generator does not run in a hand-built compilation,
        // so the partial has no implementing part. The method symbol still exists and still binds;
        // only the generated body is absent, and a compiled regex matcher calls nothing that could
        // appear on the banned list. Affects 3 methods (ResourceLocation.Reg,
        // DimensionFileFilter.DimensionPattern, DataFilenameFilter.ChunkFilePattern).
        "CS8795",
    ];

    internal readonly record struct BannedSymbol(string Pattern, string Reason)
    {
        public bool Matches(string displayString) =>
            Pattern.EndsWith('.')
                ? displayString.StartsWith(Pattern, StringComparison.Ordinal)
                : displayString == Pattern;
    }

    /// <summary>An edge the walk records but refuses to traverse. See <see cref="CutPoints" />.</summary>
    internal readonly record struct CutPoint(string Pattern, string Reason)
    {
        public bool Matches(string displayString) =>
            Pattern.EndsWith('.')
                ? displayString.StartsWith(Pattern, StringComparison.Ordinal)
                : displayString == Pattern;
    }

    /// <summary>A known violation, allowed until the phase that fixes it lands.</summary>
    internal readonly record struct Waiver(string InMethod, string BannedPattern, string Reason);
}

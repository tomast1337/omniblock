namespace OmniBlock.Tests.Determinism;

/// <summary>
///     The machine-readable definition of "in the <c>Step()</c> path", and of what may not appear
///     there. Kept as data, separate from the analysis in <see cref="CallGraph" />, so that moving
///     the frontier is a one-line edit rather than a change to the engine.
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
        "OmniBlock.Entities.EntityLiving.Travel",
        "OmniBlock.Entities.EntityPlayer.Travel",
        "OmniBlock.Entities.EntityLiving.Jump",
        "OmniBlock.Entities.Entity.Move",
        "OmniBlock.Entities.Entity.MoveNonSolid",
        "OmniBlock.Entities.Entity.ResolveCollisions",
        "OmniBlock.Entities.Entity.TryStepUp",
        "OmniBlock.Entities.Entity.ShortenStepOverLedge",
        "OmniBlock.Entities.Entity.PushOutOfBlocks",
        "OmniBlock.Entities.Entity.IsInFluid",
        "OmniBlock.Entities.Entity.GetEntitiesInside",
        "OmniBlock.Entities.Entity.SyncPositionToBoundingBox",
        "OmniBlock.Entities.Entity.UpdateBoundingBox",
        "OmniBlock.Entities.Entity.IsInsideWall"
    ];

    /// <summary>
    ///     The effect tail: everything the movement tick currently does <em>besides</em> physics —
    ///     block callbacks, footsteps, fire and water, and the AI invoked mid-tick from
    ///     <c>TickMovement</c>. Known impure, measured rather than asserted, and expected to shrink
    ///     to nothing as each of those is hoisted out of the movement tick.
    ///     <para>
    ///         Kept under test because its <em>size</em> is the progress metric:
    ///         <see cref="StepPurityTests.Effect_tail_only_shrinks" /> ratchets it downward.
    ///     </para>
    /// </summary>
    public static readonly string[] TailRoots =
    [
        "OmniBlock.Entities.EntityPlayer.TickMovement",
        "OmniBlock.Entities.EntityLiving.TickMovement",
        "OmniBlock.Entities.Entity.AccumulateWalkDistance",
        "OmniBlock.Entities.Entity.NotifyBlocksOfCollision",
        "OmniBlock.Entities.Entity.ApplyFireAndWater",
        "OmniBlock.Entities.Entity.Fall"
    ];

    /// <summary>
    ///     Calls the walk records but does not traverse. Each is a place where movement hands off to
    ///     a subsystem that is <em>by design</em> on the far side of the boundary — the call itself
    ///     is the finding, and what the subsystem does internally is not movement's problem.
    ///     <para>
    ///         Without these the graph is useless rather than merely large: one edge,
    ///         <c>Entity.NotifyBlocksOfCollision → Block.onEntityCollision</c>, reaches
    ///         <c>IBlockInteractable</c>, thence every block behavior, thence <c>Block.OnTick</c> and
    ///         the entire world-tick and redstone system — 2,515 of OmniBlock's 7,673 methods, a
    ///         third of the engine, from one call at <c>Entity.Movement.cs:413</c>.
    ///     </para>
    ///     <para>
    ///         A cut point is an assertion that the edge is a deferral boundary, so adding one is a
    ///         design decision, not a way to quiet the test. Each must name what will sever it.
    ///     </para>
    /// </summary>
    public static readonly CutPoint[] CutPoints =
    [
        new("OmniBlock.Blocks.Block.onEntityCollision",
            "block callbacks; need to become StepEffects entries"),
        new("OmniBlock.Worlds.Core.Systems.WorldEventBroadcaster.",
            "already banned at the call site; its internals are not movement's concern"),
        new("OmniBlock.Entities.EntityLiving.TickLiving",
            "the AI, invoked mid-tick; belongs outside the movement step entirely"),
        new("OmniBlock.Entities.EntityPlayer.PickupAndInventorySubtick",
            "mutates the inventory; belongs outside the movement step entirely"),
        new("OmniBlock.Entities.EntityPlayer.CollideWithPickupEntities",
            "mutates the inventory; belongs outside the movement step entirely"),
        new("OmniBlock.Entities.EntityPlayer.IncreaseStat",
            "mutates stat counters; needs to become a StepEffects entry"),
        new("OmniBlock.Entities.ServerPlayerEntity.IncreaseStat",
            "mutates stat counters; needs to become a StepEffects entry")
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
        new("System.Math.Tan", "libm — no deterministic equivalent written yet"),
        new("System.Math.Asin", "libm"),
        new("System.Math.Acos", "libm"),
        new("System.Math.Atan", "libm"),
        new("System.Math.Atan2", "libm — replacing it changes facing, which feeds MoveNonSolid"),
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
        new("OmniBlock.Util.Maths.JavaRandom.", "advances per-entity generator state; inject a stateless RNG keyed on (tick, entityId)"),
        new("System.Random.", "nondeterministic; and Random.Shared is process-global"),

        // Side effects: must be recorded into StepEffects, not executed inline, or reconciliation
        // replays them once per re-simulated tick.
        new("OmniBlock.Worlds.Core.Systems.WorldEventBroadcaster.", "side effect — route through StepEffects"),

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
        new("System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode", "reference hash code")
    ];

    /// <summary>
    ///     Reachable-and-known-impure sites that have not been fixed yet, each with what makes it
    ///     impure and what fixing it takes. The list exists so the test fails on <em>new</em>
    ///     violations while these are worked through.
    ///     <para>
    ///         This list must only ever shrink. <see cref="StepPurityTests.Waivers_do_not_rot" />
    ///         fails when an entry stops matching anything, so a fixed site cannot be quietly left
    ///         waived.
    ///     </para>
    /// </summary>
    public static readonly Waiver[] Waivers =
    [
        new("OmniBlock.Entities.Entity.PushOutOfBlocks", "OmniBlock.Util.Maths.JavaRandom.",
            "draws from ambient RNG and the draw affects returned velocity; needs an injected generator"),
        new("OmniBlock.Entities.Entity.ApplyFireAndWater", "OmniBlock.Util.Maths.JavaRandom.",
            "draws from ambient RNG for the fizz sound's pitch; needs the sound routed through StepEffects"),
        new("OmniBlock.Entities.Entity.ApplyFireAndWater", "OmniBlock.Worlds.Core.Systems.WorldEventBroadcaster.",
            "emits the fizz sound directly; needs it routed through StepEffects"),
        new("OmniBlock.Entities.Entity.AccumulateWalkDistance", "OmniBlock.Worlds.Core.Systems.WorldEventBroadcaster.",
            "emits footstep sounds directly; needs them routed through StepEffects")
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
        "CS8795"
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

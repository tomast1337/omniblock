using System.Text;
using Microsoft.CodeAnalysis;

namespace OmniBlock.Tests.Determinism;

/// <summary>
///     Proves statically that the player movement path contains no platform-dependent math, no
///     ambient randomness, and no direct side effects — and keeps proving it, so a later commit
///     cannot quietly reintroduce one.
///     <para>
///         Two frontiers, because they are held to different standards. The <b>core</b> — collision
///         resolution and the state it derives — must be clean now, and
///         <see cref="Core_step_path_is_free_of_banned_symbols" /> fails if it is not. The
///         <b>effect tail</b> is known impure and is measured instead, ratcheting downward as Cuts
///         1–3 land.
///     </para>
///     <para>
///         The first three tests guard the analysis itself. A reachability test that silently stops
///         resolving symbols passes every purity assertion while proving nothing, which is the one
///         failure mode that would make this file worse than useless.
///     </para>
/// </summary>
public sealed class StepPurityTests
{
    /// <summary>
    ///     PARKED. The deterministic <c>Step()</c> extraction was deferred in favour of the
    ///     sandboxed modding API and the message layer. The analysis engine
    ///     (<see cref="CallGraph" />) is finished and its five self-checks still run — they keep the
    ///     compilation and the frontier declarations honest for whoever resumes this.
    ///     <para>
    ///         <b>Where it stopped.</b> The two-tier frontier and cut points were half-applied. The
    ///         walk from the physics core still explodes through three edges that need cut points
    ///         added to <see cref="StepFrontier.CutPoints" />:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <c>
    ///                 Entity.IsFootprintLoaded → ChunkHost.GetChunk → IChunkSource.GetChunk →
    ///                 ServerChunkCache.LoadChunk → DecorateTerrain
    ///             </c>
    ///             — reached from
    ///             <c>Entity.Movement.cs:121</c>. Worth keeping: it means the movement path can
    ///             trigger terrain generation, which is the concrete argument for
    ///             <c>ICollisionView</c> being a read-only view that cannot generate.
    ///         </item>
    ///         <item>
    ///             <c>Entity.Fall → Entity.OnLanding → EntityLiving.Damage</c> — the damage, death
    ///             and loot system, from <c>Entity.Movement.cs:182</c>.
    ///         </item>
    ///         <item>
    ///             <c>Block.onSteppedOn → IBlockInteractable.OnSteppedOn</c> — every block behavior,
    ///             from <c>Entity.Movement.cs:386</c>. Same shape as the
    ///             <c>onEntityCollision</c> cut point already declared.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>And one engine fix.</b> <see cref="CallGraph.Walk" /> applies cut points to
    ///         explicitly-supplied roots, so declaring a tail root as a cut point would stop the
    ///         tail walk at its own starting line. Roots must be exempt from the cut-point check.
    ///     </para>
    /// </summary>
    private const string ParkedReason =
        "Parked: deterministic Step() extraction deferred. See the class remarks for the three "
        + "remaining cut points and the Walk root-exemption fix needed to finish this.";

    /// <summary>
    ///     Methods reachable from <see cref="StepFrontier.TailRoots" /> today. The tail may shrink
    ///     but never grow; lower this number whenever a cut lands. Reaching 0 retires the tail and
    ///     folds its roots into the core.
    /// </summary>
    private const int TailBudget = 2515;

    // ---------------------------------------------------------------------------------------
    // Guards on the analysis
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Sources_compile_clean_enough_to_bind()
    {
        var graph = CallGraph.Instance;

        Assert.True(
            graph.CompilationErrors.IsEmpty,
            $"""
             The synthesised compilation of OmniBlock/ reported errors. Symbol binding is degraded,
             so reachability results below cannot be trusted. Usually this means a new package
             reference, or an SDK-generated source the analysis does not synthesise.

             First {graph.CompilationErrors.Length}:
               {string.Join($"{Environment.NewLine}  ", graph.CompilationErrors)}
             """);
    }

    [Fact]
    public void Every_declared_root_exists()
    {
        var graph = CallGraph.Instance;

        string[] missing =
        [
            .. StepFrontier.CoreRoots.Concat(StepFrontier.TailRoots)
                .Where(r => !graph.Resolve(r).Any())
        ];

        Assert.True(
            missing.Length == 0,
            $"""
             Roots declared in StepFrontier no longer exist. A renamed or removed method silently
             shrinks the analysed surface, so this fails rather than skipping them.

             Missing:
               {string.Join($"{Environment.NewLine}  ", missing)}
             """);
    }

    [Fact]
    public void Analysis_is_not_vacuous()
    {
        var graph = CallGraph.Instance;
        var core = WalkCore(graph);

        Assert.True(graph.AllMethods.Length > 2000, $"Only {graph.AllMethods.Length} methods found in OmniBlock — the compilation did not load.");
        Assert.True(core.Count > 50, $"Only {core.Count} methods reachable from the physics core — the call graph collapsed.");

        var unresolvedRatio = graph.TotalInvocations == 0
            ? 1.0
            : graph.UnresolvedInvocations / (double)graph.TotalInvocations;

        Assert.True(
            unresolvedRatio < 0.02,
            $"""
             {graph.UnresolvedInvocations:N0} of {graph.TotalInvocations:N0} invocations ({unresolvedRatio:P1})
             failed to bind. Unbound calls are invisible to the purity check, so a high ratio means
             this suite is reporting clean because it cannot see, not because the path is clean.
             """);
    }

    // ---------------------------------------------------------------------------------------
    // The proof
    // ---------------------------------------------------------------------------------------

    /// <summary>
    ///     The load-bearing assertion. Everything else in this file exists to keep it honest.
    /// </summary>
    [Fact(Skip = ParkedReason + " Last run: 2,015 methods reachable from the core, 2,211 violations. "
                              + "Needs the remaining cut points from the note above before it can be green.")]
    public void Core_step_path_is_free_of_banned_symbols()
    {
        var graph = CallGraph.Instance;
        var core = WalkCore(graph);

        List<Violation> violations = [.. FindViolations(core, true)];

        Assert.True(violations.Count == 0, Report(violations, core));
    }

    /// <summary>
    ///     The tail is allowed to be impure — it is what Cuts 1–3 remove — but it must not grow.
    ///     A rising number means new impurity was added to the movement tick rather than lifted out.
    /// </summary>
    [Fact(Skip = ParkedReason + " TailBudget is also stale: the tail measured 2,091 after the first "
                              + "cut points landed, and will move again once the rest do, so re-measure rather "
                              + "than trusting the constant.")]
    public void Effect_tail_only_shrinks()
    {
        var graph = CallGraph.Instance;
        var tail = graph.Walk(StepFrontier.TailRoots.SelectMany(graph.Resolve));

        Assert.True(
            tail.Count <= TailBudget,
            $"""
             The movement tick's effect tail reaches {tail.Count:N0} methods, over the budget of
             {TailBudget:N0}. Something impure was added to the movement path instead of being
             lifted out of it.
             """);

        // Ratchet: a cut that lands must lower the constant, or the budget stops meaning anything.
        Assert.True(
            tail.Count >= TailBudget - 50,
            $"""
             The effect tail is down to {tail.Count:N0}, well under the budget of {TailBudget:N0}.
             Good — now lower StepPurityTests.TailBudget to {tail.Count} to lock the gain in.
             """);
    }

    /// <summary>
    ///     A waiver that no longer matches anything means the site was fixed. Failing here forces
    ///     the waiver to be deleted, so the allow-list can only ever shrink and cannot silently
    ///     become permission for a future violation at the same address.
    /// </summary>
    [Fact]
    public void Waivers_do_not_rot()
    {
        var graph = CallGraph.Instance;
        var core = WalkCore(graph);

        HashSet<(string Method, string Pattern)> live =
        [
            .. FindViolations(core, false)
                .Select(v => (v.MethodName, v.Banned.Pattern))
        ];

        string[] stale =
        [
            .. StepFrontier.Waivers
                .Where(w => !live.Contains((w.InMethod, w.BannedPattern)))
                .Select(w => $"{w.InMethod} -> {w.BannedPattern}   ({w.Reason})")
        ];

        Assert.True(
            stale.Length == 0,
            $"""
             Waivers in StepFrontier.Waivers no longer match any reachable violation. If the site was
             fixed, delete the waiver — leaving it in place would silently permit a future regression
             at the same address.

             Stale:
               {string.Join($"{Environment.NewLine}  ", stale)}
             """);
    }

    /// <summary>
    ///     A cut point that the walk never reaches is a claim about the code that has stopped being
    ///     true. Same ratchet as <see cref="Waivers_do_not_rot" />: the list may only shrink.
    /// </summary>
    [Fact(Skip = ParkedReason + " Fails on the cut points that are declared but not yet reached, "
                              + "because Walk applies them to explicitly-supplied roots as well.")]
    public void Cut_points_do_not_rot()
    {
        var graph = CallGraph.Instance;

        var core = WalkCore(graph);
        var tail = graph.Walk(StepFrontier.TailRoots.SelectMany(graph.Resolve));

        HashSet<string> hit = [.. core.CutPointsHit, .. tail.CutPointsHit];

        string[] stale =
        [
            .. StepFrontier.CutPoints
                .Where(c => !hit.Contains(c.Pattern))
                .Select(c => $"{c.Pattern}   ({c.Reason})")
        ];

        Assert.True(
            stale.Length == 0,
            $"""
             Cut points in StepFrontier.CutPoints are never reached. Either the boundary was severed
             — in which case delete the cut point — or the frontier moved and the analysis is no
             longer looking where it thinks it is.

             Stale:
               {string.Join($"{Environment.NewLine}  ", stale)}
             """);
    }

    /// <summary>
    ///     Records the shape of both frontiers. Not an assertion — a number that makes the effect of
    ///     each cut visible in the diff.
    /// </summary>
    [Fact]
    public void Report_frontier_size()
    {
        var graph = CallGraph.Instance;
        var core = WalkCore(graph);
        var tail = graph.Walk(StepFrontier.TailRoots.SelectMany(graph.Resolve));

        // Visible with `dotnet test --logger "console;verbosity=detailed"`.
        Console.WriteLine(
            $"""
             Step frontier reachability
               methods in OmniBlock     : {graph.AllMethods.Length:N0}
               reachable from core      : {core.Count:N0}
               reachable from tail      : {tail.Count:N0}   (budget {TailBudget:N0})
               invocations bound        : {graph.TotalInvocations - graph.UnresolvedInvocations:N0}/{graph.TotalInvocations:N0}
               core violations          : {FindViolations(core, true).Count()}
               active waivers           : {StepFrontier.Waivers.Length}
               active cut points        : {StepFrontier.CutPoints.Length}
             """);

        Assert.True(core.Count > 0);
    }

    // ---------------------------------------------------------------------------------------

    private static CallGraph.Reachability WalkCore(CallGraph graph) =>
        graph.Walk(StepFrontier.CoreRoots.SelectMany(graph.Resolve));

    private static IEnumerable<Violation> FindViolations(CallGraph.Reachability reachable, bool applyWaivers)
    {
        foreach (var method in reachable.Methods)
        {
            var methodName = CallGraph.NameOf(method);

            foreach (var reference in reachable.ReferencesFrom(method))
            {
                foreach (var banned in StepFrontier.Banned)
                {
                    if (!banned.Matches(reference.Name))
                    {
                        continue;
                    }

                    if (applyWaivers
                        && StepFrontier.Waivers.Any(w => w.InMethod == methodName && w.BannedPattern == banned.Pattern))
                    {
                        continue;
                    }

                    yield return new Violation(method, methodName, reference, banned);
                }
            }
        }
    }

    private static string Report(List<Violation> violations, CallGraph.Reachability reachable)
    {
        StringBuilder report = new();
        report.AppendLine();
        report.AppendLine($"{violations.Count} banned symbol(s) reachable from the physics core.");

        foreach (var group in violations.GroupBy(v => v.Banned.Pattern).OrderBy(g => g.Key))
        {
            report.AppendLine($"  {group.Key}");
            report.AppendLine($"    why banned: {group.First().Banned.Reason}");

            foreach (var violation in group.DistinctBy(v => (v.MethodName, v.Reference.Name)).Take(10))
            {
                var span = violation.Reference.At.GetLineSpan();
                report.AppendLine();
                report.AppendLine($"    {violation.Reference.Name}");
                report.AppendLine($"      at {Path.GetFileName(span.Path)}:{span.StartLinePosition.Line + 1}");
                report.AppendLine("      reached by:");
                report.AppendLine($"        {reachable.DescribePath(violation.Method)}");
            }

            report.AppendLine();
        }

        report.AppendLine("If a site is known and scheduled, add it to StepFrontier.Waivers with the phase that fixes it.");
        report.AppendLine("If it is a handoff to another subsystem, add a StepFrontier.CutPoint instead — and say what will sever it.");
        return report.ToString();
    }

    private readonly record struct Violation(
        IMethodSymbol Method,
        string MethodName,
        CallGraph.Reference Reference,
        StepFrontier.BannedSymbol Banned);
}

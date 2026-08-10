using System.Diagnostics;

namespace OmniBlock.Util;

/// <summary>
///     Runtime tripwire for the deterministic movement path.
///     <para>
///         Static call-graph analysis (see <c>OmniBlock.Tests/Determinism/StepPurityTests.cs</c>)
///         proves what is <em>written</em>, but it cannot see through the virtual dispatch this
///         codebase is built on: <c>IEntityPhysics.Travel</c>, the <c>Ticker</c> hooks,
///         <c>Block.Blocks[id].slipperiness</c>, and JSON-selected behavior composition. This guard
///         catches what actually <em>executes</em>.
///     </para>
///     <para>
///         Compiled out entirely unless <c>DETERMINISM_GUARD</c> is defined. Every method that costs
///         anything is <see cref="ConditionalAttribute" />-gated, so release builds carry no branch,
///         no thread-static read, and no call — which matters because the primary call site is
///         <c>JavaRandom.Next(int)</c>, an aggressively-inlined hot path.
///     </para>
/// </summary>
public static class DeterminismGuard
{
    [ThreadStatic]
    private static int s_depth;

    [ThreadStatic]
    private static List<string>? s_violations;

    /// <summary>
    ///     True while the calling thread is inside a region declared deterministic. Always false
    ///     when the guard is compiled out.
    /// </summary>
    public static bool Active
    {
        get
        {
#if DETERMINISM_GUARD
            return s_depth > 0;
#else
            return false;
#endif
        }
    }

    /// <summary>
    ///     Controls what a violation does. Throwing is right for tests; collecting is right for a
    ///     soak run where you want the whole list rather than the first one.
    /// </summary>
    public static DeterminismGuardMode Mode { get; set; } = DeterminismGuardMode.Throw;

    /// <summary>
    ///     Marks the calling thread as executing deterministic code until the returned scope is
    ///     disposed. Reentrant: nested scopes nest, and the region ends when the outermost closes.
    /// </summary>
    /// <example>
    ///     <code>
    ///     using (DeterminismGuard.Enter())
    ///     {
    ///         state = PlayerMovement.Step(state, input, view, ref effects);
    ///     }
    ///     </code>
    /// </example>
    public static Scope Enter()
    {
#if DETERMINISM_GUARD
        s_depth++;
#endif
        return default;
    }

    /// <summary>
    ///     Declares that the calling code is impure. Does nothing outside a deterministic region;
    ///     inside one it throws or records, per <see cref="Mode" />.
    ///     <para>
    ///         <paramref name="operation" /> should name the impurity, not the method — "JavaRandom
    ///         draw" and "sound broadcast" are what a failure message needs to be actionable.
    ///     </para>
    /// </summary>
    [Conditional("DETERMINISM_GUARD")]
    public static void AssertPure(string operation)
    {
        if (s_depth <= 0)
        {
            return;
        }

        string message =
            $"Impure operation '{operation}' executed inside a deterministic region. "
            + "Movement code must not mutate ambient state or emit side effects directly; "
            + "route effects through StepEffects and randomness through the injected generator.";

        if (Mode == DeterminismGuardMode.Collect)
        {
            (s_violations ??= []).Add(message);
            return;
        }

        // The depth is reset so that a caller that swallows this exception does not leave every
        // subsequent call on this thread reporting a violation.
        s_depth = 0;
        throw new DeterminismViolationException(message);
    }

    /// <summary>
    ///     Violations recorded on this thread under <see cref="DeterminismGuardMode.Collect" />,
    ///     and clears the list. Empty when the guard is compiled out.
    /// </summary>
    public static IReadOnlyList<string> DrainViolations()
    {
        List<string>? collected = s_violations;
        s_violations = null;
        return collected ?? (IReadOnlyList<string>)[];
    }

    /// <summary>
    ///     Closes the region opened by <see cref="Enter" />. A struct with no fields so that the
    ///     <c>using</c> costs nothing when the guard is compiled out.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
#if DETERMINISM_GUARD
            if (s_depth > 0)
            {
                s_depth--;
            }
#endif
        }
    }
}

public enum DeterminismGuardMode
{
    /// <summary>Throw on the first violation. The default, and what tests want.</summary>
    Throw,

    /// <summary>Record and continue, so one run yields the full list. For soak runs.</summary>
    Collect,
}

public sealed class DeterminismViolationException(string message) : Exception(message);

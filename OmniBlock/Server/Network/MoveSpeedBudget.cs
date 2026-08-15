namespace OmniBlock.Server.Network;

/// <summary>
///     Token bucket backing <see cref="ServerPlayNetworkHandler" />'s "moved too quickly" check.
///     <para>
///         Comparing each move packet's delta against a flat per-packet ceiling bounds a single
///         packet's claimed movement but not the claimed movement rate, since nothing bounds how
///         many packets a client sends per second. This refills at exactly the rate vanilla intended
///         (<see cref="MaxDistanceSqPerTick" /> squared units per <see cref="MillisecondsPerTick" />
///         ms of real elapsed time) and every accepted move consumes from it, so the total distance
///         allowed over any stretch of real time is capped regardless of how many packets that
///         distance was split across.
///     </para>
/// </summary>
internal static class MoveSpeedBudget
{
    public const double MaxDistanceSqPerTick = 100.0;
    public const double MillisecondsPerTick = 50.0;

    /// <summary>
    ///     How many ticks' worth of budget can bank up while idle. Bounds how far a client can
    ///     stand still and then dash, while staying generous enough to absorb ordinary network
    ///     jitter and read-queue backlog without falsely kicking a laggy-but-legitimate client whose
    ///     backlog drains as a burst of packets in one server tick.
    /// </summary>
    public const double BankedTicks = 4.0;

    /// <summary>Refills <paramref name="currentBudgetSq" /> by elapsed real time, then attempts to spend <paramref name="movedDistanceSq" /> from it.</summary>
    public static MoveBudgetResult Evaluate(double currentBudgetSq, double elapsedMs, double movedDistanceSq)
    {
        double refilled = elapsedMs > 0
            ? Math.Min(
                currentBudgetSq + elapsedMs / MillisecondsPerTick * MaxDistanceSqPerTick,
                MaxDistanceSqPerTick * BankedTicks)
            : currentBudgetSq;

        return movedDistanceSq > refilled
            ? new MoveBudgetResult(refilled, ExceededBudget: true)
            : new MoveBudgetResult(refilled - movedDistanceSq, ExceededBudget: false);
    }
}

internal readonly record struct MoveBudgetResult(double RemainingBudgetSq, bool ExceededBudget);

using OmniBlock.Server.Network;

namespace OmniBlock.Tests.Network;

/// <summary>
///     Anti-speed-hack fix: <see cref="ServerPlayNetworkHandler" />'s old "moved too quickly" check
///     compared each move packet's delta against a flat per-packet ceiling, which bounds a single
///     packet's claimed distance but not the claimed distance rate — nothing stopped a client from
///     sending far more packets per second than vanilla's tick rate and moving proportionally
///     faster while every individual packet still passed. <see cref="MoveSpeedBudget" /> replaces
///     that with a token bucket keyed to real elapsed time, so these tests exercise it directly.
/// </summary>
public sealed class MoveSpeedBudgetTests
{
    [Fact]
    public void Evaluate_aSingleVanillaSizedMove_afterOneTickOfElapsedTime_isAccepted()
    {
        var result = MoveSpeedBudget.Evaluate(
            MoveSpeedBudget.MaxDistanceSqPerTick,
            MoveSpeedBudget.MillisecondsPerTick,
            MoveSpeedBudget.MaxDistanceSqPerTick - 1.0);

        Assert.False(result.ExceededBudget);
    }

    [Fact]
    public void Evaluate_manySmallMoves_inTheSameTick_cannotExceedOneTicksTotalDistance()
    {
        // The exploit this closes: flood packets faster than the server tick rate, each individually
        // legal-sized, to move faster than one tick's budget should allow in real time.
        var budget = MoveSpeedBudget.MaxDistanceSqPerTick;
        var perPacketDistanceSq = MoveSpeedBudget.MaxDistanceSqPerTick / 10.0;
        var accepted = 0;

        for (var i = 0; i < 1000; i++)
        {
            // No time passes between these packets: they all land within the same instant, as a
            // flood would.
            var result = MoveSpeedBudget.Evaluate(budget, 0, perPacketDistanceSq);
            if (result.ExceededBudget)
            {
                break;
            }

            budget = result.RemainingBudgetSq;
            accepted++;
        }

        var totalDistanceMovedSq = accepted * perPacketDistanceSq;
        Assert.True(
            totalDistanceMovedSq <= MoveSpeedBudget.MaxDistanceSqPerTick,
            $"Flooding packets allowed {totalDistanceMovedSq} sq. units of movement with no elapsed time, more than one tick's {MoveSpeedBudget.MaxDistanceSqPerTick} sq. unit budget.");
    }

    [Fact]
    public void Evaluate_aMoveLargerThanTheBudget_isRejected_andBudgetIsUnchanged()
    {
        var budget = MoveSpeedBudget.MaxDistanceSqPerTick;

        var result = MoveSpeedBudget.Evaluate(budget, 0, budget + 1.0);

        Assert.True(result.ExceededBudget);
        Assert.Equal(budget, result.RemainingBudgetSq);
    }

    [Fact]
    public void Evaluate_afterALongIdleGap_budgetBanksNoMoreThanTheCappedTicks()
    {
        var hugeElapsedMs = MoveSpeedBudget.MillisecondsPerTick * 1000;

        var result = MoveSpeedBudget.Evaluate(0, hugeElapsedMs, 0);

        Assert.Equal(MoveSpeedBudget.MaxDistanceSqPerTick * MoveSpeedBudget.BankedTicks, result.RemainingBudgetSq);
    }

    [Fact]
    public void Evaluate_aBackloggedBurstWithinTheBankedWindow_isAccepted()
    {
        // A laggy-but-legitimate client: several ticks' worth of real gameplay time passed, then its
        // backlogged packets all drain within one server tick. The elapsed real time between the
        // previous check and this one should cover the combined distance.
        var elapsedMs = MoveSpeedBudget.MillisecondsPerTick * 3;
        var combinedLegitimateDistanceSq = MoveSpeedBudget.MaxDistanceSqPerTick * 3 - 1.0;

        var result = MoveSpeedBudget.Evaluate(
            MoveSpeedBudget.MaxDistanceSqPerTick,
            elapsedMs,
            combinedLegitimateDistanceSq);

        Assert.False(result.ExceededBudget);
    }
}

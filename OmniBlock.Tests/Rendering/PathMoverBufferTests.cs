using OmniBlock.Client.Rendering.PathMovers;

namespace OmniBlock.Tests.Rendering;

public sealed class PathMoverBufferTests
{
    [Fact]
    public void Spawn_setsStartEndAndInitialPositionToStart()
    {
        PathMoverBuffer buf = new();
        int i = buf.Spawn(0, 0, 0, 10, 0, 0, worldUnitsPerTick: 1.0, iconIndex: 5, scale: 1.0f);

        Assert.Equal(1, buf.Count);
        Assert.Equal(0.0, buf.X[i]);
        Assert.Equal(0.0, buf.Y[i]);
        Assert.Equal(0.0, buf.Z[i]);
        Assert.Equal(10.0, buf.EndX[i]);
        Assert.Equal(5, buf.IconIndex[i]);
    }

    [Fact]
    public void Spawn_computesProgressPerTickFromSpeedAndPathLength()
    {
        PathMoverBuffer buf = new();
        int i = buf.Spawn(0, 0, 0, 10, 0, 0, worldUnitsPerTick: 2.0, iconIndex: 0, scale: 1.0f);

        Assert.Equal(0.2f, buf.ProgressPerTick[i], precision: 5);
    }

    [Fact]
    public void Tick_movesLinearlyAlongTheStartToEndSegment()
    {
        PathMoverBuffer buf = new();
        buf.Spawn(0, 0, 0, 10, 20, 0, worldUnitsPerTick: 1.0, iconIndex: 0, scale: 1.0f);

        PathMoverUpdater.Tick(buf);

        double progress = buf.Progress[0];
        Assert.Equal(10.0 * progress, buf.X[0], precision: 6);
        Assert.Equal(20.0 * progress, buf.Y[0], precision: 6);
        Assert.True(progress > 0 && progress < 1);
    }

    [Fact]
    public void Tick_marksDeadAndCompactsWhenProgressReachesOne()
    {
        PathMoverBuffer buf = new();
        buf.Spawn(0, 0, 0, 1, 0, 0, worldUnitsPerTick: 10.0, iconIndex: 0, scale: 1.0f);
        Assert.Equal(1, buf.Count);

        PathMoverUpdater.Tick(buf);

        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void Spawn_zeroLengthPathCompletesInOneTickWithoutDividingByZero()
    {
        PathMoverBuffer buf = new();
        buf.Spawn(3, 4, 5, 3, 4, 5, worldUnitsPerTick: 1.0, iconIndex: 0, scale: 1.0f);
        Assert.Equal(1, buf.Count);

        PathMoverUpdater.Tick(buf);

        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void Tick_snapshotsPreviousPositionBeforeAdvancing()
    {
        PathMoverBuffer buf = new();
        buf.Spawn(0, 0, 0, 100, 0, 0, worldUnitsPerTick: 1.0, iconIndex: 0, scale: 1.0f);

        double xBeforeTick = buf.X[0];
        PathMoverUpdater.Tick(buf);

        Assert.Equal(xBeforeTick, buf.PrevX[0]);
        Assert.NotEqual(buf.PrevX[0], buf.X[0]);
    }
}

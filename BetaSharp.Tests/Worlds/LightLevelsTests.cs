using OmniBlock.Worlds.Core.Systems;
using Xunit;

namespace OmniBlock.Tests.Worlds;

public class LightLevelsTests
{
    private static int Collapse(LightLevels levels, int ambientDarkness) =>
        Math.Max(levels.Block, levels.Sky - ambientDarkness);

    /// <summary>
    ///     The claim <see cref="ILightProvider.GetLightLevels" /> rests on: a block that emits can
    ///     have its floor applied to the block channel alone, rather than to the collapsed value the
    ///     old path floored, and the two still agree once the channels are collapsed.
    /// </summary>
    [Fact]
    public void FlooringTheBlockChannelMatchesFlooringTheCollapsedValue()
    {
        for (int sky = 0; sky <= 15; sky++)
        {
            for (int block = 0; block <= 15; block++)
            {
                for (int emission = 0; emission <= 15; emission++)
                {
                    for (int ambientDarkness = 0; ambientDarkness <= 15; ambientDarkness++)
                    {
                        LightLevels levels = LightLevels.Of(sky, block);
                        int collapsedThenFloored = Math.Max(Collapse(levels, ambientDarkness), emission);
                        int flooredThenCollapsed = Collapse(levels.WithBlockFloor(emission), ambientDarkness);

                        Assert.Equal(collapsedThenFloored, flooredThenCollapsed);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Taking the brighter neighbour per channel is not the same as taking the brighter collapsed
    ///     neighbour, and this pins an example rather than leaving the difference to be discovered as
    ///     a shading change nobody can explain. A cell in shadow under a torch and one in the open
    ///     with no torch collapse to the same level, but keep different channels.
    /// </summary>
    [Fact]
    public void TakingTheMaxPerChannelIsNotTheCollapsedMax()
    {
        LightLevels litByTorch = LightLevels.Of(sky: 0, block: 12);
        LightLevels litBySun = LightLevels.Of(sky: 12, block: 0);

        Assert.Equal(12, Collapse(litByTorch, 0));
        Assert.Equal(12, Collapse(litBySun, 0));

        // Collapsed, the brighter of the two is 12. Per channel it is both at once.
        Assert.Equal(LightLevels.Of(12, 12), litByTorch.Max(litBySun));
        Assert.Equal(12, Collapse(litByTorch.Max(litBySun), 0));

        // And they part company as soon as the sun goes down, which is the point of keeping them.
        Assert.Equal(12, Collapse(litByTorch, 11));
        Assert.Equal(1, Collapse(litBySun, 11));
    }

    [Fact]
    public void OutsideALoadedChunkIsFullSunAndNoTorch()
    {
        Assert.Equal(15, LightLevels.FullSky.Sky);
        Assert.Equal(0, LightLevels.FullSky.Block);
    }

    [Fact]
    public void LevelsAreClampedToWhatANibbleCanHold()
    {
        Assert.Equal(LightLevels.Of(15, 0), LightLevels.Of(99, -4));
    }
}

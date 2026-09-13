using OmniBlock.Client.Worlds;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Rendering;

public sealed class ClientEntityDespawnVisualTests
{
    [Fact]
    public void Presentation_progress_is_bounded_and_expires_after_twelve_ticks()
    {
        var entity = TestEntityCatalog.ByName("creeper").Create(new FakeWorldContext());
        var visual = new ClientEntityDespawnVisual(entity);

        Assert.Equal(0, visual.Progress(0));
        for (var tick = 1; tick < ClientEntityDespawnVisual.DurationTicks; tick++)
        {
            Assert.True(visual.Tick());
            Assert.InRange(visual.Progress(0), 0, 1);
        }

        Assert.False(visual.Tick());
        Assert.Equal(1, visual.Progress(0));
    }
}

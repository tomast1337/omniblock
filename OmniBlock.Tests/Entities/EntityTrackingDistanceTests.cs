using OmniBlock.Server.Entities;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Entities;

public sealed class EntityTrackingDistanceTests
{
    [Fact]
    public void Existing_entry_tracks_runtime_terrain_distance_without_exceeding_its_type_limit()
    {
        var cow = TestEntityCatalog.ByName("cow").Create(new FakeWorldContext());
        EntityTrackerEntry entry = new(cow, 144, 3, false, 512);

        entry.SetViewDistance(496);
        Assert.Equal(496, entry.trackedDistance);

        entry.SetViewDistance(80);
        Assert.Equal(80, entry.trackedDistance);

        EntityTrackerEntry shortRange = new(cow, 64, 3, false, 64);
        shortRange.SetViewDistance(496);
        Assert.Equal(64, shortRange.trackedDistance);
    }
}

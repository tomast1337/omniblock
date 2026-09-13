using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Rendering;

public sealed class DistantMobIdlePoseTests
{
    [Fact]
    public void Paused_mob_pose_is_deterministic_bounded_and_loops()
    {
        var world = new FakeWorldContext();
        var player = new TestEntityPlayer(world);
        var cow = (EntityLiving)TestEntityCatalog.ByName("cow").Create(world);
        player.SetPosition(0, 64, 0);
        cow.SetPosition(96, 64, 0);
        cow.Yaw = cow.PrevYaw = cow.Pitch = cow.PrevPitch = cow.BodyYaw = 0;

        var first = DistantMobIdlePose.Sample(cow, player, 2, 75, 0.25f);
        var repeated = DistantMobIdlePose.Sample(cow, player, 2, 75, 0.25f);
        var looped = DistantMobIdlePose.Sample(cow, player, 2, 395, 0.25f);

        Assert.NotNull(first);
        Assert.Equal(first, repeated);
        Assert.Equal(first, looped);
        Assert.InRange(first.Value.BodyYaw - cow.BodyYaw, -5, 7);
        Assert.InRange(first.Value.HeadYaw - cow.Yaw, -18, 24);
        Assert.InRange(first.Value.Pitch - cow.Pitch, 0, 4);
    }

    [Fact]
    public void Active_mobs_players_and_dead_mobs_receive_no_cosmetic_pose()
    {
        var world = new FakeWorldContext();
        var player = new TestEntityPlayer(world);
        var cow = TestEntityCatalog.ByName("cow").Create(world);
        player.SetPosition(0, 64, 0);
        cow.SetPosition(16, 64, 0);

        Assert.Null(DistantMobIdlePose.Sample(cow, player, 2, 20, 0));
        Assert.Null(DistantMobIdlePose.Sample(player, player, 2, 20, 0));

        cow.SetPosition(96, 64, 0);
        cow.MarkDead();
        Assert.Null(DistantMobIdlePose.Sample(cow, player, 2, 20, 0));
    }

    [Fact]
    public void Position_changes_the_quiet_loop_phase_without_using_random_state()
    {
        var world = new FakeWorldContext();
        var player = new TestEntityPlayer(world);
        var firstCow = TestEntityCatalog.ByName("cow").Create(world);
        var secondCow = TestEntityCatalog.ByName("cow").Create(world);
        player.SetPosition(0, 64, 0);
        firstCow.SetPosition(96, 64, 0);
        secondCow.SetPosition(97, 64, 0);

        var differs = Enumerable.Range(0, 320).Any(time =>
            DistantMobIdlePose.Sample(firstCow, player, 2, time, 0) !=
            DistantMobIdlePose.Sample(secondCow, player, 2, time, 0));

        Assert.True(differs);
    }
}

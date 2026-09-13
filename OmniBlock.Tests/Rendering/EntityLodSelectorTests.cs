using System.Numerics;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;

namespace OmniBlock.Tests.Rendering;

public sealed class EntityLodSelectorTests
{
    private readonly object _world = new(), _content = new(), _entity = new();
    private readonly EntityLodSelector _selector = new();
    private EntityLodSelector.Decision Select(double distance = 100, double diameter = 2, double fov = 70,
        int height = 480, string variant = "standing", object? entity = null, bool supported = true) =>
        _selector.Select(entity ?? _entity, new LodPoint(0, 0, distance), true, supported,
            variant, diameter, 0, Vector3.UnitZ, fov, height);
    private void Begin(long generation = 0) => _selector.BeginFrame(_world, _content, generation, default);

    [Fact]
    public void Distance_band_uses_hysteresis_without_changing_actual_representation()
    {
        Begin();
        Assert.Equal(EntityLodTier.Model, Select(80).Intended);
        Assert.Equal(EntityLodTier.Impostor, Select(81).Intended);
        Assert.Equal(EntityLodTier.Impostor, Select(70).Intended);
        Assert.Equal(EntityLodTier.Impostor, Select(64).Intended);
        Assert.Equal(EntityLodTier.Model, Select(63).Intended);
        _selector.EndFrame();
        Assert.Equal(5, _selector.Last.ModelDraws);
        Assert.Equal(0, _selector.Last.ImpostorDraws);
        Assert.Equal(2, _selector.Last.TierTransitions);
    }

    [Theory]
    [InlineData(2, 70, 480, true)]
    [InlineData(0.1, 70, 480, true)]
    [InlineData(12, 70, 480, false)]
    [InlineData(2, 10, 480, false)]
    [InlineData(2, 70, 2160, false)]
    public void Projected_bounds_account_for_model_size_viewport_and_zoom(double size, double fov, int height, bool impostor)
    {
        Begin();
        Assert.Equal(impostor, Select(diameter: size, fov: fov, height: height).Intended == EntityLodTier.Impostor);
    }

    [Fact]
    public void Pixel_band_does_not_chatter_and_zoom_forces_immediate_model()
    {
        Begin();
        Assert.Equal(EntityLodTier.Impostor, Select().Intended);
        // 2 * 1700 / (2 * 99 * tan(35)) ~24.5: between pixel thresholds.
        Assert.Equal(EntityLodTier.Impostor, Select(height: 1700).Intended);
        Assert.Equal(EntityLodTier.Model, Select(height: 2300).Intended);
        Assert.Equal(EntityLodTier.Model, Select(height: 1700).Intended);
        Assert.Equal(EntityLodTier.Impostor, Select().Intended);
        Assert.Equal(EntityLodTier.Model, Select(fov: 5).Intended);
    }

    [Fact]
    public void Off_axis_near_depth_is_not_replaced_by_radial_distance()
    {
        Begin();
        var choice = _selector.Select(_entity, new LodPoint(150, 0, 5), true, true, "standing",
            2, 0, Vector3.UnitZ, 70, 480);
        Assert.Equal(EntityLodTier.Model, choice.Intended);
        Assert.True(choice.ProjectedPixels > 32);
    }

    [Theory]
    [InlineData(double.NaN, 70, 480)]
    [InlineData(2, 0, 480)]
    [InlineData(2, 179, 480)]
    [InlineData(2, 70, 0)]
    public void Invalid_projection_fails_closed(double size, double fov, int height)
    {
        Begin();
        Assert.Equal(EntityLodReason.InvalidView, Select(diameter: size, fov: fov, height: height).Reason);
        Assert.Equal(0, _selector.StateCount);
    }

    [Fact]
    public void Unsupported_state_is_rechecked_and_forgets_previous_intended_tier()
    {
        Begin(); Select(81);
        Assert.Equal(EntityLodReason.UnsupportedState, Select(75, supported: false).Reason);
        Assert.Equal(0, _selector.StateCount);
        Assert.Equal(EntityLodTier.Model, Select(75).Intended);
    }

    [Fact]
    public void Variant_change_resets_hysteresis()
    {
        Begin(); Select(81);
        Assert.Equal(EntityLodTier.Model, Select(75, variant: "changed").Intended);
    }

    [Theory]
    [InlineData("world")]
    [InlineData("content")]
    [InlineData("resources")]
    [InlineData("cameraTeleport")]
    public void Session_and_generation_boundaries_clear_state(string cause)
    {
        Begin(); Select(81); _selector.EndFrame();
        _selector.BeginFrame(cause == "world" ? new object() : _world,
            cause == "content" ? new object() : _content, cause == "resources" ? 1 : 0,
            cause == "cameraTeleport" ? new LodPoint(0, 0, 40) : default);
        Assert.Equal(0, _selector.StateCount);
        Assert.Equal(EntityLodTier.Model, Select(75).Intended);
    }

    [Fact]
    public void Teleported_entity_forgets_tier_even_without_camera_motion()
    {
        Begin(); Select(100);
        Assert.Equal(EntityLodTier.Model, Select(75).Intended);
    }

    [Fact]
    public void Explicit_short_teleport_does_not_depend_on_displacement_threshold()
    {
        Begin(); Select(81);
        _selector.Forget(_entity);
        Assert.Equal(EntityLodTier.Model, Select(79).Intended);
    }

    private sealed record NetworkIdentity(int Id);
    [Fact]
    public void Equal_network_ids_do_not_share_lifetime_state_and_unseen_entries_are_removed()
    {
        var first = new NetworkIdentity(7); var replacement = new NetworkIdentity(7);
        Begin(); Select(81, entity: first); _selector.EndFrame();
        Begin();
        Assert.Equal(EntityLodTier.Model, Select(75, entity: replacement).Intended);
        _selector.EndFrame(); Assert.Equal(1, _selector.StateCount);
        Begin(); _selector.EndFrame(); Assert.Equal(0, _selector.StateCount);
    }

    [Fact]
    public void Capacity_is_bounded_and_overflow_falls_back_without_hiding_entities()
    {
        EntityLodSelector selector = new(2);
        selector.BeginFrame(_world, _content, 0, default);
        for (var i = 0; i < 3; i++) selector.Select(new object(), new LodPoint(0, 0, 100), true, true,
            "standing", 2, 0, Vector3.UnitZ, 70, 480);
        selector.EndFrame();
        Assert.Equal(2, selector.StateCount);
        Assert.Equal(1, selector.Last.CapacityFallbacks);
        Assert.Equal(3, selector.Last.ModelDraws);
        Assert.Equal(0, selector.Last.ImpostorDraws);
    }

    [Fact]
    public void All_26_directions_are_unique_unit_vectors_and_select_themselves()
    {
        HashSet<Vector3> vectors = [];
        for (var i = 0; i < 26; i++)
        {
            var direction = EntityLodDirections.Get(i);
            Assert.True(vectors.Add(direction));
            Assert.InRange(direction.Length(), 0.99999f, 1.00001f);
            Assert.Equal(i, EntityLodDirections.Select(direction, 0));
        }
        Assert.Equal(24, EntityLodDirections.Select(Vector3.UnitY, 123));
        Assert.Equal(25, EntityLodDirections.Select(-Vector3.UnitY, -57));
        Assert.Equal(-1, EntityLodDirections.Select(Vector3.Zero, 0));
    }

    [Fact]
    public void Direction_boundaries_have_angular_hysteresis()
    {
        static Vector3 Angle(double a) => new((float)Math.Sin(a * Math.PI / 180), 0, (float)Math.Cos(a * Math.PI / 180));
        Assert.Equal(8, EntityLodDirections.Select(Angle(22), 0));
        Assert.Equal(9, EntityLodDirections.Select(Angle(23), 0));
        Assert.Equal(8, EntityLodDirections.Select(Angle(23), 0, 8));
        Assert.Equal(9, EntityLodDirections.Select(Angle(30), 0, 8));
        Assert.Equal(8, EntityLodDirections.Select(Angle(-1), 0));
    }

    [Fact]
    public void Elevation_ring_and_pole_boundaries_have_angular_hysteresis()
    {
        static Vector3 Elevation(double degrees) => new(0,
            (float)Math.Sin(degrees * Math.PI / 180),
            (float)Math.Cos(degrees * Math.PI / 180));

        Assert.Equal(8, EntityLodDirections.Select(Elevation(22), 0));
        Assert.Equal(16, EntityLodDirections.Select(Elevation(23), 0));
        Assert.Equal(8, EntityLodDirections.Select(Elevation(23), 0, 8));
        Assert.Equal(16, EntityLodDirections.Select(Elevation(35), 0, 8));
        Assert.Equal(24, EntityLodDirections.Select(Elevation(68), 0));
        Assert.Equal(16, EntityLodDirections.Select(Elevation(68), 0, 16));
        Assert.Equal(24, EntityLodDirections.Select(Elevation(80), 0, 16));

        Assert.Equal(8, EntityLodDirections.Select(Elevation(-22), 0));
        Assert.Equal(25, EntityLodDirections.Select(Elevation(-80), 0, 0));
    }

    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(-90)] [InlineData(360)] [InlineData(-720)]
    public void Direction_selection_matches_engine_entity_forward(float yaw)
    {
        var cow = (EntityLiving)EntityRenderBaseline.CreateEntities(new FakeWorldContext(), "cow", 1, 16, 0, 220, 0)[0];
        cow.PrevYaw = cow.Yaw = yaw;
        var forward = cow.GetLook(1);
        Assert.Equal(8, EntityLodDirections.Select(new Vector3((float)forward.X, (float)forward.Y, (float)forward.Z), yaw));
    }

    [Fact]
    public void Body_yaw_interpolation_takes_short_arc_across_wrap()
    {
        Assert.Equal(360, EntityLodDirections.InterpolateYaw(359, 1, 0.5f));
        Assert.Equal(0, EntityLodDirections.InterpolateYaw(1, 359, 0.5f));
        Assert.Equal(-720, EntityLodDirections.InterpolateYaw(-720, 0, 0.5f));
    }

    [Fact]
    public void Cow_provider_bounds_cover_visual_model_not_only_collision_height_and_reject_head_pose()
    {
        var cow = EntityRenderBaseline.CreateEntities(new FakeWorldContext(), "cow", 1, 16, 0, 220, 0)[0];
        BasicEntityImpostorProvider provider = new(new ClientEntityImpostorDescriptor(
            new("test", "cow"), new(Namespace.OmniBlock, "basic"), 2.75,
            [new("cow", "/mob/cow.png")]));
        Assert.True(provider.VisualDiameter > cow.Height);
        Assert.True(provider.Supports(cow, 0));
        cow.PrevPitch = cow.Pitch = 20;
        Assert.False(provider.Supports(cow, 0));
        cow.PrevPitch = cow.Pitch = 0;
        cow.Passenger = cow;
        Assert.False(provider.Supports(cow, 0));
    }
}

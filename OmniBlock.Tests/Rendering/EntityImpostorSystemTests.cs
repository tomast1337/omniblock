using System.Numerics;
using System.Runtime.InteropServices;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;

namespace OmniBlock.Tests.Rendering;

public class EntityImpostorSystemTests
{
    private sealed class FakeProvider(int id) : IEntityImpostorProvider
    {
        public ResourceLocation Id { get; } = new("test", "provider_" + id);
        public double VisualDiameter => 1;
        public string VariantKey => Id.ToString();
        public string TexturePath => "/mob/cow.png";
        public string CacheIdentity => Id.ToString();
        public bool Supports(Entity entity, float partialTicks) => true;
        public int Pose(Entity entity, float partialTicks) => 0;
        public EntityImpostorVertex[][] BuildPoses() => [CowImpostorGeometry.Build()];
    }

    [Fact]
    public void System_bounds_provider_atlas_residency_and_reuses_provider_ids()
    {
        using EntityImpostorSystem system = new() { Enabled = true };
        var decision = new EntityLodSelector.Decision(EntityLodTier.Impostor,
            EntityLodReason.ImpostorCandidate, 0, 10);
        for (var i = 0; i < 12; i++)
            Assert.False(system.TrySubmit(new FakeProvider(i), decision, Vector3.UnitZ, 0, 1, 0, false));
        Assert.Equal(8, system.ResidentAtlasCount);
        Assert.False(system.TrySubmit(new FakeProvider(11), decision, Vector3.UnitZ, 0, 1, 0, false));
        Assert.Equal(8, system.ResidentAtlasCount);
    }

    [Fact]
    public void Every_view_has_an_orthonormal_right_handed_basis_including_both_poles()
    {
        for (var i = 0; i < 26; i++)
        {
            var direction = EntityLodDirections.Get(i);
            var (right, up) = EntityImpostorLayout.Basis(direction);
            Assert.InRange(Math.Abs(right.Length() - 1), 0, 1e-6);
            Assert.InRange(Math.Abs(up.Length() - 1), 0, 1e-6);
            Assert.InRange(Math.Abs(Vector3.Dot(right, up)), 0, 1e-6);
            Assert.InRange(Vector3.Distance(Vector3.Cross(right, up), direction), 0, 1e-6);
            var view = Matrix4x4.CreateLookAt(direction * 10, Vector3.Zero, up);
            Assert.InRange(Vector3.Distance(Vector3.TransformNormal(right, view), Vector3.UnitX), 0, 1e-6);
            Assert.InRange(Vector3.Distance(Vector3.TransformNormal(up, view), Vector3.UnitY), 0, 1e-6);
        }
    }

    [Fact]
    public void All_view_tiles_are_disjoint_padded_and_inside_the_atlas()
    {
        List<Vector4> rectangles = [];
        for (var pose = 0; pose < EntityImpostorLayout.Poses; pose++)
        for (var i = 0; i < EntityImpostorLayout.Views; i++)
        {
            var uv = EntityImpostorLayout.UV(i, pose);
            Assert.True(uv.X > 0 && uv.Y > 0 && uv.X + uv.Z < 1 && uv.Y + uv.W < 1);
            foreach (var other in rectangles)
                Assert.True(uv.X + uv.Z < other.X || other.X + other.Z < uv.X || uv.Y + uv.W < other.Y || other.Y + other.W < uv.Y);
            rectangles.Add(uv);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => EntityImpostorLayout.UV(26));
        Assert.Throws<ArgumentOutOfRangeException>(() => EntityImpostorLayout.UV(0, EntityImpostorLayout.Poses));
    }

    [Fact]
    public void Only_a_complete_successful_candidate_can_publish_and_capture_is_bounded()
    {
        for (var completed = 0; completed <= EntityImpostorLayout.Captures; completed++)
        {
            Assert.Equal(completed == EntityImpostorLayout.Captures, EntityImpostorAtlas.MayPublish(completed, false));
            Assert.False(EntityImpostorAtlas.MayPublish(completed, true));
            Assert.InRange(EntityImpostorAtlas.ViewsThisFrame(completed), 0, 2);
        }
        Assert.Equal(1, EntityImpostorAtlas.ViewsThisFrame(EntityImpostorLayout.Captures - 1));
        Assert.Equal(0, EntityImpostorAtlas.ViewsThisFrame(EntityImpostorLayout.Captures));
    }

    [Fact]
    public void Bounded_walk_poses_are_distinct_and_selector_wraps_the_live_gait_cycle()
    {
        var poses = CowImpostorGeometry.BuildPoses();
        Assert.Equal(EntityImpostorLayout.Poses, poses.Length);
        Assert.All(poses, pose => Assert.Equal(poses[0].Length, pose.Length));
        Assert.Equal(0, CowImpostorProvider.SelectPose(0, 0, 100, .5f));
        var stride = MathF.PI / 2 / .6662f;
        Assert.Equal(1, CowImpostorProvider.SelectPose(1, 1, 0, 1));
        Assert.Equal(2, CowImpostorProvider.SelectPose(1, 1, stride, 1));
        Assert.Equal(3, CowImpostorProvider.SelectPose(1, 1, stride * 2, 1));
        Assert.Equal(4, CowImpostorProvider.SelectPose(1, 1, stride * 3, 1));
        Assert.Equal(1, CowImpostorProvider.SelectPose(1, 1, stride * 4, 1));
        Assert.NotEqual(poses[0], poses[1]);
        Assert.NotEqual(poses[1], poses[2]);
    }

    [Fact]
    public void Capture_geometry_is_deterministic_finite_and_does_not_reserve_shared_model_offsets()
    {
        var first = CowImpostorGeometry.Build();
        var second = CowImpostorGeometry.Build();
        // Other tests may reserve ordinary model slots concurrently, so test the isolated flag
        // directly rather than assuming the process-global allocator stood still.
        var isolated = new ModelPart(0, 0, false);
        isolated.AddBox(0, 0, 0, 1, 1, 1, 0);
        Assert.Equal(-1, isolated.StaticVertexOffset);
        Assert.Equal(first, second);
        Assert.Equal(32, Marshal.SizeOf<EntityImpostorVertex>());
        Assert.Equal(9 * 36, first.Length);
        foreach (var vertex in first)
        {
            Assert.True(float.IsFinite(vertex.Position.Length()));
            Assert.InRange(vertex.Position.Y, 0, 3);
            Assert.InRange(vertex.Normal.Length(), .999f, 1.001f);
        }
    }

    [Fact]
    public void Submitted_impostors_replace_exactly_one_3d_submission()
    {
        EntityLodSelector selector = new();
        selector.BeginFrame(new object(), new object(), 1, default);
        selector.Select(new object(), new LodPoint(0, 0, 100), true, true, "cow", 2, 0, Vector3.UnitZ, 70, 480);
        selector.EndFrame(1);
        Assert.Equal(1, selector.Last.ImpostorDraws);
        Assert.Equal(0, selector.Last.ModelDraws);
        Assert.Equal(1, selector.Last.Observed);
        Assert.Throws<ArgumentOutOfRangeException>(() => selector.EndFrame(2));
    }

    [Fact]
    public void Forced_test_tier_never_overrides_unsupported_state_or_invalid_projection()
    {
        EntityLodSelector selector = new();
        selector.BeginFrame(new object(), new object(), 1, default);
        EntityLodSelector.Decision Select(bool state = true, int height = 480) => selector.Select(new object(),
            new LodPoint(0, 0, 16), true, state, "cow", 2, 0, Vector3.UnitZ, 70, height, true);
        Assert.Equal(EntityLodTier.Impostor, Select().Intended);
        Assert.Equal(EntityLodReason.UnsupportedState, Select(false).Reason);
        Assert.Equal(EntityLodReason.InvalidView, Select(height: 0).Reason);
    }
}

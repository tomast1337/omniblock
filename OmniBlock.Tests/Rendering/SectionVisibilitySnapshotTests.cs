using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class SectionVisibilitySnapshotTests
{
    [Fact]
    public void Worker_result_keeps_immutable_section_identity()
    {
        using var first = Resident(new Vector3D<int>(0, 64, 0));
        using var second = Resident(new Vector3D<int>(64, 64, 0));
        var snapshot = SectionVisibilitySnapshot.Capture([first, second], 7, 11);
        var view = View(0, occlusion: false);

        // Replacing the live presentation after capture must not alter what the worker owns.
        var capturedEpoch = second.Renderer!.PresentedEpoch;
        second.Renderer.InstallPresentation(SectionPresentation.MetadataOnly(capturedEpoch + 1));
        var result = snapshot.Build(view, first.Position, AllVisibleFrustum(),
            new Vector3D<double>(8, 72, 8), CancellationToken.None);

        Assert.Equal(7, result.WorldGeneration);
        Assert.Equal(11, result.GraphEpoch);
        Assert.Contains(result.ConservativeCandidates,
            candidate => candidate.Identity.Position == second.Position &&
                         candidate.Identity.PresentationEpoch == capturedEpoch);
    }

    [Fact]
    public void Expanded_worker_frustum_is_a_conservative_superset()
    {
        using var near = Resident(new Vector3D<int>(0, 64, 0));
        using var edge = Resident(new Vector3D<int>(64, 64, 0));
        var snapshot = SectionVisibilitySnapshot.Capture([near, edge], 1, 1);
        var planes = AllVisiblePlanes();
        // x < 40. The live edge bounds begin at x=58 and are outside, while the snapshot's
        // two-section expansion reaches x=26 and must retain it for exact render-thread filtering.
        planes[0] = -1;
        planes[1] = 0;
        planes[2] = 0;
        planes[3] = 40;
        var frustum = new ImmutableFrustum(planes, 0, 0, 0);

        var result = snapshot.Build(View(0, occlusion: false), near.Position, frustum,
            new Vector3D<double>(8, 72, 8), CancellationToken.None);

        Assert.Contains(result.ConservativeCandidates,
            candidate => candidate.Identity.Position == edge.Position);
    }

    [Fact]
    public void Conservative_result_excludes_sections_beyond_its_distance_envelope()
    {
        using var near = Resident(new Vector3D<int>(0, 64, 0));
        using var far = Resident(new Vector3D<int>(128, 64, 0));
        var snapshot = SectionVisibilitySnapshot.Capture([near, far], 1, 1);

        var result = snapshot.Build(View(0, occlusion: false, renderDistance: 4), near.Position,
            AllVisibleFrustum(), new Vector3D<double>(8, 72, 8), CancellationToken.None);

        Assert.Contains(result.ConservativeCandidates,
            candidate => candidate.Identity.Position == near.Position);
        Assert.DoesNotContain(result.ConservativeCandidates,
            candidate => candidate.Identity.Position == far.Position);
    }

    [Fact]
    public void Cancelled_build_never_publishes_a_partial_result()
    {
        using var section = Resident(new Vector3D<int>(0, 64, 0));
        var snapshot = SectionVisibilitySnapshot.Capture([section], 1, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => snapshot.Build(
            View(0, occlusion: false), section.Position, AllVisibleFrustum(),
            new Vector3D<double>(8, 72, 8), cancellation.Token));
    }

    [Theory]
    [InlineData(170, false)]
    [InlineData(170, true)]
    [InlineData(192, false)]
    [InlineData(192, true)]
    [InlineData(512, false)]
    [InlineData(512, true)]
    public void Worker_retains_terrain_below_high_camera_like_the_live_culler(int height, bool occlusion)
    {
        using var below = Resident(new Vector3D<int>(0, 64, 0));
        using var distant = Resident(new Vector3D<int>(160, 64, 0));
        var position = new Vector3D<double>(8, height, 8);
        var view = VisibilityViewKey.Create(new Vector3D<int>(0, height / 16 * 16, 0),
            position, 0, 90, 0, Matrix4X4<float>.Identity, 4, occlusion);
        var snapshot = SectionVisibilitySnapshot.Capture([below, distant], 1, 1);

        Assert.True(below.Renderer!.IsWithinRenderDistance(position, 4 * 16));
        var result = snapshot.Build(view, below.Position, AllVisibleFrustum(), position, CancellationToken.None);

        Assert.Contains(result.ConservativeCandidates, candidate => candidate.Identity.Position == below.Position);
        Assert.DoesNotContain(result.ConservativeCandidates, candidate => candidate.Identity.Position == distant.Position);
    }

    private static VisibilityViewKey View(int sectionX, bool occlusion, int renderDistance = 32) => VisibilityViewKey.Create(
        new Vector3D<int>(sectionX, 64, 0),
        new Vector3D<double>(sectionX + 8, 72, 8),
        0,
        0,
        0,
        Matrix4X4<float>.Identity,
        renderDistance,
        occlusion);

    private static ImmutableFrustum AllVisibleFrustum() =>
        new(AllVisiblePlanes(), 0, 0, 0);

    private static float[] AllVisiblePlanes()
    {
        var planes = new float[24];
        for (var plane = 0; plane < 6; plane++) planes[plane * 4 + 3] = 1_000_000;
        return planes;
    }

    private static SectionRenderState Resident(Vector3D<int> position)
    {
        var state = new SectionRenderState(position);
        state.Version.MarkDirty();
        var epoch = state.Version.SnapshotIfNeeded()!.Value;
        state.CommitPresentation(
            new SubChunkRenderer(position),
            SectionPresentation.MetadataOnly(epoch));
        return state;
    }
}

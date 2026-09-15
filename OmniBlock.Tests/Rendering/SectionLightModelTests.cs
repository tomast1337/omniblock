using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class SectionLightModelTests
{
    [Fact]
    public void Bounded_evaluator_runs_light_probes_against_an_immutable_snapshot()
    {
        var world = new FakeWorldContext();
        var vertices = TopQuad(0, 0);
        var model = SectionLightModel.Create(default, vertices.Geometry, vertices.Lights);
        Assert.NotNull(model);
        var plan = new SectionPresentationLightPlan(
            7,
            [new SectionLightingPlan(model, null, 3)]);
        var snapshot = new WorldRegionSnapshot(
            world, -2, -2, -2,
            SubChunkRenderer.Size + 1,
            SubChunkRenderer.Size + 1,
            SubChunkRenderer.Size + 1);
        using var evaluator = new SectionLightEvaluationService(1);

        Assert.True(evaluator.TrySubmit(new SectionLightEvaluationRequest(
            default, 11, 13, plan, snapshot)));
        Assert.False(evaluator.HasCapacity);
        SectionLightEvaluationResult? result = null;
        Assert.True(SpinWait.SpinUntil(
            () => evaluator.TryTakeCompleted(out result), TimeSpan.FromSeconds(5)));

        Assert.True(evaluator.HasCapacity);
        Assert.NotNull(result);
        Assert.Null(result.Failure);
        Assert.Equal(11, result.SectionId);
        Assert.Equal(13, result.Generation);
        Assert.Equal(7, result.Evaluation.PresentationEpoch);
        var page = Assert.Single(result.Evaluation.Pages);
        Assert.NotNull(page);
        Assert.Equal(3, page.Value.SourceEpoch);
        Assert.Equal(4, page.Value.SolidValues?.Length);
        Assert.Null(page.Value.TranslucentValues);
    }

    [Fact]
    public void Initial_stream_preserves_the_exact_compiled_vertex_light()
    {
        var vertices = TopQuad(17, 31);
        var model = SectionLightModel.Create(default, vertices.Geometry, vertices.Lights);

        Assert.NotNull(model);
        Assert.All(model.InitialValues, value =>
        {
            Assert.Equal(17, value.Sky);
            Assert.Equal(31, value.Block);
        });
    }

    [Fact]
    public void Relighting_an_axis_face_samples_the_four_outward_corner_cells()
    {
        var vertices = TopQuad(0, 0);
        var model = SectionLightModel.Create(new Vector3D<int>(16, 32, 48), vertices.Geometry, vertices.Lights);

        var values = model!.Evaluate(new CoordinateLightProvider());

        // First corner is local (1,1,1). A positive-Y face samples y=33 and the four cells
        // x=16/17, z=48/49. Their coordinate gradients average to sky=1 and block=3.
        Assert.Equal(ChunkVertexHelper.ToQuarterLevels(1), values[0].Sky);
        Assert.Equal(ChunkVertexHelper.ToQuarterLevels(3), values[0].Block);
    }

    [Fact]
    public void Full_bright_geometry_remains_full_bright_during_relighting()
    {
        var vertices = TopQuad(0, 60, fullBright: true);
        var model = SectionLightModel.Create(default, vertices.Geometry, vertices.Lights);

        var values = model!.Evaluate(new CoordinateLightProvider());

        Assert.All(values, value =>
        {
            Assert.Equal(0, value.Sky);
            Assert.Equal(60, value.Block);
        });
    }

    private static (ChunkVertex[] Geometry, ChunkLightVertex[] Lights) TopQuad(
        byte sky, byte block, bool fullBright = false) =>
    (
        [Vertex(1, 1, 1), Vertex(1, 1, 0), Vertex(0, 1, 0), Vertex(0, 1, 1)],
        [new(sky, block, fullBright ? (byte)1 : (byte)0), new(sky, block, fullBright ? (byte)1 : (byte)0),
            new(sky, block, fullBright ? (byte)1 : (byte)0), new(sky, block, fullBright ? (byte)1 : (byte)0)]
    );

    private static ChunkVertex Vertex(float x, float y, float z) =>
        ChunkVertexHelper.Create(unchecked((int)0xFFFFFFFF), x, y, z, 0, 0, 0);

    private sealed class CoordinateLightProvider : ILightProvider
    {
        public float GetNaturalBrightness(int x, int y, int z, int minLight) => 0;
        public float GetLuminance(int x, int y, int z) => 0;

        public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) =>
            LightLevels.Of(Math.Abs(x - 16) + Math.Abs(z - 48), Math.Abs(y - 30));
    }
}

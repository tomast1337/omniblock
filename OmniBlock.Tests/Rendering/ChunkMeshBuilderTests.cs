using System.Runtime.InteropServices;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Core;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMeshBuilderTests
{
    [Fact]
    public void Output_matches_the_packed_chunk_vertex_contract()
    {
        using var builder = new ChunkMeshBuilder();
        builder.Begin(-16.0, 3.5, 32.0);
        EmitCharacterizationGeometry(builder);
        using var vertices = builder.Finish(out var lights);
        using (lights)
        {
            Assert.Equal(20, Marshal.SizeOf<ChunkVertex>());
            Assert.Equal(4, Marshal.SizeOf<ChunkLightVertex>());
            Assert.Equal(8, vertices.Count);

            var firstColor = BitConverter.IsLittleEndian ? unchecked((int)0xFFBF7F3F) : 0x3F7FBFFF;
            var secondColor = BitConverter.IsLittleEndian ? unchecked((int)0xFF001FFF) : unchecked((int)0xFF1F00FF);
            var thirdColor = BitConverter.IsLittleEndian ? unchecked((int)0xFF54FF00) : 0x00FF54FF;

            var expected = new[]
            {
                V(0, 2816, 0, 0, 0, firstColor, 49, 22, 3), V(0, 3328, 0, 0, 4095, firstColor, 49, 22, 3), V(512, 3328, 0, 4095, 4095, secondColor, 60, 0, 3), V(512, 2816, 0, 4095, 0, secondColor, 60, 0, 3),
                V(2176, 3584, 1024, 0, 0, thirdColor, 0, 60, 251), V(2176, 4096, 1024, 0, 65520, thirdColor, 0, 60, 251), V(2688, 4096, 1024, 65520, 65520, thirdColor, 0, 60, 251), V(2688, 3584, 1024, 65520, 0, thirdColor, 0, 60, 251)
            };

            for (var i = 0; i < expected.Length; i++)
            {
                AssertVertex(expected[i].Vertex, vertices.Buffer[i]);
                Assert.Equal(expected[i].SkyLight, lights.Buffer[i].Sky);
                Assert.Equal(expected[i].BlockLight, lights.Buffer[i].Block);
            }
        }
    }

    [Fact]
    public void Quad_stores_each_corner_once_in_emission_order()
    {
        using var builder = new ChunkMeshBuilder();
        builder.Begin(0, 0, 0);
        builder.setArrayLayer(7);
        builder.setColorOpaque_F(1, 1, 1);
        builder.setLight(15, 4);
        builder.addVertexWithUV(0, 0, 0, 0, 0);
        builder.addVertexWithUV(0, 1, 0, 0, 1);
        builder.addVertexWithUV(1, 1, 0, 1, 1);
        builder.addVertexWithUV(1, 0, 0, 1, 0);
        using var vertices = builder.Finish();

        Assert.Equal(4, vertices.Count);
        AssertVertexPosition(vertices.Buffer[0], 0, 0, 0);
        AssertVertexPosition(vertices.Buffer[1], 0, 1, 0);
        AssertVertexPosition(vertices.Buffer[2], 1, 1, 0);
        AssertVertexPosition(vertices.Buffer[3], 1, 0, 0);
    }

    [Fact]
    public void Finish_packs_directional_quads_and_keeps_unknown_geometry_last()
    {
        using var builder = new ChunkMeshBuilder();
        builder.Begin(0, 0, 0);

        builder.setQuadDirection(Side.East);
        EmitQuad(builder, 10);
        EmitQuad(builder, 20);
        builder.setQuadDirection(Side.Down);
        EmitQuad(builder, 30);

        using var vertices = builder.Finish(out var lights, out var ranges);
        using (lights)
        {
            Assert.Equal(new ChunkQuadRange(0, 1), ranges.Down);
            Assert.Equal(new ChunkQuadRange(1, 1), ranges.East);
            Assert.Equal(new ChunkQuadRange(2, 1), ranges.Unassigned);
            Assert.Equal(3, ranges.AvailableQuadCount);
            AssertVertexPosition(vertices.Buffer[0], 30, 0, 0);
            AssertVertexPosition(vertices.Buffer[4], 10, 0, 0);
            AssertVertexPosition(vertices.Buffer[8], 20, 0, 0);
        }
    }

    [Fact]
    public void Incomplete_quad_is_discarded_like_legacy_capture()
    {
        using var builder = new ChunkMeshBuilder();
        builder.Begin(0, 0, 0);
        builder.addVertexWithUV(0, 0, 0, 0, 0);
        builder.addVertexWithUV(1, 0, 0, 1, 0);
        builder.addVertexWithUV(1, 1, 0, 1, 1);
        using var vertices = builder.Finish();

        Assert.Empty(vertices.Span.ToArray());
    }

    [Fact]
    public void Full_bright_intent_is_kept_out_of_the_geometry_stream()
    {
        using var builder = new ChunkMeshBuilder();
        builder.Begin(0, 0, 0);
        builder.setFullBright();
        builder.addVertexWithUV(0, 0, 0, 0, 0);
        builder.addVertexWithUV(0, 1, 0, 0, 1);
        builder.addVertexWithUV(1, 1, 0, 1, 1);
        builder.addVertexWithUV(1, 0, 0, 1, 0);
        using var vertices = builder.Finish(out var lights);
        using (lights)
        {
            Assert.All(lights.Span.ToArray(), light => Assert.Equal(1, light.Pad0));
            Assert.All(vertices.Span.ToArray(), vertex =>
            {
                Assert.Equal(0, vertex.UvScaleExponent);
                Assert.Equal(0, vertex.PageOffsetY);
                Assert.Equal(0, vertex.Reserved);
            });
        }
    }

    private static void EmitCharacterizationGeometry(IBlockVertexSink sink)
    {
        sink.setArrayLayer(3);
        sink.setColorOpaque_F(0.25f, 0.5f, 0.75f);
        sink.setLight(12.25f, 5.5f);
        sink.addVertexWithUV(16, 2, -32, 0, 0);
        sink.addVertexWithUV(16, 3, -32, 0, 1);
        sink.setColorOpaque_F(1, 0.125f, 0);
        sink.setLight(15, 0);
        sink.addVertexWithUV(17, 3, -32, 1, 1);
        sink.addVertexWithUV(17, 2, -32, 1, 0);

        sink.setTranslationF(0.25f, -0.5f, 1.0f);
        sink.setArrayLayer(251);
        sink.setColorOpaque_F(-1, 2, 0.333f);
        sink.setLight(-3, 20);
        sink.addVertexWithUV(20, 4, -31, 0, 0);
        sink.addVertexWithUV(20, 5, -31, 0, 16);
        sink.addVertexWithUV(21, 5, -31, 16, 16);
        sink.addVertexWithUV(21, 4, -31, 16, 0);
    }

    private static void EmitQuad(IBlockVertexSink sink, float x)
    {
        sink.addVertexWithUV(x, 0, 0, 0, 0);
        sink.addVertexWithUV(x, 1, 0, 0, 1);
        sink.addVertexWithUV(x + 1, 1, 0, 1, 1);
        sink.addVertexWithUV(x + 1, 0, 0, 1, 0);
    }

    private static ExpectedVertex V(
        short x, short y, short z,
        ushort u, ushort v,
        int color, byte skyLight, byte blockLight, byte arrayLayer) => new(
        new ChunkVertex
        {
            X = x,
            Y = y,
            Z = z,
            U = u,
            V = v,
            Color = color,
            ArrayLayer = arrayLayer
        },
        skyLight,
        blockLight);

    private readonly record struct ExpectedVertex(
        ChunkVertex Vertex,
        byte SkyLight,
        byte BlockLight);

    private static void AssertVertex(ChunkVertex expected, ChunkVertex actual)
    {
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
        Assert.Equal(expected.Z, actual.Z);
        Assert.Equal(expected.PageOffsetXZ, actual.PageOffsetXZ);
        Assert.Equal(expected.Color, actual.Color);
        Assert.Equal(expected.U, actual.U);
        Assert.Equal(expected.V, actual.V);
        Assert.Equal(expected.ArrayLayer, actual.ArrayLayer);
        Assert.Equal(0, actual.UvScaleExponent);
        Assert.Equal(0, actual.PageOffsetY);
        Assert.Equal(0, actual.Reserved);
    }

    private static void AssertVertexPosition(ChunkVertex actual, float x, float y, float z)
    {
        Assert.Equal(ChunkVertexHelper.FloatToShortPosition(x), actual.X);
        Assert.Equal(ChunkVertexHelper.FloatToShortPosition(y), actual.Y);
        Assert.Equal(ChunkVertexHelper.FloatToShortPosition(z), actual.Z);
    }
}

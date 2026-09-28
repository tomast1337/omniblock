using System.Numerics;
using System.Runtime.InteropServices;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Biomes.Source;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Rendering;

/// <summary>
/// Locks down emitted geometry, not renderer implementation or GPU buffer layout. These assertions
/// should survive replacing procedural cube/slab construction with compiled models.
/// </summary>
public sealed class BlockGeometryCharacterizationTests
{
    private static readonly BlockPos Origin = new(7, 64, 9);

    [Theory]
    [MemberData(nameof(BoundStates))]
    public void Session_bound_models_preserve_builtin_emission(string name, int meta)
    {
        var fixture = new Fixture(name, meta);
        var models = BlockModelBindingTests.Build(fixture.World.Content.Blocks);
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        for (var z = -1; z <= 1; z++)
            if ((x * 3 + y * 5 + z * 7) % 4 == 1)
                fixture.World.Writer.SetBlock(Origin.X + x, Origin.Y + y, Origin.Z + z, fixture.World.Content.Blocks.Get("stone").Id);
        foreach (var variance in new[] { false, true })
        foreach (var allFaces in new[] { false, true })
        foreach (var overrideTexture in new[] { -1, Atlases.Terrain.IndexOf("cobblestone") })
            Assert.Equal(fixture.Render(allFaces, new NonuniformLight(), compiled: false, variance: variance, texture: overrideTexture),
                fixture.Render(allFaces, new NonuniformLight(), variance: variance, texture: overrideTexture, models: models));
    }

    public static IEnumerable<object[]> BoundStates()
    {
        foreach (var name in new[] { "stone", "slab", "double_slab" })
        for (var meta = 0; meta < 16; meta++) yield return [name, meta];
    }

    [Fact]
    public void Session_pack_material_changes_reach_the_vertex_sink_without_changing_other_sessions()
    {
        var fixture = new Fixture("stone");
        var original = BlockModelBindingTests.Build(fixture.World.Content.Blocks);
        var changed = BlockModelBindingTests.Build(fixture.World.Content.Blocks,
            json => json.Replace("omniblock:stone\"", "omniblock:cobblestone\"", StringComparison.Ordinal));
        Assert.All(fixture.Render(models: changed), v => Assert.Equal(Layer("cobblestone"), v.Layer));
        Assert.All(fixture.Render(models: original), v => Assert.Equal(Layer("stone"), v.Layer));
        Assert.All(fixture.Render(models: changed, texture: Atlases.Terrain.IndexOf("stone")),
            v => Assert.Equal(Layer("stone"), v.Layer));
    }

    [Fact]
    public void Compiled_templates_do_not_claim_partial_or_out_of_cell_shapes()
    {
        Assert.Null(CompiledCuboidGeometry.ForBounds(new Box(0, 0, 0, 1, .125, 1)));
        Assert.Null(CompiledCuboidGeometry.ForBounds(new Box(.0625, 0, .0625, .9375, 1, .9375)));
        Assert.Null(CompiledCuboidGeometry.ForBounds(new Box(0, 0, 0, 2, 1, 1)));
        Assert.Null(CompiledCuboidGeometry.ForBounds(new Box(0, -.5, 0, 1, 1, 1)));
    }

    [Theory]
    [InlineData("stone", 0)]
    [InlineData("slab", 0)]
    [InlineData("slab", 8)]
    public void Compiled_face_emitter_matches_all_rotations_flips_and_flat_color_state(string name, int meta)
    {
        var fixture = new Fixture(name, meta);
        fixture.Block.UpdateBoundingBox(fixture.World.Reader, Origin.X, Origin.Y, Origin.Z);
        var bounds = fixture.Block.BoundingBox;
        Assert.NotNull(CompiledCuboidGeometry.ForBounds(bounds));
        foreach (var ao in new[] { false, true })
        foreach (var diagonal in new[] { false, true })
        foreach (var flipTexture in new[] { false, true })
        for (var rotation = 0; rotation <= 4; rotation++)
        for (var mask = 0; mask < 4; mask++)
        foreach (var position in new[] { new Vec3D(7, 64, 9), new Vec3D(-16777217, 89, 16777217) })
        {
            Assert.Equal(Emit(false), Emit(true));
            List<Vertex> Emit(bool compiled)
            {
                var sink = new RecordingSink();
                sink.setColorOpaque_F(.1f, .2f, .3f);
                sink.setLight(6, 7);
                var ctx = new BlockRenderContext(fixture.World.Reader, fixture.World.Content.Blocks,
                    sink, new NonuniformLight(), bounds: bounds, enableAo: ao, flipTexture: flipTexture,
                    uvTop: rotation, uvBottom: rotation, uvNorth: rotation, uvSouth: rotation,
                    uvEast: rotation, uvWest: rotation, flipTop: mask, flipBottom: mask,
                    flipNorth: mask, flipSouth: mask, flipEast: mask, flipWest: mask);
                if (compiled) ctx.CompiledCuboid = CompiledCuboidGeometry.ForBounds(bounds);
                var colors = new FaceColors(.1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, .2f, .4f, .8f,
                    new CornerLight(1, 2), new CornerLight(3, 4), new CornerLight(5, 6), new CornerLight(7, 8));
                var block = fixture.Block;
                ctx.DrawBottomFace(block, position, colors, block.TextureId, diagonal);
                ctx.DrawTopFace(block, position, colors, block.TextureId, diagonal);
                ctx.DrawNorthFace(block, position, colors, block.TextureId, diagonal);
                ctx.DrawSouthFace(block, position, colors, block.TextureId, diagonal);
                ctx.DrawEastFace(block, position, colors, block.TextureId, diagonal);
                ctx.DrawWestFace(block, position, colors, block.TextureId, diagonal);
                return sink.Vertices;
            }
        }
    }

    [Theory]
    [InlineData("stone", 0)]
    [InlineData("grass_block", 0)]
    [InlineData("glass", 0)]
    [InlineData("leaves", 0)]
    [InlineData("slab", 0)]
    [InlineData("slab", 1)]
    [InlineData("slab", 2)]
    [InlineData("slab", 3)]
    [InlineData("slab", 8)]
    [InlineData("slab", 9)]
    [InlineData("slab", 10)]
    [InlineData("slab", 11)]
    [InlineData("double_slab", 0)]
    [InlineData("double_slab", 1)]
    [InlineData("double_slab", 2)]
    [InlineData("double_slab", 3)]
    public void Compiled_faces_match_legacy_corner_order_materials_and_nonuniform_light(string name, int meta)
    {
        var fixture = new Fixture(name, meta);
        // Mixed edge/corner occluders exercise both AO diagonal choices and conservative culling.
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        for (var z = -1; z <= 1; z++)
            if ((x * 3 + y * 5 + z * 7) % 4 == 1)
                fixture.World.Writer.SetBlock(Origin.X + x, Origin.Y + y, Origin.Z + z, fixture.World.Content.Blocks.Get("stone").Id);

        foreach (var all in new[] { false, true })
        foreach (var variance in new[] { false, true })
        foreach (var texture in new[] { -1, Atlases.Terrain.IndexOf("stone") })
        {
            var legacy = fixture.Render(all, new NonuniformLight(), compiled: false, variance, texture);
            var compiled = fixture.Render(all, new NonuniformLight(), compiled: true, variance, texture);
            Assert.Equal(legacy, compiled);
        }
    }

    [Theory]
    [InlineData("stone", 0)]
    [InlineData("grass_block", 0)]
    [InlineData("slab", 0)]
    [InlineData("slab", 8)]
    public void Compiled_faces_preserve_packed_geometry_light_and_direction_ranges(string name, int meta)
    {
        var fixture = new Fixture(name, meta);
        (byte[] Geometry, byte[] Light, ChunkDirectionalRanges Ranges) Packed(bool compiled)
        {
            using var builder = new ChunkMeshBuilder();
            builder.Begin(-Origin.X, -Origin.Y, -Origin.Z);
            BlockRenderer.RenderBlockByRenderType(fixture.World.Reader, fixture.World.Content.Blocks,
                new NonuniformLight(), fixture.Block, Origin, builder, doVariance: true, useCompiledCuboids: compiled);
            using var vertices = builder.Finish(out var lights, out var ranges);
            using (lights)
                return (MemoryMarshal.AsBytes(vertices.Span).ToArray(), MemoryMarshal.AsBytes(lights.Span).ToArray(), ranges);
        }
        var old = Packed(false);
        var candidate = Packed(true);
        Assert.Equal(old.Geometry, candidate.Geometry);
        Assert.Equal(old.Light, candidate.Light);
        Assert.Equal(old.Ranges, candidate.Ranges);
    }

    [Theory]
    [InlineData("stone", 0, 0f, 1f)]
    [InlineData("slab", 0, 0f, .5f)]
    [InlineData("slab", 1, 0f, .5f)]
    [InlineData("slab", 2, 0f, .5f)]
    [InlineData("slab", 3, 0f, .5f)]
    [InlineData("slab", 8, .5f, 1f)]
    [InlineData("slab", 9, .5f, 1f)]
    [InlineData("slab", 10, .5f, 1f)]
    [InlineData("slab", 11, .5f, 1f)]
    [InlineData("double_slab", 0, 0f, 1f)]
    [InlineData("double_slab", 1, 0f, 1f)]
    [InlineData("double_slab", 2, 0f, 1f)]
    [InlineData("double_slab", 3, 0f, 1f)]
    public void Isolated_cuboids_have_six_outward_faces_with_cropped_not_stretched_uvs(
        string name, int meta, float minY, float maxY)
    {
        var fixture = new Fixture(name, meta);
        var vertices = fixture.Render();
        Assert.Equal(24, vertices.Count);
        foreach (var side in Enum.GetValues<Side>())
        {
            var face = vertices.Where(v => v.Side == side).ToArray();
            Assert.Equal(4, face.Length);
            var normal = Direction(side);
            var cross = Vector3.Cross(face[1].Position - face[0].Position,
                face[2].Position - face[0].Position);
            Assert.True(Vector3.Dot(cross, normal) > 0, $"{name}:{meta} {side} winding");
            var expectedArea = side is Side.Up or Side.Down ? 1f : maxY - minY;
            Assert.Equal(expectedArea, cross.Length(), 6);
            Assert.Equal(4, face.Select(v => v.Position).Distinct().Count());

            foreach (var vertex in face)
            {
                var p = vertex.Position - new Vector3(Origin.X, Origin.Y, Origin.Z);
                Assert.Contains(p.X, new[] { 0f, 1f });
                Assert.Contains(p.Y, new[] { minY, maxY });
                Assert.Contains(p.Z, new[] { 0f, 1f });
                Assert.Equal(side switch
                {
                    Side.Down => -minY, Side.Up => maxY,
                    Side.North or Side.West => 0f, _ => 1f
                }, Vector3.Dot(p, normal));
                var expectedUv = side switch
                {
                    Side.Down or Side.Up => new Vector2(p.X, p.Z),
                    Side.North => new Vector2(1 - p.X, 1 - p.Y),
                    Side.South => new Vector2(p.X, 1 - p.Y),
                    Side.West => new Vector2(p.Z, 1 - p.Y),
                    _ => new Vector2(1 - p.Z, 1 - p.Y)
                };
                Assert.Equal(expectedUv, vertex.Uv);
                Assert.Equal(Shade(side), vertex.Color.X, 6);
                Assert.Equal(Shade(side), vertex.Color.Y, 6);
                Assert.Equal(Shade(side), vertex.Color.Z, 6);
                Assert.Equal(new Vector2(15, 3), vertex.Light);
            }
        }
    }

    [Theory]
    [InlineData(0, "stone_slab_top", "stone_slab_side", "stone_slab_top")]
    [InlineData(1, "sandstone_top", "sandstone_side", "sandstone_bottom")]
    [InlineData(2, "wooden_planks", "wooden_planks", "wooden_planks")]
    [InlineData(3, "cobblestone", "cobblestone", "cobblestone")]
    public void Slab_variants_resolve_each_face_material(int meta, string top, string side, string bottom)
    {
        foreach (var name in new[] { "slab", "double_slab" })
            Assert.All(new Fixture(name, meta).Render(), vertex =>
                Assert.Equal(Layer(vertex.Side == Side.Up ? top : vertex.Side == Side.Down ? bottom : side), vertex.Layer));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void Legacy_upper_slab_metadata_currently_falls_back_to_stone_side_texture(int meta)
    {
        // Known behavior, not a desired model rule: texture lookup does not mask the upper bit.
        Assert.All(new Fixture("slab", meta).Render(), v => Assert.Equal(Layer("stone_slab_side"), v.Layer));
    }

    [Theory]
    [InlineData(Side.Down)]
    [InlineData(Side.Up)]
    [InlineData(Side.North)]
    [InlineData(Side.South)]
    [InlineData(Side.West)]
    [InlineData(Side.East)]
    public void Solid_neighbor_culls_only_the_touching_cube_face_and_force_faces_overrides_it(Side side)
    {
        var fixture = new Fixture("stone");
        fixture.Neighbor(side, "stone");
        var vertices = fixture.Render();
        Assert.Equal(20, vertices.Count);
        Assert.DoesNotContain(vertices, v => v.Side == side);
        Assert.Equal(24, fixture.Render(allFaces: true).Count);
    }

    [Theory]
    [InlineData("slab", 0, Side.Up)]
    [InlineData("slab", 8, Side.Down)]
    [InlineData("slab", 8, Side.Up)]
    [InlineData("double_slab", 0, Side.Up)]
    public void Slab_inset_faces_and_legacy_always_visible_top_survive_a_solid_neighbor(string name, int meta, Side side)
    {
        var fixture = new Fixture(name, meta);
        fixture.Neighbor(side, "stone");
        Assert.Equal(4, fixture.Render().Count(v => v.Side == side));
    }

    [Fact]
    public void Adjacent_lower_slabs_hide_their_shared_side_but_do_not_hide_a_cube_face()
    {
        var slab = new Fixture("slab");
        slab.Neighbor(Side.East, "slab");
        Assert.Equal(20, slab.Render().Count);
        Assert.DoesNotContain(slab.Render(), v => v.Side == Side.East);
        var cube = new Fixture("stone");
        cube.Neighbor(Side.East, "slab");
        Assert.Equal(24, cube.Render().Count);
    }

    [Fact]
    public void Enclosed_cube_emits_nothing()
    {
        var fixture = new Fixture("stone");
        foreach (var side in Enum.GetValues<Side>()) fixture.Neighbor(side, "stone");
        Assert.Empty(fixture.Render());
    }

    [Fact]
    public void Grass_has_untinted_dirt_and_side_bases_with_biome_tinted_top_and_overlays()
    {
        var fixture = new Fixture("grass_block");
        var color = fixture.Block.GetColorMultiplier(fixture.World.Reader, Origin.X, Origin.Y, Origin.Z);
        var tint = new Vector3((color >> 16) & 255, (color >> 8) & 255, color & 255) * 0.0039215686f;
        Assert.NotEqual(Vector3.One, tint);
        var vertices = fixture.Render();
        Assert.Equal(40, vertices.Count);
        foreach (var side in Enum.GetValues<Side>())
        {
            var face = vertices.Where(v => v.Side == side).ToArray();
            Assert.Equal(side is Side.Up or Side.Down ? 4 : 8, face.Length);
            var baseLayer = Layer(side == Side.Up ? "grass_block_top" : side == Side.Down ? "dirt" : "grass_block_side");
            Assert.All(face.Take(4), v =>
            {
                Assert.Equal(baseLayer, v.Layer);
                Assert.Equal((side == Side.Up ? tint : Vector3.One) * Shade(side), v.Color);
            });
            Assert.All(face.Skip(4), v =>
            {
                Assert.Equal(Layer("grass_block_side_overlay"), v.Layer);
                Assert.Equal(tint * Shade(side), v.Color);
            });
            if (face.Length == 8)
                Assert.Equal(face.Take(4).Select(v => (v.Position, v.Uv)), face.Skip(4).Select(v => (v.Position, v.Uv)));
        }
    }

    [Fact]
    public void Light_changes_do_not_change_positions_uvs_materials_or_tints()
    {
        var fixture = new Fixture("stone");
        var lit = fixture.Render();
        var dark = fixture.Render(light: new ConstantLight(2, 7));
        Assert.Equal(lit.Select(v => v with { Light = default }), dark.Select(v => v with { Light = default }));
        Assert.All(dark, v => Assert.Equal(new Vector2(2, 7), v.Light));
        Assert.All(lit, v => Assert.Equal(Layer("stone"), v.Layer));
    }

    [Theory]
    [InlineData("snow")]
    [InlineData("snow_block")]
    public void Snow_above_grass_selects_snowy_sides_without_a_green_overlay(string snow)
    {
        var fixture = new Fixture("grass_block");
        fixture.Neighbor(Side.Up, snow);
        var sides = fixture.Render().Where(v => v.Side is not (Side.Up or Side.Down)).ToArray();
        Assert.Equal(16, sides.Length);
        Assert.All(sides, v =>
        {
            Assert.Equal(Layer("grass_block_side_snowy"), v.Layer);
            Assert.Equal(new Vector3(Shade(v.Side)), v.Color);
        });
    }

    [Fact]
    public void Override_texture_replaces_all_grass_materials_and_suppresses_overlays()
    {
        var fixture = new Fixture("grass_block");
        var sink = new RecordingSink();
        Assert.True(BlockRenderer.RenderBlockByRenderType(fixture.World.Reader, fixture.World.Content.Blocks,
            new ConstantLight(15, 3), fixture.Block, Origin, sink,
            overrideTexture: Atlases.Terrain.IndexOf("omniblock:stone")));
        Assert.Equal(24, sink.Vertices.Count);
        Assert.All(sink.Vertices, v => Assert.Equal(Layer("stone"), v.Layer));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Top_uv_quarter_turns_and_flips_preserve_geometry_and_other_faces(int rotation)
    {
        var fixture = new Fixture("slab");
        var baseline = fixture.Render(); // also resolves the slab's bounds
        for (var flip = 0; flip < 4; flip++)
        {
            var sink = new RecordingSink();
            var context = new BlockRenderContext(fixture.World.Reader, fixture.World.Content.Blocks,
                sink, new ConstantLight(15, 3), uvTop: rotation, flipTop: flip, aoBlendMode: 1);
            Assert.True(context.DrawBlock(fixture.Block, Origin));
            Assert.Equal(baseline.Count, sink.Vertices.Count);
            for (var i = 0; i < baseline.Count; i++)
            {
                var expected = baseline[i];
                if (expected.Side == Side.Up)
                {
                    var uv = expected.Uv;
                    uv = rotation switch
                    {
                        1 => new Vector2(uv.Y, 1 - uv.X),
                        2 => Vector2.One - uv,
                        3 => new Vector2(1 - uv.Y, uv.X),
                        _ => uv
                    };
                    if ((flip & 1) != 0) uv.X = 1 - uv.X;
                    if ((flip & 2) != 0) uv.Y = 1 - uv.Y;
                    expected = expected with { Uv = uv };
                }
                Assert.Equal(expected, sink.Vertices[i]);
            }
        }
    }

    private static int Layer(string name) => Atlases.Terrain.LayerOfGridIndex(Atlases.Terrain.IndexOf("omniblock:" + name));
    private static float Shade(Side side) => side switch { Side.Down => .5f, Side.Up => 1f, Side.North or Side.South => .8f, _ => .6f };
    private static Vector3 Direction(Side side) => side switch
    {
        Side.Down => -Vector3.UnitY, Side.Up => Vector3.UnitY,
        Side.North => -Vector3.UnitZ, Side.South => Vector3.UnitZ,
        Side.West => -Vector3.UnitX, _ => Vector3.UnitX
    };

    private sealed class Fixture
    {
        public FakeWorldContext World { get; } = new();
        public Block Block { get; }

        public Fixture(string name, int meta = 0)
        {
            World.ReaderWriter.BiomeSource = new BiomeSource(World);
            Block = World.Content.Blocks.Get(name);
            World.Writer.SetBlock(Origin.X, Origin.Y, Origin.Z, Block.Id, meta);
        }

        public void Neighbor(Side side, string name)
        {
            var d = Direction(side);
            World.Writer.SetBlock(Origin.X + (int)d.X, Origin.Y + (int)d.Y, Origin.Z + (int)d.Z, World.Content.Blocks.Get(name).Id);
        }

        public List<Vertex> Render(bool allFaces = false, ILightProvider? light = null, bool compiled = true,
            bool variance = false, int texture = -1, BlockModelBindings? models = null)
        {
            var sink = new RecordingSink();
            var rendered = BlockRenderer.RenderBoundBlock(models, World.Reader, World.Content.Blocks,
                light ?? new ConstantLight(15, 3), Block, Origin, sink, overrideTexture: texture,
                renderAllFaces: allFaces, doVariance: variance, useCompiledCuboids: compiled);
            Assert.Equal(sink.Vertices.Count != 0, rendered);
            return sink.Vertices;
        }
    }

    private sealed class ConstantLight(byte sky, byte block) : ILightProvider
    {
        public float GetNaturalBrightness(int x, int y, int z, int minLight) => throw new InvalidOperationException("Geometry must retain separate light channels.");
        public float GetLuminance(int x, int y, int z) => throw new InvalidOperationException("Geometry must retain separate light channels.");
        public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) => new LightLevels(sky, block).WithBlockFloor(minBlockLight);
    }

    private sealed class NonuniformLight : ILightProvider
    {
        public float GetNaturalBrightness(int x, int y, int z, int minLight) => throw new InvalidOperationException();
        public float GetLuminance(int x, int y, int z) => throw new InvalidOperationException();
        public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) =>
            new LightLevels((byte)((x * 3 + y * 7 + z * 11) & 15), (byte)((x * 13 + y * 5 + z) & 15)).WithBlockFloor(minBlockLight);
    }

    private readonly record struct Vertex(Vector3 Position, Vector2 Uv, Vector3 Color, Vector2 Light, int Layer, Side Side);

    private sealed class RecordingSink : IBlockVertexSink
    {
        public List<Vertex> Vertices { get; } = [];
        private Vector3 _color;
        private Vector2 _light;
        private int _layer;
        private Side _side;
        public void addVertexWithUV(double x, double y, double z, double u, double v) =>
            Vertices.Add(new Vertex(new Vector3((float)x, (float)y, (float)z), new Vector2((float)u, (float)v), _color, _light, _layer, _side));
        public void setArrayLayer(int layer) => _layer = layer;
        public void setColorOpaque_F(float red, float green, float blue) => _color = new Vector3(red, green, blue);
        public void setLight(float sky, float block) => _light = new Vector2(sky, block);
        public void setQuadDirection(Side? side) => _side = side ?? throw new InvalidOperationException("Cuboid faces must have a direction.");
        public void setTranslationF(float x, float y, float z) => throw new InvalidOperationException("Cuboids use absolute world positions.");
    }
}

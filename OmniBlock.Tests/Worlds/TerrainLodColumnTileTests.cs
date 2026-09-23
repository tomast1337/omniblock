using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodColumnTileTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(1, "example:stone",
            TerrainLodGeometryClass.Opaque, true, 0x707070),
        new TerrainLodMaterialDefinition(2, "example:water",
            TerrainLodGeometryClass.Liquid, false, 0x4040FF)
    ]);

    private static readonly TerrainLodMaterial Stone = Materials.Resolve(1, 0);
    private static readonly TerrainLodMaterial Water = Materials.Resolve(2, 0);

    [Fact]
    public void Detail_only_cactus_and_flowers_do_not_expand_to_two_block_columns()
    {
        var cactus = Stone with
        {
            BlockId = "example:cactus", Geometry = TerrainLodGeometryClass.BoundedCube,
            OccludesFaces = false, MaxSampleSize = 1
        };
        var flower = cactus with
        {
            BlockId = "example:flower", Geometry = TerrainLodGeometryClass.CrossedQuad
        };
        var air = Column(4, new TerrainLodColumnSpan(
            0, 4, TerrainLodMaterial.Air, 0, 15));
        foreach (var detail in new[] { cactus, flower })
        {
            var single = Column(4, new TerrainLodColumnSpan(0, 4, detail, 0, 15));
            var reduced = TerrainLodColumnReducer.MergeFour(single, air, air, air);
            Assert.All(reduced.Spans, span => Assert.True(span.IsAir));
            Assert.Equal(detail, single.Spans[0].Material);
        }
    }

    [Fact]
    public void Snow_surface_layer_remains_eligible_when_horizontal_samples_grow()
    {
        var snow = Stone with
        {
            BlockId = "example:snow", Geometry = TerrainLodGeometryClass.SurfaceLayer,
            OccludesFaces = false
        };
        var single = Column(4, new TerrainLodColumnSpan(0, 4, snow, 0, 15));
        var air = Column(4, new TerrainLodColumnSpan(
            0, 4, TerrainLodMaterial.Air, 0, 15));

        var reduced = TerrainLodColumnReducer.MergeFour(single, air, air, air);

        Assert.Equal(snow, Assert.Single(reduced.Spans).Material);
    }

    [Fact]
    public void Leaf_columns_preserve_explicit_cave_air_intervals()
    {
        var source = Snapshot(3, (x, y, z) =>
            x == 0 && z == 0 && y is not (2 or 3 or 4) ? (byte)1 : (byte)0,
            height: 8);

        var leaf = TerrainLodColumnTile.BuildLeaf(source, Materials);

        Assert.Equal(new TerrainLodTileKey(0, 3, -4), leaf.Key);
        Assert.Equal(16, leaf.Width);
        Assert.Equal(0, leaf.HorizontalSampleLevel);
        Assert.Equal(3, leaf[0, 0].Spans.Count);
        Assert.Equal(
            [(0, 2, false), (2, 3, true), (5, 3, false)],
            CoalescedMaterialSpans(leaf[0, 0]));
        Assert.Single(leaf[1, 0].Spans);
        Assert.True(leaf[1, 0].Spans[0].IsAir);
    }

    [Fact]
    public void Broad_cave_survives_four_column_reduction()
    {
        var cave = Column(8,
            new TerrainLodColumnSpan(0, 2, Stone, 0, 0),
            new TerrainLodColumnSpan(2, 3, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(5, 3, Stone, 0, 0));

        var reduced = TerrainLodColumnReducer.MergeFour(cave, cave, cave, cave);

        Assert.Equal(
            [(0, 2, false), (2, 3, true), (5, 3, false)],
            CoalescedMaterialSpans(reduced));
    }

    [Fact]
    public void Narrow_cave_is_closed_conservatively_in_distant_parent()
    {
        var solid = Column(8, new TerrainLodColumnSpan(0, 8, Stone, 0, 0));
        var cave = Column(8,
            new TerrainLodColumnSpan(0, 2, Stone, 0, 0),
            new TerrainLodColumnSpan(2, 3, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(5, 3, Stone, 0, 0));

        var reduced = TerrainLodColumnReducer.MergeFour(cave, solid, solid, solid);

        Assert.Single(reduced.Spans);
        Assert.Equal(Stone, reduced.Spans[0].Material);
        Assert.Equal(8, reduced.Spans[0].Height);
    }

    [Fact]
    public void Light_is_averaged_only_from_columns_supporting_the_winning_material()
    {
        var stoneFour = Column(4, new TerrainLodColumnSpan(0, 4, Stone, 4, 10));
        var stoneEight = Column(4, new TerrainLodColumnSpan(0, 4, Stone, 8, 14));
        var waterBright = Column(4, new TerrainLodColumnSpan(0, 4, Water, 15, 15));
        var airDark = Column(4,
            new TerrainLodColumnSpan(0, 4, TerrainLodMaterial.Air, 0, 0));

        var reduced = TerrainLodColumnReducer.MergeFour(
            stoneFour, stoneEight, waterBright, airDark);

        Assert.Single(reduced.Spans);
        Assert.Equal(Stone, reduced.Spans[0].Material);
        Assert.Equal(6, reduced.Spans[0].BlockLight);
        Assert.Equal(12, reduced.Spans[0].SkyLight);
    }

    [Fact]
    public void Tint_identity_uses_the_same_deterministic_material_vote_as_geometry()
    {
        var lush = Stone with { MapColor = 0x55AA44 };
        var dry = Stone with { MapColor = 0xAA9944 };
        var lushColumn = Column(4, new TerrainLodColumnSpan(0, 4, lush, 0, 15));
        var dryColumn = Column(4, new TerrainLodColumnSpan(0, 4, dry, 0, 15));

        var reduced = TerrainLodColumnReducer.MergeFour(
            lushColumn, dryColumn, lushColumn, lushColumn);

        Assert.Single(reduced.Spans);
        Assert.Equal(lush, reduced.Spans[0].Material);
        Assert.Equal(0x55AA44u, reduced.Spans[0].Material.MapColor);
    }

    [Fact]
    public void Liquid_surface_and_air_above_it_survive_parent_reduction()
    {
        var shoreline = Column(8,
            new TerrainLodColumnSpan(0, 4, Stone, 0, 0),
            new TerrainLodColumnSpan(4, 2, Water, 0, 12),
            new TerrainLodColumnSpan(6, 2, TerrainLodMaterial.Air, 0, 15));

        var reduced = TerrainLodColumnReducer.MergeFour(
            shoreline, shoreline, shoreline, shoreline);

        Assert.Equal(3, reduced.Spans.Count);
        Assert.Equal(TerrainLodGeometryClass.Liquid, reduced.Spans[1].Material.Geometry);
        Assert.Equal(4, reduced.Spans[1].BottomY);
        Assert.Equal(6, reduced.Spans[1].TopY);
        Assert.True(reduced.Spans[2].IsAir);
    }

    [Fact]
    public void Overhang_and_ceiling_air_intervals_remain_in_canonical_parent_data()
    {
        var ceiling = Column(10,
            new TerrainLodColumnSpan(0, 2, Stone, 0, 0),
            new TerrainLodColumnSpan(2, 3, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(5, 2, Stone, 0, 0),
            new TerrainLodColumnSpan(7, 3, TerrainLodMaterial.Air, 0, 0));

        var reduced = TerrainLodColumnReducer.MergeFour(
            ceiling, ceiling, ceiling, ceiling);

        Assert.Equal(4, reduced.Spans.Count);
        Assert.True(reduced.Spans[1].IsAir);
        Assert.Equal(Stone, reduced.Spans[2].Material);
        Assert.True(reduced.Spans[3].IsAir);
        Assert.All(reduced.Spans, span => Assert.Equal(0, span.SkyLight));
    }

    [Fact]
    public void Vertical_budget_collapses_light_bands_before_cave_or_surface_boundaries()
    {
        var source = Column(9,
            new TerrainLodColumnSpan(0, 2, Stone, 0, 0),
            new TerrainLodColumnSpan(2, 2, Stone, 4, 8),
            new TerrainLodColumnSpan(4, 4, TerrainLodMaterial.Air, 15, 0),
            new TerrainLodColumnSpan(8, 1, Water, 15, 15));

        var reduced = TerrainLodVerticalSliceReducer.Reduce(source, 3);

        Assert.Equal(3, reduced.Spans.Count);
        Assert.Equal(Stone, reduced.Spans[0].Material);
        Assert.Equal(4, reduced.Spans[0].Height);
        Assert.True(reduced.Spans[1].IsAir);
        Assert.Equal(Water, reduced.Spans[2].Material);
        Assert.Equal(8, reduced.Spans[2].BottomY);
    }

    [Fact]
    public void Vertical_budget_is_deterministic_complete_and_does_not_mutate_source()
    {
        var source = Column(10,
            new TerrainLodColumnSpan(0, 2, Stone, 0, 0),
            new TerrainLodColumnSpan(2, 3, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(5, 2, Water, 8, 4),
            new TerrainLodColumnSpan(7, 3, Stone, 2, 6));

        var first = TerrainLodVerticalSliceReducer.Reduce(source, 2);
        var second = TerrainLodVerticalSliceReducer.Reduce(source, 2);

        Assert.Equal(4, source.Spans.Count);
        Assert.True(first.Spans.Count <= 2);
        Assert.Equal(first.Spans, second.Spans);
        Assert.Equal(0, first.Spans[0].BottomY);
        Assert.Equal(10, first.Spans[^1].TopY);
        for (var i = 1; i < first.Spans.Count; i++)
            Assert.Equal(first.Spans[i - 1].TopY, first.Spans[i].BottomY);
    }

    [Fact]
    public void Parent_can_retain_horizontal_samples_without_reducing_vertical_columns()
    {
        var parentKey = new TerrainLodTileKey(1, -1, 0);
        var children = Children(parentKey, [1, 2, 2, 1]);

        var parent = TerrainLodColumnTile.BuildParent(
            parentKey, children, horizontalSampleLevel: 0);

        Assert.Equal(32, parent.Width);
        Assert.Equal(Stone, parent[0, 0].Spans[0].Material);
        Assert.Equal(Water, parent[31, 0].Spans[0].Material);
        Assert.Equal(Water, parent[0, 31].Spans[0].Material);
        Assert.Equal(Stone, parent[31, 31].Spans[0].Material);
        Assert.Equal(children.Select(static child => child.CanonicalHash), parent.InputHashes);
    }

    [Fact]
    public void Parent_can_advance_horizontal_sampling_by_one_octave()
    {
        var parentKey = new TerrainLodTileKey(1, 0, 0);
        var children = Children(parentKey, [1, 2, 2, 1]);

        var parent = TerrainLodColumnTile.BuildParent(
            parentKey, children, horizontalSampleLevel: 1);

        Assert.Equal(16, parent.Width);
        Assert.Equal(Stone, parent[0, 0].Spans[0].Material);
        Assert.Equal(Water, parent[15, 0].Spans[0].Material);
        Assert.Equal(Water, parent[0, 15].Spans[0].Material);
        Assert.Equal(Stone, parent[15, 15].Spans[0].Material);
    }

    [Fact]
    public void Parent_identity_changes_when_a_child_revision_changes()
    {
        var parentKey = new TerrainLodTileKey(1, 0, 0);
        var firstChildren = Children(parentKey, [1, 1, 1, 1], revision: 4);
        var secondChildren = Children(parentKey, [1, 1, 1, 1], revision: 4);
        secondChildren[2] = UniformLeaf(parentKey.Child(2), 1, revision: 5);

        var first = TerrainLodColumnTile.BuildParent(parentKey, firstChildren, 1);
        var second = TerrainLodColumnTile.BuildParent(parentKey, secondChildren, 1);

        Assert.NotEqual(first.InputHashes[2], second.InputHashes[2]);
        Assert.NotEqual(first.CanonicalHash, second.CanonicalHash);
    }

    [Fact]
    public void Uniform_import_tile_has_policy_dimensions_and_stable_identity()
    {
        var key = new TerrainLodTileKey(4, -2, 3);
        var column = Column(128,
            new TerrainLodColumnSpan(0, 64, Stone, 0, 0),
            new TerrainLodColumnSpan(64, 64, TerrainLodMaterial.Air, 0, 15));

        var first = TerrainLodColumnTile.CreateUniform(
            key, horizontalSampleLevel: 2, 128, column, "fixture-v1");
        var second = TerrainLodColumnTile.CreateUniform(
            key, horizontalSampleLevel: 2, 128, column, "fixture-v1");

        Assert.Equal(64, first.Width);
        Assert.Equal(4, first.InputHashes.Count);
        Assert.Null(first.LeafTerrainRevision);
        Assert.Equal(column.Spans, first[0, 0].Spans);
        Assert.Equal(column.Spans, first[63, 63].Spans);
        Assert.Equal(first.CanonicalHash, second.CanonicalHash);
    }

    [Fact]
    public void Invalid_noncanonical_column_is_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => Column(8,
            new TerrainLodColumnSpan(0, 4, Stone, 0, 0),
            new TerrainLodColumnSpan(4, 4, Stone, 0, 0)));

        Assert.Contains("canonicalized", error.Message);
    }

    private static TerrainLodColumnTile[] Children(
        TerrainLodTileKey parent,
        int[] blockIds,
        long revision = 1) => Enumerable.Range(0, 4)
        .Select(index => UniformLeaf(parent.Child(index), blockIds[index], revision))
        .ToArray();

    private static TerrainLodColumnTile UniformLeaf(
        TerrainLodTileKey key,
        int blockId,
        long revision)
    {
        if (key.Level != 0) throw new ArgumentException("Test leaf must be level zero.", nameof(key));
        return TerrainLodColumnTile.BuildLeaf(
            Snapshot(key.X, (_, _, _) => (byte)blockId, key.Z, revision: revision),
            Materials);
    }

    private static TerrainLodSourceSnapshot Snapshot(
        int chunkX,
        Func<int, int, int, byte> block,
        int chunkZ = -4,
        int height = 8,
        long revision = 1)
    {
        var blocks = new byte[16 * height * 16];
        var metadata = new byte[blocks.Length];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < height; y++)
            blocks[(x * 16 + z) * height + y] = block(x, y, z);
        return new TerrainLodSourceSnapshot(
            chunkX, chunkZ, 16, height, 16, blocks, metadata, revision);
    }

    private static TerrainLodColumn Column(
        int height,
        params TerrainLodColumnSpan[] spans) => TerrainLodColumn.Create(height, spans);

    private static (int Bottom, int Height, bool Air)[] CoalescedMaterialSpans(
        TerrainLodColumn column)
    {
        List<(int Bottom, int Height, bool Air)> values = [];
        foreach (var span in column.Spans)
        {
            if (values.Count > 0 && values[^1].Air == span.IsAir)
            {
                var previous = values[^1];
                values[^1] = (previous.Bottom, previous.Height + span.Height, previous.Air);
            }
            else
            {
                values.Add((span.BottomY, span.Height, span.IsAir));
            }
        }
        return [.. values];
    }
}

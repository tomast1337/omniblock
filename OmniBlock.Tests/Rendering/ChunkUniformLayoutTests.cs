using System.Runtime.InteropServices;
using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkUniformLayoutTests
{
    [Fact]
    public void Draw_metadata_layout_matches_chunk_shader()
    {
        Assert.Equal(48, Marshal.SizeOf<ChunkDrawMetadata>());
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.RegionCellX), 0);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.FadeProgress), 12);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.LocalOriginX), 16);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.ChunkFadeEnabled), 28);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.ChunkPosX), 32);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.PresentationFadeMode), 40);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.PresentationFadeSeed), 44);
    }

    [Fact]
    public void Frame_uniform_layout_matches_chunk_shader()
    {
        Assert.Equal(336, Marshal.SizeOf<ChunkFrameUniforms>());
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.ModelViewMatrix), 0);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.ProjectionMatrix), 64);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.CameraCellX), 128);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.CameraLocalX), 144);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.TimeX), 160);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.AmbientDarkness), 172);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyPlantMode), 196);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyLeafLayer0), 208);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyLeafCount), 240);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyPlantLayer0), 256);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyPlantCount), 288);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.FogColorR), 304);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.FogMode), 332);
    }

    [Theory]
    [InlineData(-2049, -3, 1023)]
    [InlineData(-1024, -1, 0)]
    [InlineData(-1, -1, 1023)]
    [InlineData(0, 0, 0)]
    [InlineData(1023, 0, 1023)]
    [InlineData(1024, 1, 0)]
    public void Integer_coordinates_split_into_exact_cells(int coordinate, int expectedCell, int expectedLocal)
    {
        TerrainCoordinateFrame.Split(coordinate, out var cell, out var local);

        Assert.Equal(expectedCell, cell);
        Assert.Equal(expectedLocal, local);
        Assert.Equal(coordinate, cell * TerrainCoordinateFrame.CellSize + local);
    }

    [Theory]
    [InlineData(-1024.25, -2, 1023.75)]
    [InlineData(-0.25, -1, 1023.75)]
    [InlineData(0.25, 0, 0.25)]
    [InlineData(1024.25, 1, 0.25)]
    public void Camera_coordinates_keep_a_small_local_component(
        double coordinate, int expectedCell, float expectedLocal)
    {
        TerrainCoordinateFrame.Split(coordinate, out var cell, out var local);

        Assert.Equal(expectedCell, cell);
        Assert.Equal(expectedLocal, local);
    }

    private static void AssertOffset<T>(string field, int expected) where T : struct =>
        Assert.Equal(new IntPtr(expected), Marshal.OffsetOf<T>(field));
}

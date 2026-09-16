using System.Runtime.InteropServices;
using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkUniformLayoutTests
{
    [Fact]
    public void Draw_metadata_layout_matches_chunk_shader()
    {
        Assert.Equal(96, Marshal.SizeOf<ChunkDrawMetadata>());
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.ModelViewMatrix), 0);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.ChunkPosX), 64);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.FadeProgress), 72);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.ChunkFadeEnabled), 76);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.PresentationFadeMode), 80);
        AssertOffset<ChunkDrawMetadata>(nameof(ChunkDrawMetadata.PresentationFadeSeed), 84);
    }

    [Fact]
    public void Frame_uniform_layout_matches_chunk_shader()
    {
        Assert.Equal(240, Marshal.SizeOf<ChunkFrameUniforms>());
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.ProjectionMatrix), 0);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.TimeX), 64);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.AmbientDarkness), 76);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyPlantMode), 100);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyLeafLayer0), 112);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyLeafCount), 144);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyPlantLayer0), 160);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.WavyPlantCount), 192);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.FogColorR), 208);
        AssertOffset<ChunkFrameUniforms>(nameof(ChunkFrameUniforms.FogMode), 236);
    }

    private static void AssertOffset<T>(string field, int expected) where T : struct =>
        Assert.Equal(new IntPtr(expected), Marshal.OffsetOf<T>(field));
}

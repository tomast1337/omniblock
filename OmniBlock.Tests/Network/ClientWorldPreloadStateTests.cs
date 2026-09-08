using OmniBlock.Client.Network;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Network;

public sealed class ClientWorldPreloadStateTests
{
    [Fact]
    public void Readiness_requires_all_decoded_columns_and_nearby_vertical_meshes()
    {
        ClientWorldPreloadState state = new();
        state.SetSpawn(8, 64, 8);

        for (var x = -2; x <= 2; x++)
        for (var z = -2; z <= 2; z++)
        {
            state.MarkChunkDecoded(x, z);
            state.MarkMeshUploaded(new Vector3D<int>(x * 16, 4 * 16, z * 16));
        }

        Assert.Equal(25, state.DecodedChunks);
        Assert.Equal(9, state.UploadedMeshes);
        Assert.True(state.IsReady);
    }

    [Fact]
    public void Data_outside_preload_radius_does_not_advance_progress()
    {
        ClientWorldPreloadState state = new();
        state.SetSpawn(8, 64, 8);
        state.MarkChunkDecoded(3, 0);
        state.MarkMeshUploaded(new Vector3D<int>(48, 64, 0));

        Assert.Equal(0, state.DecodedChunks);
        Assert.Equal(0, state.UploadedMeshes);
        Assert.False(state.IsReady);
    }

    [Fact]
    public void Unloading_required_chunk_revokes_readiness()
    {
        ClientWorldPreloadState state = new();
        state.SetSpawn(8, 64, 8);
        for (var x = -2; x <= 2; x++)
        for (var z = -2; z <= 2; z++)
        {
            state.MarkChunkDecoded(x, z);
            state.MarkMeshUploaded(new Vector3D<int>(x * 16, 4 * 16, z * 16));
        }

        state.MarkChunkUnloaded(1, 1);

        Assert.False(state.IsReady);
    }

    [Fact]
    public void Required_unmeshed_startup_sections_are_identified_for_priority()
    {
        ClientWorldPreloadState state = new();
        state.SetSpawn(8, 64, 8);

        Assert.True(state.RequiresMesh(new Vector3D<int>(16, 48, -16)));
        Assert.True(state.RequiresMesh(new Vector3D<int>(16, 80, -16)));
        Assert.False(state.RequiresMesh(new Vector3D<int>(48, 64, -32)));
        Assert.False(state.RequiresMesh(new Vector3D<int>(16, 96, -16)));

        state.MarkMeshUploaded(new Vector3D<int>(16, 64, -16));

        Assert.False(state.RequiresMesh(new Vector3D<int>(16, 48, -16)));
        Assert.False(state.RequiresMesh(new Vector3D<int>(16, 80, -16)));
    }

    [Fact]
    public void Required_mesh_enumeration_covers_three_sections_in_each_pending_column()
    {
        ClientWorldPreloadState state = new();
        state.SetSpawn(8, 64, 8);

        Assert.Equal(27, state.RequiredMeshSections().Count());

        state.MarkMeshUploaded(new Vector3D<int>(-16, 64, 16));

        Assert.Equal(24, state.RequiredMeshSections().Count());
    }

    [Fact]
    public void Visual_state_reports_decoded_and_meshed_columns_independently()
    {
        ClientWorldPreloadState state = new();
        state.SetSpawn(8, 64, 8);

        state.MarkChunkDecoded(1, -1);
        Assert.True(state.IsChunkDecoded(1, -1));
        Assert.False(state.HasMesh(1, -1));

        state.MarkMeshUploaded(new Vector3D<int>(16, 80, -16));
        Assert.True(state.HasMesh(1, -1));
        Assert.False(state.IsChunkDecoded(-1, 1));
    }
}

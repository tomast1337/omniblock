namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMatrixOwnershipTests
{
    [Fact]
    public void Chunk_renderer_cannot_read_global_matrix_stacks()
    {
        var root = FindRepositoryRoot();
        var file = Path.Combine(root, "OmniBlock.Client", "Rendering", "Chunks", "ChunkRenderer.cs");
        var source = File.ReadAllText(file);

        Assert.DoesNotContain("RenderSystem.ModelView", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderSystem.Projection", source, StringComparison.Ordinal);
        Assert.Contains("_modelView = renderParams.ModelView", source, StringComparison.Ordinal);
        Assert.Contains("_projection = renderParams.Projection", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmniBlock.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Could not locate the OmniBlock repository root.");
    }
}

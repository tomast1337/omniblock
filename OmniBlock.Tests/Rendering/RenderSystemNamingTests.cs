namespace OmniBlock.Tests.Rendering;

public sealed class RenderSystemNamingTests
{
    [Fact]
    public void Legacy_gl_manager_name_cannot_return()
    {
        var root = FindRepositoryRoot();
        var legacyName = "GL" + "Manager";
        Assert.False(File.Exists(Path.Combine(
            root, "OmniBlock.Client", "Rendering", "Core", legacyName + ".cs")));

        var separator = Path.DirectorySeparatorChar.ToString();
        var offenders = Directory.EnumerateFiles(
                Path.Combine(root, "OmniBlock.Client"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(separator + "bin" + separator, StringComparison.Ordinal)
                           && !file.Contains(separator + "obj" + separator, StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains(legacyName, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file))
            .Order()
            .ToArray();

        Assert.Empty(offenders);
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

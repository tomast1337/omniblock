namespace OmniBlock.Tests.Entities;

using RuntimeEntity = global::OmniBlock.Entities.Entity;
using RuntimeEntityLiving = global::OmniBlock.Entities.EntityLiving;
using RuntimeEntityCreature = global::OmniBlock.Entities.EntityCreature;
using RuntimeEntityType = global::OmniBlock.Entities.EntityType;

public sealed class EntityRuntimeIsolationArchitectureTests
{
    [Fact]
    public void Removed_entity_globals_do_not_exist_or_appear_in_runtime_source()
    {
        string root = FindRepositoryRoot();
        string[] removedFiles =
        [
            Path.Combine(root, "OmniBlock", "Entities", "EntityRegistry.cs"),
            Path.Combine(root, "OmniBlock", "Entities", "EntityDefinitionRegistry.cs")
        ];
        Assert.All(removedFiles, file => Assert.False(File.Exists(file), file));

        string[] projects = ["OmniBlock", "OmniBlock.Client", "OmniBlock.Server"];
        string[] offenders = projects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(file =>
            {
                string source = File.ReadAllText(file);
                return source.Contains("EntityRegistry.", StringComparison.Ordinal)
                       || source.Contains("EntityDefinitionRegistry.", StringComparison.Ordinal)
                       || source.Contains("DefaultRegistries.EntityTypes", StringComparison.Ordinal)
                       || source.Contains("ContentRuntime.Current.EntityTypes", StringComparison.Ordinal)
                       || source.Contains("static class EntityBehaviorRegistry", StringComparison.Ordinal)
                       || source.Contains("static class MobBehaviorRegistry", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(root, file))
            .Order()
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Ordinary_entity_bodies_require_an_explicit_entity_type()
    {
        AssertConstructorRequiresType(typeof(RuntimeEntity), nonPublic: true);
        AssertConstructorRequiresType(typeof(RuntimeEntityLiving));
        AssertConstructorRequiresType(typeof(RuntimeEntityCreature));

        static void AssertConstructorRequiresType(Type type, bool nonPublic = false)
        {
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                                                   | System.Reflection.BindingFlags.Public
                                                   | (nonPublic ? System.Reflection.BindingFlags.NonPublic : 0);
            Assert.All(type.GetConstructors(flags), constructor =>
                Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(RuntimeEntityType)));
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmniBlock.slnx")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Could not locate the OmniBlock repository root.");
    }
}

using System.Reflection;
using OmniBlock.Entities;

namespace OmniBlock.Tests.Entities;

using RuntimeEntity = Entity;
using RuntimeEntityLiving = EntityLiving;
using RuntimeEntityCreature = EntityCreature;
using RuntimeEntityType = EntityType;

public sealed class EntityRuntimeIsolationArchitectureTests
{
    [Fact]
    public void Removed_entity_globals_do_not_exist_or_appear_in_runtime_source()
    {
        var root = FindRepositoryRoot();
        string[] removedFiles =
        [
            Path.Combine(root, "OmniBlock", "Entities", "EntityRegistry.cs"),
            Path.Combine(root, "OmniBlock", "Entities", "EntityDefinitionRegistry.cs")
        ];
        Assert.All(removedFiles, file => Assert.False(File.Exists(file), file));

        string[] projects = ["OmniBlock", "OmniBlock.Client", "OmniBlock.Server"];
        var offenders = projects
            .SelectMany(project => Directory.EnumerateFiles(
                Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(file =>
            {
                var source = File.ReadAllText(file);
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
        AssertConstructorRequiresType(typeof(RuntimeEntity), true);
        AssertConstructorRequiresType(typeof(RuntimeEntityLiving));
        AssertConstructorRequiresType(typeof(RuntimeEntityCreature));

        static void AssertConstructorRequiresType(Type type, bool nonPublic = false)
        {
            var flags = BindingFlags.Instance
                        | BindingFlags.Public
                        | (nonPublic ? BindingFlags.NonPublic : 0);
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

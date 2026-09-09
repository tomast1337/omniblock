using System.Reflection;

namespace OmniBlock.Tests.Worlds;

public sealed class NaturalSpawnerIsolationTests
{
    [Fact]
    public void Spawn_pass_does_not_share_a_process_global_chunk_working_set()
    {
        var staticHashSets = typeof(NaturalSpawner)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType.IsGenericType
                            && field.FieldType.GetGenericTypeDefinition() == typeof(HashSet<>));

        Assert.Empty(staticHashSets);
    }
}

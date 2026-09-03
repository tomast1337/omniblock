using OmniBlock.Blocks;
using OmniBlock.Items;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Initializes global registries and core static tables once for entity tests.
/// This prevents order-dependent static constructor crashes in parallel execution.
/// </summary>
[CollectionDefinition("EntityTests")]
public sealed class EntityTestsCollection : ICollectionFixture<EntityTestsFixture>;

public sealed class EntityTestsFixture
{
    private static readonly object s_initLock = new();
    private static bool s_initialized;

    public EntityTestsFixture()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        lock (s_initLock)
        {
            if (s_initialized)
            {
                return;
            }

            // Trigger class initialization in a stable order for tests.
            _ = ContentRuntime.Current.Items.Get("omniblock:stick").Id;
            _ = BlockRegistry.Get("stone").Id;

            s_initialized = true;
        }
    }
}

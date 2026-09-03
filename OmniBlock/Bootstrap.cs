using OmniBlock.Blocks;
using OmniBlock.Registries;

namespace OmniBlock;

public class Bootstrap
{
    private static readonly object s_lock = new();

    public static ContentRuntime Initialize()
    {
        lock (s_lock)
        {
            if (ContentRuntime.IsPublished) return ContentRuntime.Current;

            ContentRuntimeBuilder content = ContentRuntimeBuilder.CreateBuiltIns();
            try
            {
                DefaultRegistries.Initialize(content);
                ContentRuntime runtime = content.Build();
                content.PublishEntitiesToLegacyRegistry();
                Stats.Stats.InitializeCraftedItemStats(runtime.Items, runtime.Processes);
                ContentRuntime.Publish(runtime);
                return runtime;
            }
            finally
            {
                BlockRegistry.CompleteBootstrap(content);
            }
        }
    }
}

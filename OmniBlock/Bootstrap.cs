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

            var content = ContentRuntimeBuilder.CreateBuiltIns();
            try
            {
                DefaultRegistries.Initialize(content);
                var runtime = content.Build();
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

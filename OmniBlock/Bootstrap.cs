using OmniBlock.Blocks;
using OmniBlock.Registries;

namespace OmniBlock;

public class Bootstrap
{
    private static readonly object s_lock = new();

    public static void Initialize()
    {
        lock (s_lock)
        {
            if (ContentRuntime.IsPublished) return;

            ContentRuntimeBuilder content = ContentRuntimeBuilder.CreateBuiltIns();
            try
            {
                DefaultRegistries.Initialize(content);
                ContentRuntime.Publish(content.Build());
            }
            finally
            {
                BlockRegistry.CompleteBootstrap(content);
            }
        }
    }
}

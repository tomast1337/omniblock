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
            DefaultRegistries.Initialize(content);
            ContentRuntime.Publish(content.Build());
        }
    }
}

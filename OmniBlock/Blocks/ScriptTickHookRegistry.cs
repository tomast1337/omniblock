using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks;

internal static class ScriptTickHookRegistry
{
    public delegate void TickHook(TickHost host, int x, int y, int z, int meta, int blockId);

    private static readonly Dictionary<string, TickHook> s_hooks = new();

    public static void Register(string key, TickHook hook)
    {
        s_hooks[key] = hook;
    }

    public static bool TryGet(string key, out TickHook hook)
    {
        return s_hooks.TryGetValue(key, out hook!);
    }
}
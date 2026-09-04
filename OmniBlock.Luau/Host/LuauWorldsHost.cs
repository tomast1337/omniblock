using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

public readonly record struct LuauWorldInfo(
    string Id,
    string Name,
    double LastPlayed,
    double Size,
    bool Unsupported);

/// <summary>FFI facade for discovering saves and queueing safe client world transitions.</summary>
public static unsafe class LuauWorldsHost
{
    public const string Bootstrap = """
                                    OMNI.client = OMNI.client or {}
                                    OMNI.client.worlds = {
                                        list = function() return __Worlds.list() end,
                                        load = function(id) return __Worlds.load(id) end,
                                    }
                                    """;

    public static Func<IReadOnlyList<LuauWorldInfo>>? List;
    public static Func<string, bool>? Load;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 2);
        Add(l, "list", &ListClosure);
        Add(l, "load", &LoadClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__Worlds");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__Worlds." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ListClosure(IntPtr l)
    {
        IReadOnlyList<LuauWorldInfo> worlds = [];
        try
        {
            worlds = List?.Invoke() ?? [];
        }
        catch
        {
            // No managed exception may unwind through an UnmanagedCallersOnly frame.
        }

        LuauNative.lua_createtable(l, worlds.Count, 0);
        for (var i = 0; i < worlds.Count; i++)
        {
            var world = worlds[i];
            LuauNative.lua_createtable(l, 0, 5);
            Set(l, "id", world.Id);
            Set(l, "name", world.Name);
            Set(l, "lastPlayed", world.LastPlayed);
            Set(l, "size", world.Size);
            Set(l, "unsupported", world.Unsupported);
            LuauNative.lua_rawseti(l, -2, i + 1);
        }

        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int LoadClosure(IntPtr l)
    {
        var accepted = false;
        try
        {
            var id = ReadString(l, 1);
            accepted = id != null && Load?.Invoke(id) == true;
        }
        catch
        {
            // No managed exception may unwind through an UnmanagedCallersOnly frame.
        }

        LuauNative.lua_pushboolean(l, accepted ? 1 : 0);
        return 1;
    }

    private static string? ReadString(IntPtr l, int index)
    {
        var pointer = LuauNative.lua_tolstring(l, index, out var length);
        return pointer == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)pointer, (int)length);
    }

    private static void Set(IntPtr l, string key, string value)
    {
        LuauNative.lua_pushstring(l, value);
        LuauNative.lua_setfield(l, -2, key);
    }

    private static void Set(IntPtr l, string key, double value)
    {
        LuauNative.lua_pushnumber(l, value);
        LuauNative.lua_setfield(l, -2, key);
    }

    private static void Set(IntPtr l, string key, bool value)
    {
        LuauNative.lua_pushboolean(l, value ? 1 : 0);
        LuauNative.lua_setfield(l, -2, key);
    }
}

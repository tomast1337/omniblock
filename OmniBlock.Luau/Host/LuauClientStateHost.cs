using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OmniBlock.Luau.Host;

/// <summary>Read-only FFI facade for client readiness observed by automation scripts.</summary>
public static unsafe class LuauClientStateHost
{
    public const string Bootstrap = """
                                    OMNI.client = OMNI.client or {}
                                    OMNI.client.state = setmetatable({}, {
                                        __index = function(_, key)
                                            if key == "worldLoaded" then return __ClientState.worldLoaded() end
                                            if key == "playerReady" then return __ClientState.playerReady() end
                                            if key == "worldId" then return __ClientState.worldId() end
                                            return nil
                                        end,
                                        __newindex = function()
                                            error("OMNI.client.state is read-only", 2)
                                        end,
                                    })
                                    """;

    public static Func<bool>? WorldLoaded;
    public static Func<bool>? PlayerReady;
    public static Func<string?>? WorldId;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 3);
        Add(l, "worldLoaded", &WorldLoadedClosure);
        Add(l, "playerReady", &PlayerReadyClosure);
        Add(l, "worldId", &WorldIdClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__ClientState");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__ClientState." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WorldLoadedClosure(IntPtr l)
    {
        LuauNative.lua_pushboolean(l, ReadBool(WorldLoaded) ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerReadyClosure(IntPtr l)
    {
        LuauNative.lua_pushboolean(l, ReadBool(PlayerReady) ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WorldIdClosure(IntPtr l)
    {
        string? id = null;
        try
        {
            id = WorldId?.Invoke();
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
        }

        if (id == null) LuauNative.lua_pushnil(l);
        else LuauNative.lua_pushstring(l, id);
        return 1;
    }

    private static bool ReadBool(Func<bool>? getter)
    {
        try
        {
            return getter?.Invoke() == true;
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            return false;
        }
    }
}

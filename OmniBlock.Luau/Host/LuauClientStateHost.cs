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
                                            if key == "meshPending" then return __ClientState.meshPending() end
                                            if key == "meshRequestToGpuMs" then return __ClientState.meshRequestToGpuMs() end
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
    public static Func<double>? MeshPending;
    public static Func<double>? MeshRequestToGpuMs;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 5);
        Add(l, "worldLoaded", &WorldLoadedClosure);
        Add(l, "playerReady", &PlayerReadyClosure);
        Add(l, "worldId", &WorldIdClosure);
        Add(l, "meshPending", &MeshPendingClosure);
        Add(l, "meshRequestToGpuMs", &MeshRequestToGpuMsClosure);
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

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshPendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshPending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshRequestToGpuMsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshRequestToGpuMs));
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

    private static double ReadNumber(Func<double>? getter)
    {
        try
        {
            return getter?.Invoke() ?? 0;
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            return 0;
        }
    }
}

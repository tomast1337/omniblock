using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

/// <summary>Restricted process-control facade installed only for explicit E2E launches.</summary>
public static unsafe class LuauTestHost
{
    public const string Bootstrap = """
                                    OMNI.test = {
                                        pass = function() __Test.pass() end,
                                        fail = function(reason) __Test.fail(tostring(reason or "Test failed")) end,
                                        creative = function() __Test.creative() end,
                                        breakBlock = function(x, y, z) return __Test.breakBlock(x, y, z) end,
                                        setBlock = function(id, x, y, z) __Test.setBlock(tostring(id), x, y, z) end,
                                        isMeshCurrent = function(x, y, z) return __Test.isMeshCurrent(x, y, z) end,
                                        meshDeadlineMissCount = function(x, y, z) return __Test.meshDeadlineMissCount(x, y, z) end,
                                        setFlying = function(value) __Test.setFlying(value) end,
                                        teleport = function(x, y, z) __Test.teleport(x, y, z) end,
                                        setLook = function(yaw, pitch) __Test.setLook(yaw, pitch) end,
                                        setMovement = function(forward, strafe, vertical) __Test.setMovement(forward, strafe, vertical) end,
                                        flyPath = function(ax, ay, az, bx, by, bz, seconds) __Test.flyPath(ax, ay, az, bx, by, bz, seconds) end,
                                        screenshot = function() __Test.screenshot() end,
                                        dumpTerrain = function(label) __Test.dumpTerrain(tostring(label or "terrain")) end,
                                        entityBaseline = function(scene, count, distance) return __Test.entityBaseline(scene, count, distance) end,
                                        beginEntitySample = function() return __Test.beginEntitySample() end,
                                        endEntitySample = function(label) return __Test.endEntitySample(label) end,
                                        clearEntityBaseline = function() __Test.clearEntityBaseline() end,
                                    }
                                    local previousHas = OMNI.has
                                    OMNI.has = function(capability)
                                        return capability == "test" or previousHas(capability)
                                    end
                                    """;

    public static Action? Pass;
    public static Action<string>? Fail;
    public static Action? Creative;
    public static Func<int, int, int, bool>? BreakBlock;
    public static Action<string, int, int, int>? SetBlock;
    public static Func<int, int, int, bool>? IsMeshCurrent;
    public static Func<int, int, int, double>? MeshDeadlineMissCount;
    public static Action<bool>? SetFlying;
    public static Action<int, int, int>? Teleport;
    public static Action<double, double>? SetLook;
    public static Action<double, double, double>? SetMovement;
    public static Action<double, double, double, double, double, double, double>? FlyPath;
    public static Action? Screenshot;
    public static Action<string>? DumpTerrain;
    public static Func<string, int, double, bool>? EntityBaseline;
    public static Func<bool>? BeginEntitySample;
    public static Func<string, bool>? EndEntitySample;
    public static Action? ClearEntityBaseline;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 18);
        Add(l, "pass", &PassClosure);
        Add(l, "fail", &FailClosure);
        Add(l, "creative", &CreativeClosure);
        Add(l, "breakBlock", &BreakBlockClosure);
        Add(l, "setBlock", &SetBlockClosure);
        Add(l, "isMeshCurrent", &IsMeshCurrentClosure);
        Add(l, "meshDeadlineMissCount", &MeshDeadlineMissCountClosure);
        Add(l, "setFlying", &SetFlyingClosure);
        Add(l, "teleport", &TeleportClosure);
        Add(l, "setLook", &SetLookClosure);
        Add(l, "setMovement", &SetMovementClosure);
        Add(l, "flyPath", &FlyPathClosure);
        Add(l, "screenshot", &ScreenshotClosure);
        Add(l, "dumpTerrain", &DumpTerrainClosure);
        Add(l, "entityBaseline", &EntityBaselineClosure);
        Add(l, "beginEntitySample", &BeginEntitySampleClosure);
        Add(l, "endEntitySample", &EndEntitySampleClosure);
        Add(l, "clearEntityBaseline", &ClearEntityBaselineClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__Test");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__Test." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EntityBaselineClosure(IntPtr l)
    {
        var ok = false;
        try { ok = EntityBaseline?.Invoke(ReadString(l, 1) ?? "", LuauNative.luaL_checkinteger(l, 2),
            LuauNative.luaL_checknumber(l, 3)) == true; }
        catch (Exception error) { Fail?.Invoke($"Entity baseline: {error.Message}"); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int BeginEntitySampleClosure(IntPtr l)
    {
        var ok = false;
        try { ok = BeginEntitySample?.Invoke() == true; }
        catch (Exception error) { Fail?.Invoke($"Entity sample: {error.Message}"); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EndEntitySampleClosure(IntPtr l)
    {
        var ok = false;
        try { ok = EndEntitySample?.Invoke(ReadString(l, 1) ?? "sample") == true; }
        catch (Exception error) { Fail?.Invoke($"Entity sample: {error.Message}"); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ClearEntityBaselineClosure(IntPtr l)
    {
        Invoke(ClearEntityBaseline);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PassClosure(IntPtr l)
    {
        try
        {
            Pass?.Invoke();
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FailClosure(IntPtr l)
    {
        try
        {
            Fail?.Invoke(ReadString(l, 1) ?? "Test failed");
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CreativeClosure(IntPtr l)
    {
        Invoke(Creative);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int BreakBlockClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = BreakBlock?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3)) == true;
        }
        catch
        {
        }

        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetBlockClosure(IntPtr l)
    {
        try
        {
            SetBlock?.Invoke(
                ReadString(l, 1) ?? string.Empty,
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3),
                LuauNative.luaL_checkinteger(l, 4));
        }
        catch
        {
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IsMeshCurrentClosure(IntPtr l)
    {
        var result = InvokeAt(IsMeshCurrent, l, false);
        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshDeadlineMissCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, InvokeAt(MeshDeadlineMissCount, l, 0));
        return 1;
    }

    private static T InvokeAt<T>(Func<int, int, int, T>? callback, IntPtr l, T fallback)
    {
        try
        {
            return callback == null
                ? fallback
                : callback(
                    LuauNative.luaL_checkinteger(l, 1),
                    LuauNative.luaL_checkinteger(l, 2),
                    LuauNative.luaL_checkinteger(l, 3));
        }
        catch
        {
            return fallback;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetFlyingClosure(IntPtr l)
    {
        try
        {
            SetFlying?.Invoke(LuauNative.lua_toboolean(l, 1) != 0);
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TeleportClosure(IntPtr l)
    {
        try
        {
            Teleport?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3));
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetLookClosure(IntPtr l)
    {
        try
        {
            SetLook?.Invoke(
                LuauNative.luaL_checknumber(l, 1),
                LuauNative.luaL_checknumber(l, 2));
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetMovementClosure(IntPtr l)
    {
        try
        {
            SetMovement?.Invoke(
                LuauNative.luaL_checknumber(l, 1),
                LuauNative.luaL_checknumber(l, 2),
                LuauNative.luaL_checknumber(l, 3));
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FlyPathClosure(IntPtr l)
    {
        try
        {
            FlyPath?.Invoke(
                LuauNative.luaL_checknumber(l, 1),
                LuauNative.luaL_checknumber(l, 2),
                LuauNative.luaL_checknumber(l, 3),
                LuauNative.luaL_checknumber(l, 4),
                LuauNative.luaL_checknumber(l, 5),
                LuauNative.luaL_checknumber(l, 6),
                LuauNative.luaL_checknumber(l, 7));
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ScreenshotClosure(IntPtr l)
    {
        Invoke(Screenshot);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DumpTerrainClosure(IntPtr l)
    {
        try
        {
            DumpTerrain?.Invoke(ReadString(l, 1) ?? "terrain");
        }
        catch
        {
        }

        return 0;
    }

    private static void Invoke(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch
        {
        }
    }

    private static string? ReadString(IntPtr l, int index)
    {
        var pointer = LuauNative.lua_tolstring(l, index, out var length);
        return pointer == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)pointer, (int)length);
    }
}

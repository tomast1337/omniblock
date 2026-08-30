using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

/// <summary>Restricted process-control facade installed only for explicit E2E launches.</summary>
public static unsafe class LuauTestHost
{
    public static Action? Pass;
    public static Action<string>? Fail;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 2);
        Add(l, "pass", &PassClosure);
        Add(l, "fail", &FailClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__Test");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__Test." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PassClosure(IntPtr l)
    {
        try { Pass?.Invoke(); } catch { }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FailClosure(IntPtr l)
    {
        try { Fail?.Invoke(ReadString(l, 1) ?? "Test failed"); } catch { }
        return 0;
    }

    private static string? ReadString(IntPtr l, int index)
    {
        IntPtr pointer = LuauNative.lua_tolstring(l, index, out nuint length);
        return pointer == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)pointer, (int)length);
    }

    public const string Bootstrap = """
OMNI.test = {
    pass = function() __Test.pass() end,
    fail = function(reason) __Test.fail(tostring(reason or "Test failed")) end,
}
local previousHas = OMNI.has
OMNI.has = function(capability)
    return capability == "test" or previousHas(capability)
end
""";
}

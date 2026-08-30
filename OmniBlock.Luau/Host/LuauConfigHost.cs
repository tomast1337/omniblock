using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

public enum LuauConfigValueKind
{
    Nil,
    Boolean,
    Number,
    String
}

public readonly record struct LuauConfigValue(LuauConfigValueKind Kind, bool Boolean = false, double Number = 0, string? String = null)
{
    public static LuauConfigValue From(bool value) => new(LuauConfigValueKind.Boolean, Boolean: value);
    public static LuauConfigValue From(double value) => new(LuauConfigValueKind.Number, Number: value);
    public static LuauConfigValue From(string value) => new(LuauConfigValueKind.String, String: value);
}

/// <summary>Typed FFI boundary for client-owned, persistent game configuration.</summary>
public static unsafe class LuauConfigHost
{
    public static Func<string, LuauConfigValue>? Get;
    public static Func<string, LuauConfigValue, bool>? Set;
    public static Func<string, IReadOnlyList<LuauConfigValue>?>? Options;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 3);
        Add(l, "get", &GetClosure);
        Add(l, "set", &SetClosure);
        Add(l, "options", &OptionsClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__Config");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__Config." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GetClosure(IntPtr l)
    {
        string? key = ReadString(l, 1);
        LuauConfigValue value = default;
        try
        {
            if (key != null) value = Get?.Invoke(key) ?? default;
        }
        catch
        {
            // No managed exception may unwind through an UnmanagedCallersOnly frame.
        }
        Push(l, value);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetClosure(IntPtr l)
    {
        string? key = ReadString(l, 1);
        LuauConfigValue value = ReadValue(l, 2, ReadString(l, 3));
        bool success = false;
        try
        {
            success = key != null && Set?.Invoke(key, value) == true;
        }
        catch
        {
            // No managed exception may unwind through an UnmanagedCallersOnly frame.
        }
        LuauNative.lua_pushboolean(l, success ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OptionsClosure(IntPtr l)
    {
        IReadOnlyList<LuauConfigValue>? values = null;
        try
        {
            string? key = ReadString(l, 1);
            if (key != null) values = Options?.Invoke(key);
        }
        catch
        {
            // No managed exception may unwind through an UnmanagedCallersOnly frame.
        }

        if (values == null)
        {
            LuauNative.lua_pushnil(l);
            return 1;
        }

        LuauNative.lua_createtable(l, values.Count, 0);
        for (int i = 0; i < values.Count; i++)
        {
            Push(l, values[i]);
            LuauNative.lua_rawseti(l, -2, i + 1);
        }
        return 1;
    }

    private static LuauConfigValue ReadValue(IntPtr l, int index, string? kind)
    {
        if (kind == "boolean") return LuauConfigValue.From(LuauNative.lua_toboolean(l, index) != 0);
        if (kind == "number") return LuauConfigValue.From(LuauNative.lua_tonumberx(l, index, IntPtr.Zero));
        if (kind == "string")
        {
            string? value = ReadString(l, index);
            if (value != null) return LuauConfigValue.From(value);
        }

        return default;
    }

    private static string? ReadString(IntPtr l, int index)
    {
        IntPtr pointer = LuauNative.lua_tolstring(l, index, out nuint length);
        return pointer == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)pointer, (int)length);
    }

    private static void Push(IntPtr l, LuauConfigValue value)
    {
        switch (value.Kind)
        {
            case LuauConfigValueKind.Boolean: LuauNative.lua_pushboolean(l, value.Boolean ? 1 : 0); break;
            case LuauConfigValueKind.Number: LuauNative.lua_pushnumber(l, value.Number); break;
            case LuauConfigValueKind.String: LuauNative.lua_pushstring(l, value.String ?? string.Empty); break;
            default: LuauNative.lua_pushnil(l); break;
        }
    }

    public const string Bootstrap = """
local config = { options = function(key) return __Config.options(key) end }
OMNI.config = setmetatable(config, {
    __index = function(_, key) return __Config.get(key) end,
    __newindex = function(_, key, value)
        if not __Config.set(key, value, typeof(value)) then error("invalid configuration key or value: " .. key, 2) end
    end
})
""";
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

/// <summary>Generic Luau facade for a client-owned DOM handle table.</summary>
public static unsafe class LuauDomHost
{
    public static Func<string, int>? Query;
    public static Func<int, int>? Parent;
    public static Func<int, int>? ChildCount;
    public static Func<int, int, int>? Child;
    public static Func<int, string, string?>? GetString;
    public static Func<int, string, string, bool>? SetString;
    public static Func<int, string, bool?>? GetBool;
    public static Func<int, string, bool, bool>? SetBool;
    public static Func<int, bool>? Click;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 9);
        Add(l, "query", &QueryClosure);
        Add(l, "parent", &ParentClosure);
        Add(l, "childCount", &ChildCountClosure);
        Add(l, "child", &ChildClosure);
        Add(l, "getString", &GetStringClosure);
        Add(l, "setString", &SetStringClosure);
        Add(l, "getBool", &GetBoolClosure);
        Add(l, "setBool", &SetBoolClosure);
        Add(l, "click", &ClickClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__Dom");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__Dom." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    private static string? ReadString(IntPtr l, int index)
    {
        IntPtr pointer = LuauNative.lua_tolstring(l, index, out nuint length);
        return pointer == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)pointer, (int)length);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int QueryClosure(IntPtr l)
    {
        string? selector = ReadString(l, 1);
        LuauNative.lua_pushinteger(l, selector == null ? 0 : Query?.Invoke(selector) ?? 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ParentClosure(IntPtr l) { LuauNative.lua_pushinteger(l, Parent?.Invoke(LuauNative.luaL_checkinteger(l, 1)) ?? 0); return 1; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ChildCountClosure(IntPtr l) { LuauNative.lua_pushinteger(l, ChildCount?.Invoke(LuauNative.luaL_checkinteger(l, 1)) ?? 0); return 1; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ChildClosure(IntPtr l)
    {
        LuauNative.lua_pushinteger(l, Child?.Invoke(LuauNative.luaL_checkinteger(l, 1), LuauNative.luaL_checkinteger(l, 2)) ?? 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GetStringClosure(IntPtr l)
    {
        string? property = ReadString(l, 2);
        string? value = property == null ? null : GetString?.Invoke(LuauNative.luaL_checkinteger(l, 1), property);
        if (value == null) LuauNative.lua_pushnil(l); else LuauNative.lua_pushstring(l, value);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetStringClosure(IntPtr l)
    {
        string? property = ReadString(l, 2);
        string? value = ReadString(l, 3);
        bool success = property != null && value != null && SetString?.Invoke(LuauNative.luaL_checkinteger(l, 1), property, value) == true;
        LuauNative.lua_pushboolean(l, success ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GetBoolClosure(IntPtr l)
    {
        string? property = ReadString(l, 2);
        bool? value = property == null ? null : GetBool?.Invoke(LuauNative.luaL_checkinteger(l, 1), property);
        if (value == null) LuauNative.lua_pushnil(l); else LuauNative.lua_pushboolean(l, value.Value ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetBoolClosure(IntPtr l)
    {
        string? property = ReadString(l, 2);
        bool success = property != null && SetBool?.Invoke(LuauNative.luaL_checkinteger(l, 1), property, LuauNative.lua_toboolean(l, 3) != 0) == true;
        LuauNative.lua_pushboolean(l, success ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ClickClosure(IntPtr l)
    {
        bool success;
        try
        {
            success = Click?.Invoke(LuauNative.luaL_checkinteger(l, 1)) == true;
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            success = false;
        }

        LuauNative.lua_pushboolean(l, success ? 1 : 0);
        return 1;
    }

    public const string Bootstrap = """
local Node = {}
local function wrap(handle)
    if handle == 0 then return nil end
    return setmetatable({ __handle = handle }, Node)
end
Node.__index = function(self, key)
    if key == "parent" then return wrap(__Dom.parent(self.__handle)) end
    if key == "childCount" then return __Dom.childCount(self.__handle) end
    if key == "type" or key == "id" or key == "text" then return __Dom.getString(self.__handle, key) end
    if key == "visible" or key == "enabled" or key == "hitTestVisible" then return __Dom.getBool(self.__handle, key) end
    return Node[key]
end
Node.__newindex = function(self, key, value)
    if key == "text" then __Dom.setString(self.__handle, key, value); return end
    if key == "visible" or key == "enabled" or key == "hitTestVisible" then __Dom.setBool(self.__handle, key, value); return end
    rawset(self, key, value)
end
function Node:child(index) return wrap(__Dom.child(self.__handle, index - 1)) end
function Node:click() return __Dom.click(self.__handle) end
local ui = setmetatable({ querySelector = function(selector) return wrap(__Dom.query(selector)) end }, {
    __index = function(_, key)
        if key == "root" then return wrap(__Dom.query("#root")) end
        if key == "hud" then return wrap(__Dom.query("#hud")) end
    end
})
OMNI = {
    environment = "client",
    has = function(capability)
        return capability == "ui" or capability == "config" or capability == "worlds"
    end,
    ui = ui,
}
""";
}

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

/// <summary>Overrides Luau's stdout-oriented <c>print</c> with a host logging callback.</summary>
public static unsafe class LuauLogHost
{
    public static Action<string>? WriteLine;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_pushcclosurek(l, &PrintClosure, "print", 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "print");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PrintClosure(IntPtr l)
    {
        var argumentCount = LuauNative.lua_gettop(l);
        StringBuilder line = new();

        for (var index = 1; index <= argumentCount; index++)
        {
            var pointer = LuauNative.luaL_tolstring(l, index, out var length);
            if (index > 1)
            {
                line.Append('\t');
            }

            if (pointer != IntPtr.Zero)
            {
                line.Append(Encoding.UTF8.GetString((byte*)pointer, (int)length));
            }

            // luaL_tolstring pushed one temporary result above the original arguments.
            LuauNative.lua_settop(l, -2);
        }

        WriteLine?.Invoke(line.ToString());
        return 0;
    }
}

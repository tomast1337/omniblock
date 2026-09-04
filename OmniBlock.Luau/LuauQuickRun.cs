using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau;

/// <summary>
///     Public entry point for one-shot, ephemeral Luau script execution: boot a fresh
///     <c>lua_State</c>, compile+load+run the given source, describe whatever it left on the
///     stack as text, and close the state — no VM lifetime survives past a single
///     <see cref="TryExecute" /> call. Retained as a standalone one-shot execution utility and
///     native-library availability probe; the unified client console uses persistent
///     <see cref="LuauState" /> execution instead.
///     <see cref="LuauNative" />/<see cref="LuauCallbacks" /> stay internal; this is the one
///     public surface callers outside this assembly should use.
///     Each VM has Luau's standard library loaded (<see cref="LuauNative.luaL_openlibs" />) —
///     <c>string</c>/<c>math</c>/<c>table</c>/<c>error</c>/<c>tostring</c> etc. all work — but
///     <c>print</c>/<c>io.write</c> are not redirected anywhere: their output goes wherever
///     Luau's default <c>lua_writestring</c> sends it (the process's native stdout), not into
///     the debug console's own log. Use <c>return</c> to see a value in the console; capturing
///     print output would need a custom callback and is Host API-shaped work, not done here.
/// </summary>
public static class LuauQuickRun
{
    /// <summary>
    ///     Whether <c>omniblock_luau</c> is resolvable right now. Must be checked before
    ///     <see cref="TryExecute" /> is ever called from a long-running host like a debug UI —
    ///     touching any static member of <see cref="LuauNative" /> for the first time runs its
    ///     static constructor, which eagerly loads the native library and throws if it's
    ///     missing; once that throw happens, every later access to <see cref="LuauNative" /> in
    ///     the process fails too (CLR type-initialization failure is permanent), so callers
    ///     must never reach <see cref="TryExecute" /> without checking this first. Mirrors the
    ///     same bare-name-then-sibling-file probe duplicated in
    ///     OmniBlock.Tests/Luau/LuauInterruptIntegrationTests.cs and
    ///     OmniBlock.Tests/Luau/LuauExecutionTests.cs, for the same reason: this check itself
    ///     must not reference <see cref="LuauNative" />.
    /// </summary>
    public static bool IsAvailable()
    {
        const string libraryName = "omniblock_luau";

        if (NativeLibrary.TryLoad(libraryName, out _))
        {
            return true;
        }

        var fileName = OperatingSystem.IsWindows() ? "omniblock_luau.dll"
            : OperatingSystem.IsMacOS() ? "libomniblock_luau.dylib"
            : "libomniblock_luau.so";
        return NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, fileName), out _);
    }

    /// <summary>
    ///     Compiles, loads, and runs <paramref name="source" /> in a fresh VM. On success,
    ///     <paramref name="output" /> is every value the script left on the stack, formatted
    ///     for display and comma-joined (or <c>"(no return value)"</c> if it left none). On
    ///     failure — compile error, load error, or a runtime error/exception from the script
    ///     itself — <paramref name="output" /> is the error message instead, and this returns
    ///     <see langword="false" />.
    /// </summary>
    /// <remarks>
    ///     Caller must have already confirmed <see cref="IsAvailable" />. This method still
    ///     wraps its body in a broad try/catch and reports failures through the return value
    ///     rather than throwing: <paramref name="source" /> is arbitrary debug-user input
    ///     crossing an FFI boundary, a system boundary in the sense the project's error-handling
    ///     convention already carves out an exception for — a debug console must never take the
    ///     client process down because someone fat-fingered a script.
    /// </remarks>
    public static bool TryExecute(string source, out string output)
    {
        try
        {
            return TryExecuteCore(source, out output);
        }
        catch (Exception ex)
        {
            output = $"internal error: {ex.Message}";
            return false;
        }
    }

    private static unsafe bool TryExecuteCore(string source, out string output)
    {
        var allocFn = (IntPtr)(delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*>)&LuauCallbacks.Allocate;
        var L = LuauNative.lua_newstate(allocFn, IntPtr.Zero);
        if (L == IntPtr.Zero)
        {
            output = "lua_newstate failed";
            return false;
        }

        try
        {
            LuauNative.luaL_openlibs(L);


            var sourceBytes = Encoding.UTF8.GetBytes(source);
            byte* bytecode;
            nuint bytecodeSize;
            fixed (byte* sourcePtr = sourceBytes)
            {
                bytecode = LuauNative.luau_compile(sourcePtr, (nuint)sourceBytes.Length, IntPtr.Zero, &bytecodeSize);
            }

            if (bytecode == null)
            {
                output = "compile failed: no bytecode produced";
                return false;
            }

            var loadResult = LuauNative.luau_load(L, "=console", bytecode, bytecodeSize, 0);
            if (loadResult != 0)
            {
                output = DescribeValue(L, -1);
                return false;
            }

            var pcallResult = LuauNative.lua_pcall(L, 0, -1, 0);
            if (pcallResult != 0)
            {
                output = DescribeValue(L, -1);
                return false;
            }

            output = DescribeResults(L);
            return true;
        }
        finally
        {
            LuauNative.lua_close(L);
        }
    }

    private static unsafe string DescribeResults(IntPtr L)
    {
        var top = LuauNative.lua_gettop(L);
        if (top == 0)
        {
            return "(no return value)";
        }

        var values = new string[top];
        for (var i = 0; i < top; i++)
        {
            values[i] = DescribeValue(L, i + 1);
        }

        return string.Join(", ", values);
    }

    private static unsafe string DescribeValue(IntPtr L, int idx)
    {
        var type = LuauNative.lua_type(L, idx);

        if (type == 0) // LUA_TNIL — see lua_type's binding comment for why this is safe to hardcode.
        {
            return "nil";
        }

        if (type == 1) // LUA_TBOOLEAN — same guarantee.
        {
            return LuauNative.lua_toboolean(L, idx) != 0 ? "true" : "false";
        }

        if (LuauNative.lua_isstring(L, idx) != 0)
        {
            var strPtr = LuauNative.lua_tolstring(L, idx, out var len);
            return strPtr == IntPtr.Zero ? string.Empty : Encoding.UTF8.GetString((byte*)strPtr, (int)len);
        }

        var namePtr = LuauNative.lua_typename(L, type);
        var typeName = namePtr == IntPtr.Zero ? "unknown" : Marshal.PtrToStringUTF8(namePtr) ?? "unknown";
        return $"<{typeName}>";
    }
}

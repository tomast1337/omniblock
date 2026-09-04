using System.Runtime.InteropServices;
using System.Text;
using OmniBlock.Luau;

namespace OmniBlock.Tests.Luau;

/// <summary>
///     Companion to <see cref="LuauInterruptIntegrationTests" />: that test proves a runaway
///     script gets halted; this one proves the ordinary case still works end to end — compile,
///     load, call, and read a result back off the C stack — with no managed allocation in the
///     execution/retrieval phase itself (no boxing, no marshalled strings, only
///     <c>delegate*</c> calls and raw pointers). Same native-library requirement and skip-guard
///     shape as <see cref="LuauInterruptIntegrationTests" />: requires native/luau/build-local.sh
///     to have been run for this checkout.
/// </summary>
public sealed unsafe class LuauExecutionTests
{
    [SkippableFact]
    public void Execute_simpleArithmetic_leavesResultOnTheStack()
    {
        // Same reasoning as LuauInterruptIntegrationTests.IsNativeLibraryAvailable: must not
        // touch LuauNative before this check, since referencing any static member of it runs
        // its static constructor, which eagerly NativeLibrary.Loads and throws if missing.
        Skip.IfNot(IsNativeLibraryAvailable(),
            "native/luau/build-local.sh hasn't been run for this checkout — omniblock_luau isn't resolvable.");

        var allocFn = (IntPtr)(delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*>)&LuauCallbacks.Allocate;
        var L = LuauNative.lua_newstate(allocFn, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, L);

        try
        {
            var source = Encoding.UTF8.GetBytes("return 10 + 32");
            byte* bytecode;
            nuint bytecodeSize;
            fixed (byte* sourcePtr = source)
            {
                bytecode = LuauNative.luau_compile(sourcePtr, (nuint)source.Length, IntPtr.Zero, &bytecodeSize);
            }

            Assert.True(bytecode != null);

            var loadResult = LuauNative.luau_load(L, "=execution_test", bytecode, bytecodeSize, 0);
            Assert.Equal(0, loadResult);

            var pcallResult = LuauNative.lua_pcall(L, 0, -1, 0);
            Assert.Equal(0, pcallResult);

            Assert.Equal(1, LuauNative.lua_gettop(L));
            Assert.Equal(42, LuauNative.lua_tointegerx(L, -1, IntPtr.Zero));
        }
        finally
        {
            LuauNative.lua_close(L);
        }
    }

    private static bool IsNativeLibraryAvailable()
    {
        if (NativeLibrary.TryLoad("omniblock_luau", out _))
        {
            return true;
        }

        var fileName = OperatingSystem.IsWindows() ? "omniblock_luau.dll"
            : OperatingSystem.IsMacOS() ? "libomniblock_luau.dylib"
            : "libomniblock_luau.so";
        return NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, fileName), out _);
    }
}

using System.IO;
using System.Runtime.InteropServices;
using OmniBlock.Luau;

namespace OmniBlock.Tests.Luau;

/// <summary>
///     Drives a real <c>lua_State</c> through the actual <c>omniblock_luau</c> shared library to
///     prove <see cref="LuauCallbacks.Interrupt" /> genuinely halts a script, not just that its
///     pointer-decrement logic looks right in isolation. Requires
///     native/luau/build-local.sh to have been run for this checkout (see
///     docs/luau-ffi-embedding-plan.md Part 1.5) — skipped, not failed, when the native library
///     isn't resolvable, since CI/a fresh checkout won't have it until the RID-packaged NuGet
///     (Part 1.4) exists.
/// </summary>
public sealed unsafe class LuauInterruptIntegrationTests
{
    [SkippableFact]
    public void Interrupt_haltsAnInfiniteLoopOnceTheInstructionBudgetIsExhausted()
    {
        // Checked before anything below touches LuauNative — referencing that type runs its
        // static constructor, which eagerly NativeLibrary.Loads omniblock_luau and throws if
        // it's missing. This probe must resolve independently, first, without going through
        // LuauNative's own DllImportResolver (that resolver only applies to P/Invoke calls
        // made *from* LuauNative's assembly — a standalone NativeLibrary.TryLoad call here
        // doesn't trigger it), so it mirrors the same bare-name-then-sibling-file fallback by
        // hand.
        Skip.IfNot(IsNativeLibraryAvailable(),
            "native/luau/build-local.sh hasn't been run for this checkout — omniblock_luau isn't resolvable.");

        IntPtr allocFn = (IntPtr)(delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*>)&LuauCallbacks.Allocate;
        IntPtr L = LuauNative.lua_newstate(allocFn, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, L);

        long* budget = null;
        try
        {
            IntPtr callbacks = LuauNative.lua_callbacks(L);
            Assert.NotEqual(IntPtr.Zero, callbacks);

            // lua_Callbacks { void* userdata; void(*interrupt)(lua_State*, int); ... } —
            // interrupt is the second pointer-sized field, right after userdata.
            IntPtr interruptFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, void>)&LuauCallbacks.Interrupt;
            Marshal.WriteIntPtr(callbacks, IntPtr.Size, interruptFn);

            budget = (long*)NativeMemory.Alloc((nuint)sizeof(long));
            *budget = 1000;
            LuauNative.lua_setthreaddata(L, (IntPtr)budget);

            byte[] source = System.Text.Encoding.UTF8.GetBytes("while true do end");
            byte* bytecode;
            nuint bytecodeSize;
            fixed (byte* sourcePtr = source)
            {
                bytecode = LuauNative.luau_compile(sourcePtr, (nuint)source.Length, IntPtr.Zero, &bytecodeSize);
            }

            Assert.True(bytecode != null);

            int loadResult = LuauNative.luau_load(L, "=budget_test", bytecode, bytecodeSize, 0);
            Assert.Equal(0, loadResult);

            int pcallResult = LuauNative.lua_pcall(L, 0, 0, 0);

            // A real infinite loop: the only way lua_pcall returns at all is Interrupt firing
            // and raising the budget-exceeded error, not the script finishing on its own.
            Assert.NotEqual(0, pcallResult);
            Assert.True(*budget <= 0);
        }
        finally
        {
            LuauNative.lua_close(L);
            if (budget != null)
            {
                NativeMemory.Free(budget);
            }
        }
    }

    private static bool IsNativeLibraryAvailable()
    {
        if (NativeLibrary.TryLoad("omniblock_luau", out _))
        {
            return true;
        }

        string fileName = OperatingSystem.IsWindows() ? "omniblock_luau.dll"
            : OperatingSystem.IsMacOS() ? "libomniblock_luau.dylib"
            : "libomniblock_luau.so";
        return NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, fileName), out _);
    }
}

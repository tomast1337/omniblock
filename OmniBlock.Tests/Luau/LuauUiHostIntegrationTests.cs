using System.IO;
using System.Runtime.InteropServices;
using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

/// <summary>
///     Drives a real <c>lua_State</c> — via <see cref="LuauState" />, not a hand-rolled
///     <c>lua_newstate</c> like <see cref="LuauInterruptIntegrationTests" /> — through the actual
///     <c>omniblock_luau</c> shared library to prove <see cref="LuauUiHost" />'s FFI boundary
///     genuinely round-trips: a script's <c>Host.openScreen(id, a, b, c)</c> call reaches
///     <see cref="LuauUiHost.Dispatch" /> with the same four integers, not just that the argument-
///     marshalling code reads right in isolation. Same native-library requirement as every other
///     test in this directory — skipped, not failed, when <c>omniblock_luau</c> isn't resolvable
///     (native/luau/build-local.sh hasn't been run for this checkout).
///     <para>
///         Runs execution against <see cref="LuauState.Handle" /> by hand, the same low-level
///         pattern <see cref="LuauStateTests" /> already uses — <see cref="LuauState" />
///         deliberately exposes no compile/load/pcall surface, since driving execution is Host
///         API work, which is exactly what this test is exercising.
///     </para>
/// </summary>
public sealed unsafe class LuauUiHostIntegrationTests
{
    [SkippableFact]
    public void OpenScreen_calledFromLuau_dispatchesWithTheSameFourIntegers()
    {
        Skip.IfNot(IsNativeLibraryAvailable(),
            "native/luau/build-local.sh hasn't been run for this checkout — omniblock_luau isn't resolvable.");

        using LuauState state = new();
        state.ResetInstructionBudget(10_000);

        int invocationCount = 0;
        (int CommandId, int Arg0, int Arg1, int Arg2)? received = null;

        LuauUiHost.Dispatch = (commandId, arg0, arg1, arg2) =>
        {
            invocationCount++;
            received = (commandId, arg0, arg1, arg2);
        };

        try
        {
            LuauUiHost.Install(state.Handle);

            byte[] source = System.Text.Encoding.UTF8.GetBytes("Host.openScreen(42, 1, 2, 3)");
            byte* bytecode;
            nuint bytecodeSize;
            fixed (byte* sourcePtr = source)
            {
                bytecode = LuauNative.luau_compile(sourcePtr, (nuint)source.Length, IntPtr.Zero, &bytecodeSize);
            }

            Assert.True(bytecode != null);

            int loadResult = LuauNative.luau_load(state.Handle, "=ui_host_test", bytecode, bytecodeSize, 0);
            Assert.Equal(0, loadResult);

            int pcallResult = LuauNative.lua_pcall(state.Handle, 0, 0, 0);

            Assert.Equal(0, pcallResult);
            Assert.Equal(1, invocationCount);
            Assert.Equal((42, 1, 2, 3), received);
        }
        finally
        {
            LuauUiHost.Dispatch = null;
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

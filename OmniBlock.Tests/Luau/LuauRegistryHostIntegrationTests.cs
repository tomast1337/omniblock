using System.IO;
using System.Runtime.InteropServices;
using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

/// <summary>
///     Drives a real <c>lua_State</c> — via <see cref="LuauState" />, same pattern
///     <see cref="LuauUiHostIntegrationTests" /> already uses — to prove
///     <see cref="LuauRegistryHost" />'s FFI boundary round-trips: a script's
///     <c>Registry.registerUi(name)</c> call reaches <see cref="LuauRegistryHost.RegisterUi" />
///     with the exact string it wrote, and the integer that comes back is usable Luau-side.
///     Skipped, not failed, when <c>omniblock_luau</c> isn't resolvable (native/luau/build-local.sh
///     hasn't been run for this checkout), same as every other test in this directory.
/// </summary>
public sealed unsafe class LuauRegistryHostIntegrationTests
{
    [SkippableFact]
    public void RegisterUi_calledFromLuau_receivesTheStringAndReturnsAnId()
    {
        Skip.IfNot(IsNativeLibraryAvailable(),
            "native/luau/build-local.sh hasn't been run for this checkout — omniblock_luau isn't resolvable.");

        using LuauState state = new();
        state.ResetInstructionBudget(10_000);

        List<string> received = [];
        LuauRegistryHost.RegisterUi = (string name, out int id) =>
        {
            received.Add(name);
            id = received.Count - 1; // mirrors UiCommandRegistry's real call-order assignment
            return true;
        };

        try
        {
            LuauRegistryHost.Install(state.Handle);

            byte[] source = System.Text.Encoding.UTF8.GetBytes(
                "local id = Registry.registerUi(\"omniblock:inventory.open\") return id");
            byte* bytecode;
            nuint bytecodeSize;
            fixed (byte* sourcePtr = source)
            {
                bytecode = LuauNative.luau_compile(sourcePtr, (nuint)source.Length, IntPtr.Zero, &bytecodeSize);
            }

            Assert.True(bytecode != null);

            int loadResult = LuauNative.luau_load(state.Handle, "=registry_host_test", bytecode, bytecodeSize, 0);
            Assert.Equal(0, loadResult);

            int pcallResult = LuauNative.lua_pcall(state.Handle, 0, -1, 0);

            Assert.Equal(0, pcallResult);
            Assert.Equal(["omniblock:inventory.open"], received);
            Assert.Equal(0, LuauNative.lua_tointegerx(state.Handle, -1, IntPtr.Zero));
        }
        finally
        {
            LuauRegistryHost.RegisterUi = null;
        }
    }

    [SkippableFact]
    public void RegisterUi_calledTwiceFromLuau_returnsDistinctIdsInCallOrder()
    {
        Skip.IfNot(IsNativeLibraryAvailable(),
            "native/luau/build-local.sh hasn't been run for this checkout — omniblock_luau isn't resolvable.");

        using LuauState state = new();
        state.ResetInstructionBudget(10_000);

        List<string> received = [];
        LuauRegistryHost.RegisterUi = (string name, out int id) =>
        {
            received.Add(name);
            id = received.Count - 1;
            return true;
        };

        try
        {
            LuauRegistryHost.Install(state.Handle);

            byte[] source = System.Text.Encoding.UTF8.GetBytes(
                """
                local a = Registry.registerUi("omniblock:inventory.open")
                local b = Registry.registerUi("omniblock:debug.toast")
                return a, b
                """);
            byte* bytecode;
            nuint bytecodeSize;
            fixed (byte* sourcePtr = source)
            {
                bytecode = LuauNative.luau_compile(sourcePtr, (nuint)source.Length, IntPtr.Zero, &bytecodeSize);
            }

            Assert.True(bytecode != null);

            int loadResult = LuauNative.luau_load(state.Handle, "=registry_host_order_test", bytecode, bytecodeSize, 0);
            Assert.Equal(0, loadResult);

            int pcallResult = LuauNative.lua_pcall(state.Handle, 0, -1, 0);

            Assert.Equal(0, pcallResult);
            Assert.Equal(["omniblock:inventory.open", "omniblock:debug.toast"], received);
            Assert.Equal(0, LuauNative.lua_tointegerx(state.Handle, -2, IntPtr.Zero));
            Assert.Equal(1, LuauNative.lua_tointegerx(state.Handle, -1, IntPtr.Zero));
        }
        finally
        {
            LuauRegistryHost.RegisterUi = null;
        }
    }

    [SkippableFact]
    public void RegisterUi_whenDelegateSignalsFrozen_errorsRatherThanReturning()
    {
        Skip.IfNot(IsNativeLibraryAvailable(),
            "native/luau/build-local.sh hasn't been run for this checkout — omniblock_luau isn't resolvable.");

        using LuauState state = new();
        state.ResetInstructionBudget(10_000);

        LuauRegistryHost.RegisterUi = (string _, out int id) =>
        {
            id = -1;
            return false; // the exact contract RegisterUiClosure treats as "reject, luaL_error".
        };

        try
        {
            LuauRegistryHost.Install(state.Handle);

            byte[] source = System.Text.Encoding.UTF8.GetBytes(
                "Registry.registerUi(\"omniblock:too.late\")");
            byte* bytecode;
            nuint bytecodeSize;
            fixed (byte* sourcePtr = source)
            {
                bytecode = LuauNative.luau_compile(sourcePtr, (nuint)source.Length, IntPtr.Zero, &bytecodeSize);
            }

            Assert.True(bytecode != null);

            int loadResult = LuauNative.luau_load(state.Handle, "=registry_host_frozen_test", bytecode, bytecodeSize, 0);
            Assert.Equal(0, loadResult);

            int pcallResult = LuauNative.lua_pcall(state.Handle, 0, 0, 0);

            // Non-zero pcall result is luaL_errorL's longjmp landing back here — the same signal
            // LuauInterruptIntegrationTests uses to prove Interrupt actually halts a script,
            // applied here to a rejected registerUi call instead of a budget exhaustion. The VM
            // itself survives (LuauState's whole point, per LuauStateTests), so a second, valid
            // call on the same state still works afterward.
            Assert.NotEqual(0, pcallResult);

            LuauRegistryHost.RegisterUi = (string _, out int id) =>
            {
                id = 7;
                return true;
            };

            byte[] source2 = System.Text.Encoding.UTF8.GetBytes(
                "return Registry.registerUi(\"omniblock:still.works\")");
            byte* bytecode2;
            nuint bytecodeSize2;
            fixed (byte* sourcePtr = source2)
            {
                bytecode2 = LuauNative.luau_compile(sourcePtr, (nuint)source2.Length, IntPtr.Zero, &bytecodeSize2);
            }

            int loadResult2 = LuauNative.luau_load(state.Handle, "=registry_host_recovery_test", bytecode2, bytecodeSize2, 0);
            Assert.Equal(0, loadResult2);
            int pcallResult2 = LuauNative.lua_pcall(state.Handle, 0, -1, 0);

            Assert.Equal(0, pcallResult2);
            Assert.Equal(7, LuauNative.lua_tointegerx(state.Handle, -1, IntPtr.Zero));
        }
        finally
        {
            LuauRegistryHost.RegisterUi = null;
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

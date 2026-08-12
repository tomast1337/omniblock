using System.IO;
using System.Runtime.InteropServices;
using OmniBlock.Luau;

namespace OmniBlock.Tests.Luau;

/// <summary>
///     Covers <see cref="LuauState" />'s lifecycle guarantees from
///     docs/luau-persistent-lifecycle-plan.md: construction/disposal, os pruning, the
///     zero-initialized fail-closed instruction budget, and the budget/GC-step primitives.
///     Same native-library requirement as the other Luau integration tests. Since
///     <see cref="LuauState" /> deliberately exposes no compile/load/pcall surface (that's Host
///     API work), tests that need to run a script against the persistent state drive
///     <see cref="LuauNative" /> directly against <see cref="LuauState.Handle" /> — the same
///     low-level pattern <see cref="LuauInterruptIntegrationTests" /> already uses, just against
///     a state this class owns instead of one the test creates by hand.
/// </summary>
public sealed unsafe class LuauStateTests
{
    [SkippableFact]
    public void Construct_and_dispose_doesNotThrow()
    {
        Skip.IfNot(IsNativeLibraryAvailable(), "native library not resolvable for this checkout.");

        using LuauState state = new();

        Assert.NotEqual(IntPtr.Zero, state.Handle);
    }

    [SkippableFact]
    public void Dispose_calledTwice_isIdempotent()
    {
        Skip.IfNot(IsNativeLibraryAvailable(), "native library not resolvable for this checkout.");

        LuauState state = new();
        state.Dispose();
        state.Dispose();
    }

    [SkippableFact]
    public void Members_afterDispose_throwObjectDisposed()
    {
        Skip.IfNot(IsNativeLibraryAvailable(), "native library not resolvable for this checkout.");

        LuauState state = new();
        state.Dispose();

        Assert.Throws<ObjectDisposedException>(() => state.ResetInstructionBudget(1000));
        Assert.Throws<ObjectDisposedException>(() => state.StepGarbageCollector(16));
        Assert.Throws<ObjectDisposedException>(() => _ = state.HeapSizeKb);
    }

    [SkippableFact]
    public void OsLibrary_isPruned()
    {
        Skip.IfNot(IsNativeLibraryAvailable(), "native library not resolvable for this checkout.");

        using LuauState state = new();
        state.ResetInstructionBudget(10_000);

        int type = RunAndGetTopType(state.Handle, "return os");

        Assert.Equal(0, type); // LUA_TNIL — os was set to nil at construction.
    }

    [SkippableFact]
    public void StandardLibrary_otherwiseWorks()
    {
        Skip.IfNot(IsNativeLibraryAvailable(), "native library not resolvable for this checkout.");

        using LuauState state = new();
        state.ResetInstructionBudget(10_000);

        int type = RunAndGetTopType(state.Handle, "return math.floor(3.7)");

        Assert.Equal(3, LuauNative.lua_tointegerx(state.Handle, -1, IntPtr.Zero));
        Assert.NotEqual(0, type);
    }

    [SkippableFact]
    public void FreshState_withNoBudgetReset_failsClosedInsteadOfReadingGarbage()
    {
        Skip.IfNot(IsNativeLibraryAvailable(), "native library not resolvable for this checkout.");

        using LuauState state = new();
        // Deliberately no ResetInstructionBudget call — the zero-initialized counter must make
        // even a trivial script fail immediately, not run against uninitialized memory.

        byte[] source = System.Text.Encoding.UTF8.GetBytes("return 1");
        byte* bytecode;
        nuint bytecodeSize;
        fixed (byte* sourcePtr = source)
        {
            bytecode = LuauNative.luau_compile(sourcePtr, (nuint)source.Length, IntPtr.Zero, &bytecodeSize);
        }

        LuauNative.luau_load(state.Handle, "=fail_closed_test", bytecode, bytecodeSize, 0);
        int pcallResult = LuauNative.lua_pcall(state.Handle, 0, -1, 0);

        Assert.NotEqual(0, pcallResult);
    }

    [SkippableFact]
    public void ResetInstructionBudget_boundsALongRunningScript()
    {
        Skip.IfNot(IsNativeLibraryAvailable(), "native library not resolvable for this checkout.");

        using LuauState state = new();
        state.ResetInstructionBudget(1000);

        byte[] source = System.Text.Encoding.UTF8.GetBytes("while true do end");
        byte* bytecode;
        nuint bytecodeSize;
        fixed (byte* sourcePtr = source)
        {
            bytecode = LuauNative.luau_compile(sourcePtr, (nuint)source.Length, IntPtr.Zero, &bytecodeSize);
        }

        LuauNative.luau_load(state.Handle, "=budget_test", bytecode, bytecodeSize, 0);
        int pcallResult = LuauNative.lua_pcall(state.Handle, 0, -1, 0);

        Assert.NotEqual(0, pcallResult);

        // The VM itself must survive — a persistent state's whole point is outliving one
        // interrupted call, unlike LuauQuickRun's ephemeral VM which is discarded regardless.
        state.ResetInstructionBudget(10_000);
        int type = RunAndGetTopType(state.Handle, "return 42");
        Assert.NotEqual(0, type);
        Assert.Equal(42, LuauNative.lua_tointegerx(state.Handle, -1, IntPtr.Zero));
    }

    [SkippableFact]
    public void StepGarbageCollector_and_HeapSizeKb_doNotThrow()
    {
        Skip.IfNot(IsNativeLibraryAvailable(), "native library not resolvable for this checkout.");

        using LuauState state = new();
        state.ResetInstructionBudget(10_000);

        state.StepGarbageCollector(16);

        Assert.True(state.HeapSizeKb >= 0);
    }

    private static int RunAndGetTopType(IntPtr L, string source)
    {
        byte[] sourceBytes = System.Text.Encoding.UTF8.GetBytes(source);
        byte* bytecode;
        nuint bytecodeSize;
        fixed (byte* sourcePtr = sourceBytes)
        {
            bytecode = LuauNative.luau_compile(sourcePtr, (nuint)sourceBytes.Length, IntPtr.Zero, &bytecodeSize);
        }

        LuauNative.luau_load(L, "=state_test", bytecode, bytecodeSize, 0);
        int pcallResult = LuauNative.lua_pcall(L, 0, -1, 0);
        Assert.Equal(0, pcallResult);

        return LuauNative.lua_type(L, -1);
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

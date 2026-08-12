using System.Runtime.InteropServices;

namespace OmniBlock.Luau;

/// <summary>
///     Persistent Luau VM lifecycle, per docs/luau-persistent-lifecycle-plan.md: one
///     <c>lua_State</c> created once and disposed once by its owner, unlike
///     <see cref="LuauQuickRun" /> (a fresh VM per call, used by the debug console — the two
///     must never share a <c>lua_State*</c>). The plan's intended call sites are
///     <c>OmniBlock.Client/OmniBlock.cs</c>'s <c>SetupCoreSystems</c> (construct) and
///     <c>ShutdownGame</c> (dispose) — this class does not wire itself in there; that's the
///     owner's job.
///     Covers lifecycle only: creation, standard-library sandboxing, and the per-tick
///     instruction-budget/GC-pacing primitives. It does not run scripts — no compile/load/pcall
///     surface is exposed here, since driving execution against this state is Host API work
///     (docs/luau-ui-host-api-plan.md), deliberately out of scope for this class.
///     Must only ever be constructed, used, and disposed from one thread — the single-thread
///     rule docs/luau-ui-host-api-plan.md §2 established (the client has no separate render
///     thread) is what makes this safe with no locking; this class does not itself assert that
///     rule at runtime (see the plan's own open question about that guard).
/// </summary>
public sealed unsafe class LuauState : IDisposable
{
    private readonly IntPtr _state;
    private readonly long* _instructionBudget;
    private bool _disposed;

    /// <summary>
    ///     Raw <c>lua_State*</c>, for a future Host facade to push closures into during its own
    ///     setup, or to compile/load/run against. An opaque handle, not a C# object crossing the
    ///     FFI boundary — matches the IDs-over-objects boundary rule.
    /// </summary>
    public IntPtr Handle => _state;

    public LuauState()
    {
        IntPtr allocFn = (IntPtr)(delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*>)&LuauCallbacks.Allocate;
        _state = LuauNative.lua_newstate(allocFn, IntPtr.Zero);
        if (_state == IntPtr.Zero)
        {
            throw new InvalidOperationException("lua_newstate failed");
        }

        try
        {
            LuauNative.luaL_openlibs(_state);
            PruneUnsafeLibraries();

            // Zero-initialized, not just allocated: a zero counter makes Interrupt fire on the
            // very first safepoint, so a Host-phase call made before anyone calls
            // ResetInstructionBudget fails closed instead of reading uninitialized memory as a
            // budget.
            _instructionBudget = (long*)NativeMemory.AllocZeroed((nuint)sizeof(long));
            LuauNative.lua_setthreaddata(_state, (IntPtr)_instructionBudget);

            // lua_Callbacks { void* userdata; void(*interrupt)(lua_State*, int); ... } —
            // interrupt is the second pointer-sized field, right after userdata (same layout
            // LuauInterruptIntegrationTests already verified against the real VM).
            IntPtr callbacks = LuauNative.lua_callbacks(_state);
            IntPtr interruptFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, void>)&LuauCallbacks.Interrupt;
            Marshal.WriteIntPtr(callbacks, IntPtr.Size, interruptFn);
        }
        catch
        {
            LuauNative.lua_close(_state);
            throw;
        }
    }

    private void PruneUnsafeLibraries()
    {
        // os.time/os.clock/os.date etc leak host-machine information a UI script has no
        // legitimate need for. io does not exist in Luau 0.733 at all (confirmed against
        // lualib.h's luaopen_* list — no luaopen_io), so there is nothing to prune for file
        // access specifically. debug is also real and more capable than a UI script plausibly
        // needs, but whether/how much to prune it is an open question
        // (docs/luau-persistent-lifecycle-plan.md Open Questions #3) deliberately left alone
        // here rather than guessed.
        LuauNative.lua_pushnil(_state);
        LuauNative.lua_setfield(_state, LuauNative.GlobalsIndex, "os");
    }

    /// <summary>
    ///     Resets the instruction-budget counter <see cref="LuauCallbacks.Interrupt" /> reads
    ///     every VM safepoint. Call once per client tick, before any Host-phase call that tick
    ///     is allowed to run — per docs/luau-persistent-lifecycle-plan.md §3, this is a per-tick
    ///     ceiling shared across every script invocation that tick, not a per-call one. The
    ///     actual budget value is not this class's decision (plan Open Questions #1) — needs
    ///     empirical tuning by whoever drives the tick loop.
    /// </summary>
    public void ResetInstructionBudget(long budget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        *_instructionBudget = budget;
    }

    /// <summary>
    ///     Runs one explicit incremental-GC step (<c>LUA_GCSTEP</c>), sized in KB. Luau's GC
    ///     already collects automatically via allocation-paced "assists" regardless of this call
    ///     (see lua.h's own doc comment on <c>lua_GCOp</c>) — this exists to make GC work land at
    ///     one predictable point (tick start, alongside <see cref="ResetInstructionBudget" />)
    ///     instead of an unbounded assist pause landing mid-script, per
    ///     docs/luau-persistent-lifecycle-plan.md §4. Step size is not this class's decision
    ///     (plan Open Questions #2).
    /// </summary>
    public void StepGarbageCollector(int stepSizeKb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LuauNative.lua_gc(_state, LuauNative.LUA_GCSTEP, stepSizeKb);
    }

    /// <summary>Live heap size in KB (<c>LUA_GCCOUNT</c>) — for debug telemetry, not required for correct operation.</summary>
    public int HeapSizeKb
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return LuauNative.lua_gc(_state, LuauNative.LUA_GCCOUNT, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LuauNative.lua_close(_state);
        NativeMemory.Free(_instructionBudget);
    }
}

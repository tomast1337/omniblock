using System.Runtime.InteropServices;
using System.Text;

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
    private readonly long* _instructionBudget;
    private bool _disposed;

    public LuauState()
    {
        var allocFn = (IntPtr)(delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*>)&LuauCallbacks.Allocate;
        Handle = LuauNative.lua_newstate(allocFn, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("lua_newstate failed");
        }

        try
        {
            LuauNative.luaL_openlibs(Handle);
            PruneUnsafeLibraries();

            // Zero-initialized, not just allocated: a zero counter makes Interrupt fire on the
            // very first safepoint, so a Host-phase call made before anyone calls
            // ResetInstructionBudget fails closed instead of reading uninitialized memory as a
            // budget.
            _instructionBudget = (long*)NativeMemory.AllocZeroed(sizeof(long));
            LuauNative.lua_setthreaddata(Handle, (IntPtr)_instructionBudget);

            // lua_Callbacks { void* userdata; void(*interrupt)(lua_State*, int); ... } —
            // interrupt is the second pointer-sized field, right after userdata (same layout
            // LuauInterruptIntegrationTests already verified against the real VM).
            var callbacks = LuauNative.lua_callbacks(Handle);
            var interruptFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, void>)&LuauCallbacks.Interrupt;
            Marshal.WriteIntPtr(callbacks, IntPtr.Size, interruptFn);

            // lua_Callbacks.userthread is the fourth pointer (after userdata, interrupt and
            // panic). Propagate this VM's shared budget into coroutines created by OMNI.run.
            var userThreadFn = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&LuauCallbacks.UserThread;
            Marshal.WriteIntPtr(callbacks, IntPtr.Size * 3, userThreadFn);
        }
        catch
        {
            LuauNative.lua_close(Handle);
            throw;
        }
    }

    /// <summary>
    ///     Raw <c>lua_State*</c>, for a future Host facade to push closures into during its own
    ///     setup, or to compile/load/run against. An opaque handle, not a C# object crossing the
    ///     FFI boundary — matches the IDs-over-objects boundary rule.
    /// </summary>
    public IntPtr Handle { get; }

    /// <summary>Live heap size in KB (<c>LUA_GCCOUNT</c>) — for debug telemetry, not required for correct operation.</summary>
    public int HeapSizeKb
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return LuauNative.lua_gc(Handle, LuauNative.LUA_GCCOUNT, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LuauNative.lua_close(Handle);
        NativeMemory.Free(_instructionBudget);
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
        LuauNative.lua_pushnil(Handle);
        LuauNative.lua_setfield(Handle, LuauNative.GlobalsIndex, "os");
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
        LuauNative.lua_gc(Handle, LuauNative.LUA_GCSTEP, stepSizeKb);
    }

    /// <summary>
    ///     Evaluates one console submission in this persistent VM. Global variables and functions
    ///     survive subsequent calls; temporary return values and errors are removed from the stack.
    ///     A bare expression is accepted as REPL shorthand for <c>return expression</c>.
    /// </summary>
    public bool TryExecute(string source, out string output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            LuauNative.lua_settop(Handle, 0);
            if (TryExecuteSource("return " + source, out output, out var expressionLoaded))
            {
                return true;
            }

            if (expressionLoaded)
            {
                return false;
            }

            // A failed expression parse leaves an error object on the stack. Statements such as
            // assignments and function declarations get a second compile attempt without the REPL
            // expression wrapper.
            LuauNative.lua_settop(Handle, 0);
            return TryExecuteSource(source, out output, out _);
        }
        catch (Exception ex)
        {
            output = $"internal error: {ex.Message}";
            return false;
        }
        finally
        {
            LuauNative.lua_settop(Handle, 0);
        }
    }

    /// <summary>
    ///     Calls a previously installed global function with one numeric argument. Unlike
    ///     <see cref="TryExecute" />, this performs no compilation and is suitable for the
    ///     scheduler's once-per-client-tick dispatch.
    /// </summary>
    public bool TryCallGlobal(string functionName, double argument, out string error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            LuauNative.lua_settop(Handle, 0);
            LuauNative.lua_getfield(Handle, LuauNative.GlobalsIndex, functionName);
            LuauNative.lua_pushnumber(Handle, argument);
            if (LuauNative.lua_pcall(Handle, 1, 0, 0) != 0)
            {
                error = DescribeValue(-1);
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"internal error: {ex.Message}";
            return false;
        }
        finally
        {
            LuauNative.lua_settop(Handle, 0);
        }
    }

    private bool TryExecuteSource(string source, out string output, out bool loaded)
    {
        loaded = false;
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

        try
        {
            if (LuauNative.luau_load(Handle, "=console", bytecode, bytecodeSize, 0) != 0)
            {
                output = DescribeValue(-1);
                return false;
            }
        }
        finally
        {
            LuauNative.omniblock_luau_free(bytecode);
        }

        loaded = true;
        if (LuauNative.lua_pcall(Handle, 0, -1, 0) != 0)
        {
            output = DescribeValue(-1);
            return false;
        }

        var top = LuauNative.lua_gettop(Handle);
        if (top == 0)
        {
            output = "(no return value)";
            return true;
        }

        var values = new string[top];
        for (var i = 0; i < top; i++)
        {
            values[i] = DescribeValue(i + 1);
        }

        output = string.Join(", ", values);
        return true;
    }

    private string DescribeValue(int index)
    {
        var type = LuauNative.lua_type(Handle, index);
        if (type == 0)
        {
            return "nil";
        }

        if (type == 1)
        {
            return LuauNative.lua_toboolean(Handle, index) != 0 ? "true" : "false";
        }

        if (LuauNative.lua_isstring(Handle, index) != 0)
        {
            var pointer = LuauNative.lua_tolstring(Handle, index, out var length);
            return pointer == IntPtr.Zero ? string.Empty : Encoding.UTF8.GetString((byte*)pointer, (int)length);
        }

        var namePointer = LuauNative.lua_typename(Handle, type);
        var typeName = namePointer == IntPtr.Zero ? "unknown" : Marshal.PtrToStringUTF8(namePointer) ?? "unknown";
        return $"<{typeName}>";
    }
}

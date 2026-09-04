using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OmniBlock.Luau;

/// <summary>
///     The two native-calls-C# entry points every Luau VM needs at setup
///     (docs/luau-ffi-embedding-plan.md Part 2.3/2.5/2.6): the allocator handed to
///     <c>lua_newstate</c>, and the instruction-budget interrupt installed via
///     <c>lua_callbacks(L)-&gt;interrupt</c>. Both are <c>static</c>, capture nothing, and touch
///     no managed object — required for <see cref="UnmanagedCallersOnlyAttribute" /> to apply at
///     all, and exactly the constraint that keeps them off the GC's radar on the hot path.
///     Wiring these into an actual <c>lua_State*</c> (installing the counter via
///     <c>lua_setthreaddata</c>, resetting the budget every tick) is VM-lifecycle code that
///     doesn't exist yet — this file only defines the callbacks themselves.
/// </summary>
internal static unsafe class LuauCallbacks
{
    /// <summary>
    ///     <c>lua_Alloc</c> — every byte a Luau VM touches (bytecode, tables, strings) is
    ///     requested through this function, backed by <see cref="NativeMemory" /> rather than
    ///     the managed heap, so a mod script's entire memory footprint stays invisible to the
    ///     .NET GC. <paramref name="osize" /> (the previous allocation size) is part of the
    ///     C API contract but unused here — <see cref="NativeMemory.Realloc" /> doesn't need it.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void* Allocate(void* userData, void* ptr, nuint osize, nuint nsize)
    {
        if (nsize == 0)
        {
            NativeMemory.Free(ptr);
            return null;
        }

        return NativeMemory.Realloc(ptr, nsize);
    }

    /// <summary>
    ///     Luau's <c>lua_Callbacks.interrupt</c> — called at every VM safepoint (loop
    ///     back-edges, calls/returns, GC steps). Reads the per-state instruction-budget counter
    ///     via the cached <see cref="LuauNative.lua_getthreaddata" /> pointer, decrements it, and
    ///     raises a Lua error once it's exhausted. Pure pointer arithmetic against unmanaged
    ///     memory — no managed allocation, no managed call, so it stays cheap enough to run at
    ///     this frequency without itself becoming the cost it exists to bound.
    ///     <para>
    ///         The counter is a single unmanaged <see cref="long" />, owned and reset once per
    ///         tick by VM-lifecycle code that doesn't exist yet. If nothing has called
    ///         <c>lua_setthreaddata</c> for this state yet, the counter pointer is null and this
    ///         is a no-op — there is no budget to enforce before one has been installed.
    ///     </para>
    ///     <para>
    ///         Calling <see cref="LuauNative.luaL_errorL" /> here performs a C <c>longjmp</c>
    ///         back to the nearest <c>lua_pcall</c> boundary rather than returning — safe only
    ///         because the native build enables <c>LUA_USE_LONGJMP</c> for exactly this reason
    ///         (native/luau/vendor/luau's <c>LUAU_EXTERN_C</c> option sets it automatically; see
    ///         native/luau/CMakeLists.txt). Unwinding a C++ exception through an
    ///         <see cref="UnmanagedCallersOnlyAttribute" /> boundary is undefined behavior, but a
    ///         plain C longjmp through one is not.
    ///     </para>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void Interrupt(IntPtr L, int gc)
    {
        var counterPtr = LuauNative.lua_getthreaddata(L);
        if (counterPtr == IntPtr.Zero)
        {
            return;
        }

        var counter = (long*)counterPtr;
        if (--*counter <= 0)
        {
            LuauNative.luaL_errorL(L, "OmniBlock.Luau: instruction budget exceeded");
        }
    }

    /// <summary>
    ///     Copies the parent state's shared instruction counter into every coroutine. Luau
    ///     otherwise initializes a coroutine's thread-data slot to null, which would make its
    ///     interrupt callbacks silently unbudgeted.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static void UserThread(IntPtr parent, IntPtr thread)
    {
        // On destruction Luau calls userthread(NULL, thread); never touch the dying state.
        if (parent == IntPtr.Zero || thread == IntPtr.Zero) return;
        LuauNative.lua_setthreaddata(thread, LuauNative.lua_getthreaddata(parent));
    }
}

using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace OmniBlock.Luau;

/// <summary>
///     Raw P/Invoke surface over <c>libomniblock_luau</c> (native/luau, pinned to Luau 0.733 —
///     see docs/luau-ffi-embedding-plan.md Part 2.2). Two call shapes coexist deliberately:
///     <list type="bullet">
///         <item>
///             <c>[LibraryImport]</c> static partial methods for the general, low-frequency
///             surface (VM creation/teardown, pushing values, registering closures — all
///             load-time or setup-time calls, never once-per-instruction).
///         </item>
///         <item>
///             Cached <c>delegate* unmanaged[Cdecl]</c> function pointers, resolved once via
///             <see cref="NativeLibrary.GetExport" />, for the handful of calls that happen on
///             the actual hot path — every VM instruction/safepoint, not every tick. These skip
///             even the small residual stub <c>[LibraryImport]</c> leaves in place.
///         </item>
///     </list>
///     Two Luau public-API entry points named in the plan are <b>macros</b>, not exported
///     symbols — binding the macro name directly would build clean and fail at runtime with
///     <see cref="EntryPointNotFoundException" />. Both are called out at their binding below.
/// </summary>
internal static unsafe partial class LuauNative
{
    private const string LibraryName = "omniblock_luau";

    // --- General surface: [LibraryImport], cold/setup-frequency calls only ---------------

    [LibraryImport(LibraryName)]
    internal static partial IntPtr lua_newstate(IntPtr allocFn, IntPtr userData);

    // Registers every standard library (base, string, math, table, ...) into a freshly created
    // state — a bare lua_newstate has none of them, not even `error`/`tostring`/`pairs`.
    // LuauQuickRun calls this once per ephemeral VM so debug-console scripts see ordinary Luau
    // semantics rather than a crippled arithmetic-only subset.
    [LibraryImport(LibraryName)]
    internal static partial void luaL_openlibs(IntPtr L);

    [LibraryImport(LibraryName)]
    internal static partial void lua_close(IntPtr L);

    [LibraryImport(LibraryName)]
    internal static partial void lua_pushinteger(IntPtr L, int n);

    // Luau's lua_pushstring returns void, not const char* like stock Lua 5.1 —
    // it does not hand back an interned-string pointer to the caller.
    [LibraryImport(LibraryName)]
    internal static partial void lua_pushstring(IntPtr L, [MarshalUsing(typeof(Utf8StringMarshaller))] string s);

    // lua_pushcclosure(L, fn, debugname, nup) is `#define lua_pushcclosure(L, fn, debugname, nup)
    // lua_pushcclosurek(L, fn, debugname, nup, NULL)` in lua.h — there is no exported
    // "lua_pushcclosure" symbol. `cont` (lua_Continuation) is unused by anything this project
    // builds so far; always pass IntPtr.Zero for it.
    [LibraryImport(LibraryName)]
    internal static partial void lua_pushcclosurek(
        IntPtr L,
        delegate* unmanaged[Cdecl]<IntPtr, int> fn,
        [MarshalUsing(typeof(Utf8StringMarshaller))] string? debugname,
        int nup,
        IntPtr cont);

    // luaL_error(L, fmt, ...) is `#define luaL_error(L, fmt, ...) luaL_errorL(L, fmt,
    // ##__VA_ARGS__)` in lualib.h — the exported symbol is luaL_errorL, and it is genuinely
    // variadic in C. Binding it with a fixed (L, message) signature and zero marshalled
    // varargs is safe under cdecl (the callee only reads args its format string references),
    // but only as long as `message` is a literal with no printf format specifiers — this
    // binding must never be called with caller-controlled or specifier-bearing text.
    [LibraryImport(LibraryName)]
    internal static partial void luaL_errorL(IntPtr L, [MarshalUsing(typeof(Utf8StringMarshaller))] string message);

    // Luau's determinism hook (docs/luau-ffi-embedding-plan.md Part 2.6): returns a pointer to
    // a per-VM lua_Callbacks struct whose `interrupt` field the VM invokes at every safepoint.
    // Called once per VM at setup to install LuauCallbacks.Interrupt, not on the hot path
    // itself.
    [LibraryImport(LibraryName)]
    internal static partial IntPtr lua_callbacks(IntPtr L);

    // Luau's single per-state unmanaged userdata slot. Set once at VM setup to the address of
    // the instruction-budget counter LuauCallbacks.Interrupt reads every safepoint via the
    // cached lua_getthreaddata pointer below, not this LibraryImport form.
    [LibraryImport(LibraryName)]
    internal static partial void lua_setthreaddata(IntPtr L, IntPtr data);

    // Compile-and-load: the minimum needed to prove Interrupt actually halts a running script,
    // not a HostApi surface. `source`/`data` are raw byte buffers with explicit lengths rather
    // than marshalled strings — source text isn't necessarily null-terminated, and bytecode
    // definitely isn't text. `options: null` (IntPtr.Zero) is an explicitly supported "use
    // defaults" per luau_compile's own implementation. The returned bytecode buffer is
    // malloc'd by the compiler, independent of lua_Alloc/LuauCallbacks.Allocate — freeing it
    // correctly (which CRT's free(), on which platform) is a real question a HostApi-level
    // compiler wrapper will need to answer; out of scope here.
    [LibraryImport(LibraryName)]
    internal static partial byte* luau_compile(byte* source, nuint size, IntPtr options, nuint* outsize);

    [LibraryImport(LibraryName)]
    internal static partial int luau_load(
        IntPtr L,
        [MarshalUsing(typeof(Utf8StringMarshaller))] string chunkname,
        byte* data,
        nuint size,
        int env);

    // lua_tointeger(L, i) is `#define lua_tointeger(L, i) lua_tointegerx(L, i, NULL)` in lua.h —
    // the exported symbol is lua_tointegerx. Result-retrieval, not hot path, so [LibraryImport]
    // like the rest of this section; isnum is always passed null since callers here already
    // know the stack slot holds a number from having pushed/computed it themselves.
    [LibraryImport(LibraryName)]
    internal static partial int lua_tointegerx(IntPtr L, int idx, IntPtr isnum);

    // Value-inspection surface for LuauQuickRun's console output formatter. All five are real
    // exported symbols, not macros — verified against VM/src/lapi.cpp, not assumed. None of
    // them can error/longjmp or invoke a __tostring metamethod (unlike luaL_tolstring, which
    // was deliberately NOT bound here for that reason), so they're safe to call on
    // caller-controlled values with no protected-call wrapper.

    // LUA_TNIL == 0 and LUA_TBOOLEAN == 1 are guaranteed stable by lua.h's own comments
    // ("must be 0/1 due to ..."); every other lua_Type enum value shifts depending on the
    // LUA_VECTOR_DOUBLE build config, so this binding's caller must never compare lua_type's
    // result against a hardcoded value other than those two — use lua_isstring/lua_typename
    // instead, both below.
    [LibraryImport(LibraryName)]
    internal static partial int lua_type(IntPtr L, int idx);

    // True for LUA_TSTRING and LUA_TNUMBER only (see lapi.cpp) — the two types lua_tolstring
    // below can actually convert. Deliberately used instead of comparing against a hardcoded
    // LUA_TNUMBER/LUA_TSTRING enum value, which (unlike TNIL/TBOOLEAN) is not ABI-stable.
    [LibraryImport(LibraryName)]
    internal static partial int lua_isstring(IntPtr L, int idx);

    [LibraryImport(LibraryName)]
    internal static partial int lua_toboolean(IntPtr L, int idx);

    // Real symbol (the macro is lua_tostring, which wraps this with len: NULL — see
    // lua.h:525). Converts numbers in place too (VM/src/lapi.cpp: falls through to
    // luaV_tostring for non-string values), so this alone covers both LUA_TSTRING and
    // LUA_TNUMBER; returns NULL for every other type without erroring. `len` is read
    // explicitly rather than relying on the returned buffer being NUL-terminated at the
    // right point — a Luau string can contain embedded NUL bytes, and trusting strlen()
    // instead of the real length would silently truncate those.
    [LibraryImport(LibraryName)]
    internal static partial IntPtr lua_tolstring(IntPtr L, int idx, out nuint len);

    // Returns a pointer into Luau's static, compiled-in luaT_typenames table — NOT a
    // caller-owned buffer. Deliberately bound as a raw IntPtr, not
    // [return: MarshalUsing(typeof(Utf8StringMarshaller))]: LibraryImport's generated stub for
    // a marshalled string return calls Utf8StringMarshaller.Free() on the native pointer after
    // copying it, on the assumption the callee allocated it for the caller to release. Applying
    // that to a pointer into static/const data crashes the process (glibc "free(): invalid
    // pointer") the first time it runs — caught empirically via a step-traced repro, not
    // predicted, since it doesn't fail until you actually call it. Callers must decode with
    // Marshal.PtrToStringUTF8, never free the result — mirrors lua_tolstring above, which was
    // bound this way from the start for the same reason (Luau owns the returned buffer).
    [LibraryImport(LibraryName)]
    internal static partial IntPtr lua_typename(IntPtr L, int t);

    // --- Hot path: cached delegate* unmanaged[Cdecl] pointers -----------------------------

    // Registering this resolver here — inside the first static field's initializer — means it
    // runs before ANY static member of this class is first touched (C# runs a type's static
    // field initializers as one block before first use), so every [LibraryImport] call above
    // benefits from it too, not just the manually-loaded handle below.
    private static readonly IntPtr s_libraryHandle = LoadLibrary();

    private static IntPtr LoadLibrary()
    {
        NativeLibrary.SetDllImportResolver(typeof(LuauNative).Assembly, ResolveLibrary);
        return NativeLibrary.Load(LibraryName, typeof(LuauNative).Assembly, null);
    }

    // Bare-name P/Invoke resolution on Linux/macOS does NOT search the app's own output
    // directory unless the native asset is registered in deps.json — which only happens
    // through real RID-NuGet packaging (docs/luau-ffi-embedding-plan.md Part 1.4, not built
    // yet; confirmed by direct experiment, not assumption — a physically-present sibling .so
    // is invisible to a bare NativeLibrary.Load without this). Until the RID package exists,
    // LUAU_NATIVE_LOCAL (OmniBlock.Luau.csproj) copies the local build next to this assembly,
    // and this fallback finds it there by absolute path.
    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
        {
            return IntPtr.Zero;
        }

        string fileName = OperatingSystem.IsWindows() ? "omniblock_luau.dll"
            : OperatingSystem.IsMacOS() ? "libomniblock_luau.dylib"
            : "libomniblock_luau.so";
        string candidate = Path.Combine(AppContext.BaseDirectory, fileName);
        return NativeLibrary.TryLoad(candidate, out IntPtr handle) ? handle : IntPtr.Zero;
    }

    internal static readonly delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int> lua_pcall =
        (delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int>)NativeLibrary.GetExport(s_libraryHandle, "lua_pcall");

    internal static readonly delegate* unmanaged[Cdecl]<IntPtr, int> lua_gettop =
        (delegate* unmanaged[Cdecl]<IntPtr, int>)NativeLibrary.GetExport(s_libraryHandle, "lua_gettop");

    // Read from inside LuauCallbacks.Interrupt — the highest-frequency call in the whole
    // system (every loop back-edge/call/ret/gc safepoint) — to fetch the instruction-budget
    // counter set up via lua_setthreaddata above. Cached rather than LibraryImport'd for
    // exactly that reason.
    internal static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr> lua_getthreaddata =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)NativeLibrary.GetExport(s_libraryHandle, "lua_getthreaddata");
}

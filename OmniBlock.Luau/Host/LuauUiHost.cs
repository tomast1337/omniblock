using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OmniBlock.Luau.Host;

/// <summary>
///     The Host Facade for UI commands (docs/luau-ui-host-api-plan.md §3): the one
///     <c>[UnmanagedCallersOnly]</c> entry point a script's
///     <c>
///         Host.openScreen(id, arg0, arg1,
///         arg2)
///     </c>
///     call actually lands on, and <see cref="Install" />, which pushes it into a
///     <see cref="LuauState" />'s VM as a global <c>Host</c> table — the same
///     <c>lua_pushcclosurek</c>-at-setup pattern <see cref="LuauCallbacks" />'s allocator/interrupt
///     already use, just invoked by <c>LuauState</c>'s owner instead of <c>LuauState</c> itself
///     (see the note on <see cref="Install" /> below for why).
///     <para>
///         This class deliberately knows nothing about <c>UIScreen</c> types, <c>ResourceLocation</c>,
///         or which command ID means what — it only knows how to pull four ints off the Luau stack
///         and hand them to <see cref="Dispatch" />.
///         <b>
///             OmniBlock.Luau has no project reference to
///             OmniBlock.Client or core OmniBlock
///         </b>
///         (by design — see
///         docs/luau-ffi-embedding-plan.md), so the actual command registry
///         (<c>OmniBlock.Client.UI.UiCommandRegistry</c>) can't be called directly from here; the
///         owning client wires <see cref="Dispatch" /> to <c>UiCommandRegistry.Invoke</c> once at
///         startup instead. This keeps the dependency arrow pointing the one way it already does
///         (Client → Luau), and keeps this facade reusable if a second, non-UI Host table is ever
///         added the same way.
///     </para>
/// </summary>
public static unsafe class LuauUiHost
{
    /// <summary>
    ///     Set exactly once by the owning client, e.g. right after
    ///     <c>
    ///         LuauState = new
    ///         LuauState()
    ///     </c>
    ///     in <c>OmniBlock.cs</c>'s <c>SetupCoreSystems</c>, to
    ///     <c>UiCommandRegistry.Invoke</c>. Left null this is a safe no-op (see
    ///     <see cref="OpenScreen" />), not a crash — matters for tests and any build where the
    ///     client-side registry hasn't been wired up yet.
    /// </summary>
    public static Action<int, int, int, int>? Dispatch;

    /// <summary>
    ///     Pushes the <c>Host</c> global table into <paramref name="l" />, with <c>openScreen</c>
    ///     bound to <see cref="OpenScreen" />. Not done inside <see cref="LuauState" /> itself —
    ///     that class's own doc comment states it exposes no compile/load/pcall surface because
    ///     driving script execution is Host API work, out of its scope by design; this is exactly
    ///     that Host API work, so it lives here and is called against <see cref="LuauState.Handle" />
    ///     by whichever Host-phase setup code owns that decision (client bootstrap, not the VM
    ///     lifecycle class).
    /// </summary>
    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 1);
        LuauNative.lua_pushcclosurek(l, &OpenScreen, "Host.openScreen", 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, "openScreen");
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "Host");
    }

    /// <summary>
    ///     <c>Host.openScreen(commandId, arg0, arg1, arg2)</c>. Looks up nothing itself — every
    ///     argument is read off the Luau stack as a plain int via
    ///     <see cref="LuauNative.luaL_checkinteger" /> (which errors, safely longjmping back to the
    ///     nearest <c>lua_pcall</c>, on a wrong-typed or missing argument — see that binding's own
    ///     doc comment) and handed to <see cref="Dispatch" /> unchanged. No managed allocation on
    ///     the success path: four native calls and one delegate invoke, matching the zero-allocation
    ///     discipline <see cref="LuauCallbacks" /> already established for this
    ///     <c>[UnmanagedCallersOnly]</c> tier.
    ///     <para>
    ///         Returns 0 (no Luau results pushed) — this is a command, not a query.
    ///     </para>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OpenScreen(IntPtr l)
    {
        var commandId = LuauNative.luaL_checkinteger(l, 1);
        var arg0 = LuauNative.luaL_checkinteger(l, 2);
        var arg1 = LuauNative.luaL_checkinteger(l, 3);
        var arg2 = LuauNative.luaL_checkinteger(l, 4);

        Dispatch?.Invoke(commandId, arg0, arg1, arg2);
        return 0;
    }
}

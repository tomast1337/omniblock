using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

/// <summary>
///     The Registry Facade for UI commands (docs/luau-registry-phase-plan.md), the string→ID
///     bridge <see cref="LuauUiHost" /> assumes exists: a mod's Registry-phase module calls
///     <c>Registry.registerUi(name)</c> once per command name and gets back the plain integer it
///     later passes to <c>Host.openScreen</c>. Separate <c>Registry</c> table, not more entries on
///     <c>Host</c> — deliberately, so a script that calls the wrong one by mistake gets an
///     immediate "attempt to call a nil value" at the call site instead of a phase check catching
///     it later.
///     <para>
///         Same no-project-reference constraint as <see cref="LuauUiHost" />: this class knows
///         nothing about <c>UiCommandRegistry</c> or <c>ResourceLocation</c>, only how to read one
///         string off the Luau stack and hand it to <see cref="RegisterUi" />, which the owning
///         client wires up once at startup.
///     </para>
/// </summary>
public static unsafe class LuauRegistryHost
{
    /// <summary>
    ///     Resolves (or creates) the ID for <paramref name="name" />, returning <c>false</c> when
    ///     the registry is already frozen. A status return, not a thrown exception, on purpose:
    ///     see <see cref="RegisterUiClosure" />'s doc comment for why a managed exception must
    ///     never be allowed to reach this class's <c>[UnmanagedCallersOnly]</c> boundary.
    /// </summary>
    public delegate bool RegisterUiDelegate(string name, out int id);

    /// <summary>
    ///     Set exactly once by the owning client, alongside <see cref="LuauUiHost.Dispatch" />, to
    ///     a wrapper over <c>UiCommandRegistry.Frozen</c>/<c>ResolveOrCreate</c>. Left null this is
    ///     treated the same as "frozen" (see <see cref="RegisterUiClosure" />) — there is nothing
    ///     useful <c>Registry.registerUi</c> can do without a registry to resolve against, and
    ///     erroring is safer than silently handing back a meaningless ID.
    /// </summary>
    public static RegisterUiDelegate? RegisterUi;

    /// <summary>
    ///     Pushes the <c>Registry</c> global table into <paramref name="l" />, with
    ///     <c>registerUi</c> bound to <see cref="RegisterUiClosure" />. Same call-site expectations
    ///     as <see cref="LuauUiHost.Install" />: called once per VM, before any mod's Registry-phase
    ///     module runs.
    /// </summary>
    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 1);
        LuauNative.lua_pushcclosurek(l, &RegisterUiClosure, "Registry.registerUi", 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, "registerUi");
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "Registry");
    }

    /// <summary>
    ///     <c>Registry.registerUi(name) -&gt; id</c>. Reads <paramref name="l" />'s argument 1 via
    ///     <see cref="LuauNative.lua_tolstring" />, copying it into a managed <see cref="string" />
    ///     immediately and never freeing the native pointer — that pointer is Luau's own string
    ///     data, not a caller-owned buffer (see <see cref="LuauNative.lua_tolstring" />'s own doc
    ///     comment); nothing here ever takes ownership of it, so there is nothing to free and
    ///     nothing to double-free.
    ///     <para>
    ///         <b>Never lets a managed exception reach this method's return.</b>
    ///         <see cref="LuauCallbacks.Interrupt" />'s doc comment establishes that a C
    ///         <c>longjmp</c> through <c>[UnmanagedCallersOnly]</c> is safe here (the native build
    ///         enables <c>LUA_USE_LONGJMP</c> for exactly that), but that guarantee does not extend
    ///         to a thrown CLR exception unwinding the same boundary — a separate, undefined-behavior
    ///         path this method must not take. That's why <see cref="RegisterUi" /> is a
    ///         <c>bool</c>-returning delegate rather than one that throws on "already frozen": the
    ///         only error signal this method itself raises is <see cref="LuauNative.luaL_errorL" />,
    ///         called directly, natively, from managed code that is not inside a try/catch or
    ///         propagating anything — a real longjmp, not an exception pretending to be one.
    ///     </para>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RegisterUiClosure(IntPtr l)
    {
        IntPtr ptr = LuauNative.lua_tolstring(l, 1, out nuint len);
        if (ptr == IntPtr.Zero)
        {
            LuauNative.luaL_errorL(l, "Registry.registerUi expects a string argument");
            return 0; // unreachable — luaL_errorL longjmps
        }

        // Unchecked cast, deliberately: a Luau string long enough to overflow int would already be
        // an absurd, self-inflicted mod-author problem (client-side, locally installed script, not
        // adversarial network input), and a checked cast throwing OverflowException here would be
        // exactly the "managed exception crosses the boundary" case this method must not allow.
        // Encoding.UTF8 (the static property, not a throwing UTF8Encoding) replaces malformed
        // sequences instead of throwing, so this call itself cannot throw either.
        string name = Encoding.UTF8.GetString(new ReadOnlySpan<byte>((byte*)ptr, (int)len));

        if (RegisterUi is not { } registerUi || !registerUi(name, out int id))
        {
            // Covers both realistic causes with one message: called after Freeze(), or 'name'
            // failed ResourceLocation validation (bad characters, missing namespace on a
            // "namespace:" prefix). RegisterUi collapses both into a single false — see its own
            // doc comment for why this boundary can't distinguish them without risking a thrown
            // exception crossing back into this method.
            LuauNative.luaL_errorL(l,
                "Registry.registerUi failed: the Registry phase is frozen, or 'name' is not a valid command identifier");
            return 0; // unreachable — luaL_errorL longjmps
        }

        LuauNative.lua_pushinteger(l, id);
        return 1;
    }
}

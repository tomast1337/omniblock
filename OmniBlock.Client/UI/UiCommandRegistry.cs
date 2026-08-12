namespace OmniBlock.Client.UI;

/// <summary>Handler for one UI command, invoked with whatever a given command needs in <paramref name="arg0" />..<paramref name="arg2" /> — a container window ID, a toast severity, etc. Always primitive ints, never a marshalled string or object reference, matching every other Host-phase binding (docs/luau-ui-host-api-plan.md §3).</summary>
public delegate void UiCommandHandler(int arg0, int arg1, int arg2);

/// <summary>
///     Maps UI command keys to the compact integers a script holds onto, for one client session.
///     Instantiable rather than static, matching <see cref="OmniBlock.Network.Messages.MessageRegistry" />
///     — one owner (<c>OmniBlock.cs</c>, like <c>OmniBlockServer.Messages</c> holds its own
///     <c>MessageRegistry</c>) holds the real instance, and tests can construct their own.
///     <para>
///         IDs are assigned at <see cref="Register" />/<see cref="ResolveOrCreate" /> call time, in
///         call order — <b>not</b> sorted-and-reassigned at <see cref="Freeze" />, unlike an earlier
///         version of this class that copied <c>MessageRegistry</c>'s sorted-freeze shape wholesale.
///         That shape solves cross-process ID agreement (client and server independently sorting
///         the same key set to converge on one table); this registry is local to one process, one
///         load sequence, with nothing to agree with — and sorted-reassignment is actively wrong
///         once a script can synchronously receive an ID mid-load
///         (docs/luau-registry-phase-plan.md §0): a later re-sort would silently invalidate any ID
///         a script already cached. Collision safety between independently-loaded mods never came
///         from sort order in the first place — it comes from <see cref="Register" /> throwing on a
///         duplicate key, which call-order assignment preserves unchanged.
///     </para>
///     <para>
///         <see cref="Invoke" /> is the Host-phase (hot) half of this class — an array index by a
///         small frozen integer, no allocation — reached from <c>LuauUiHost</c>'s
///         <c>[UnmanagedCallersOnly]</c> facade via the <c>LuauUiHost.Dispatch</c> seam, which the
///         owner points at one instance's <see cref="Invoke" /> method. <see cref="Register" />/
///         <see cref="ResolveOrCreate" />/<see cref="Freeze" /> are the Registry-phase (cold) half;
///         all three run only during client bootstrap / mod load, off any hot path, and allocate
///         freely.
///     </para>
/// </summary>
public sealed class UiCommandRegistry
{
    private readonly Dictionary<ResourceLocation, int> _idByKey = [];
    private readonly List<UiCommandHandler?> _byId = [];

    public bool Frozen { get; private set; }

    /// <summary>
    ///     Registers a UI command handler, assigning it the next unused ID. Idempotent per key in
    ///     the sense of rejecting reuse, not silently merging: re-registering a key that already
    ///     exists — whether from a prior <see cref="Register" /> or <see cref="ResolveOrCreate" />
    ///     call — throws, since that's two callers claiming one command name rather than something
    ///     to resolve silently.
    /// </summary>
    public void Register(ResourceLocation key, UiCommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(handler);
        EnsureNotFrozen(key);

        if (_idByKey.ContainsKey(key))
        {
            throw new InvalidOperationException($"UI command key '{key}' is already registered.");
        }

        _idByKey[key] = _byId.Count;
        _byId.Add(handler);
    }

    /// <summary>
    ///     Registry-phase entry point for a script's <c>Registry.registerUi(name)</c>
    ///     (docs/luau-registry-phase-plan.md §1/§3): returns the existing ID for
    ///     <paramref name="key" /> if one was already assigned by any prior <see cref="Register" />
    ///     or <see cref="ResolveOrCreate" /> call, otherwise assigns the next unused ID with no
    ///     handler attached yet. Unlike <see cref="Register" />, calling this twice for the same key
    ///     is expected and safe — two mods (or one mod's module loaded twice) asking for the same
    ///     name should get the same ID back, not a collision error. A key with no handler is not an
    ///     error state: <see cref="Invoke" /> on it is simply a no-op, the same as an out-of-range
    ///     ID.
    /// </summary>
    public int ResolveOrCreate(ResourceLocation key)
    {
        ArgumentNullException.ThrowIfNull(key);
        EnsureNotFrozen(key);

        if (_idByKey.TryGetValue(key, out int existingId))
        {
            return existingId;
        }

        int id = _byId.Count;
        _idByKey[key] = id;
        _byId.Add(null);
        return id;
    }

    private void EnsureNotFrozen(ResourceLocation key)
    {
        if (Frozen)
        {
            throw new InvalidOperationException(
                $"Cannot register '{key}' after {nameof(UiCommandRegistry)} is frozen: every Registry-phase module must run before scripts reach Host-phase.");
        }
    }

    /// <summary>
    ///     Locks the registry against further <see cref="Register" />/<see cref="ResolveOrCreate" />
    ///     calls. IDs are already final by the time this runs — assigned at call time, not here —
    ///     so this is purely a gate, not an ID-assignment step. Must run once, after every mod's
    ///     Registry-phase module has run and before any script reaches Host-phase — the exact freeze
    ///     point relative to <c>Bootstrap.cs</c> is docs/luau-ui-host-api-plan.md Open Question #2,
    ///     undecided by this class.
    /// </summary>
    public void Freeze() => Frozen = true;

    /// <summary>
    ///     Host-phase entry point: invokes the handler at <paramref name="id" />, or does nothing
    ///     when <paramref name="id" /> is out of range or has no handler attached — an unknown or
    ///     handler-less ID is a skip, not an error, the same convention <c>MessageRegistry.Create</c>
    ///     uses for a message this peer doesn't implement.
    /// </summary>
    public void Invoke(int id, int arg0, int arg1, int arg2)
    {
        if ((uint)id >= (uint)_byId.Count)
        {
            return;
        }

        _byId[id]?.Invoke(arg0, arg1, arg2);
    }
}

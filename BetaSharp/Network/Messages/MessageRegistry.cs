using Microsoft.Extensions.Logging;

namespace BetaSharp.Network.Messages;

/// <summary>
///     Maps message keys to the compact integers they travel as, for one session.
///     <para>
///         Registration and negotiation are separate on purpose. Registration is local and
///         order-independent: content can register at any point during load. Negotiation happens
///         once per connection and freezes an ordering both peers agree on.
///     </para>
///     <para>
///         <b>The server dictates the ordering.</b> It sends its registered keys, sorted, and the
///         client adopts that list as the ID table. This is the whole reason mods cannot collide:
///         nobody picks a number, and two mods loading in a different order on the two machines
///         still agree, because the ordering is derived from the sorted key set rather than from
///         registration sequence.
///     </para>
/// </summary>
public sealed class MessageRegistry
{
    private static readonly ILogger<MessageRegistry> s_logger = Log.Instance.For<MessageRegistry>();

    private readonly Dictionary<ResourceLocation, Registration> _byKey = [];

    private ResourceLocation[] _negotiatedOrder = [];
    private Registration?[] _byId = [];

    /// <summary>Keys registered locally, sorted canonically. Not yet frozen.</summary>
    public IReadOnlyList<ResourceLocation> RegisteredKeys => [.. _byKey.Keys.Order()];

    /// <summary>
    ///     The frozen table, in wire-ID order. This is what a server advertises; reading it before
    ///     negotiation throws rather than silently sending an unfrozen list that later registrations
    ///     could contradict.
    /// </summary>
    public IReadOnlyList<ResourceLocation> NegotiatedOrder =>
        Negotiated
            ? _negotiatedOrder
            : throw new InvalidOperationException("The message ID table has not been negotiated yet.");

    /// <summary>False until <see cref="AdoptOrdering" /> or <see cref="NegotiateAsServer" /> runs.</summary>
    public bool Negotiated { get; private set; }

    public int Count => _negotiatedOrder.Length;

    /// <summary>
    ///     Registers a message type. Idempotent per key; re-registering the same key throws, since
    ///     that is two pieces of content claiming one name rather than something to resolve
    ///     silently.
    /// </summary>
    public void Register(ResourceLocation key, int schemaVersion, Func<Message> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (Negotiated)
        {
            throw new InvalidOperationException(
                $"Cannot register '{key}' after the ID table is negotiated: the peer has already been told the table.");
        }

        if (!_byKey.TryAdd(key, new Registration(key, schemaVersion, factory)))
        {
            throw new InvalidOperationException(
                $"Message key '{key}' is already registered. Two message types cannot share a key.");
        }
    }

    /// <summary>
    ///     Server side. Freezes the local key set into the ordering to advertise.
    /// </summary>
    public IReadOnlyList<ResourceLocation> NegotiateAsServer()
    {
        AdoptOrdering(RegisteredKeys);
        return _negotiatedOrder;
    }

    /// <summary>
    ///     Client side. Adopts the server's ordering verbatim.
    ///     <para>
    ///         Keys the server advertises that this peer does not know are kept as holes in the
    ///         table. That is deliberate: the ID space stays aligned with the server's, so a message
    ///         this peer cannot decode is skipped rather than shifting every subsequent ID. Keys
    ///         registered locally but absent from the server's list simply never travel.
    ///     </para>
    /// </summary>
    public void AdoptOrdering(IReadOnlyList<ResourceLocation> ordering)
    {
        ArgumentNullException.ThrowIfNull(ordering);

        _negotiatedOrder = [.. ordering];
        _byId = new Registration?[ordering.Count];

        int known = 0;
        for (int id = 0; id < ordering.Count; id++)
        {
            if (_byKey.TryGetValue(ordering[id], out Registration? registration))
            {
                _byId[id] = registration;
                known++;
            }
        }

        Negotiated = true;

        int unknown = ordering.Count - known;
        int unsent = _byKey.Count - known;

        if (unknown > 0 || unsent > 0)
        {
            s_logger.LogInformation(
                "Message table negotiated: {Known} shared, {Unknown} advertised but unknown here, {Unsent} registered here but not advertised.",
                known,
                unknown,
                unsent);
        }
    }

    /// <summary>Wire ID for a key, or -1 when the peer did not advertise it.</summary>
    public int GetId(ResourceLocation key)
    {
        if (!Negotiated)
        {
            throw new InvalidOperationException("The message ID table has not been negotiated yet.");
        }

        return Array.IndexOf(_negotiatedOrder, key);
    }

    /// <summary>
    ///     Creates an empty instance of the message with this wire ID, or null when the ID is
    ///     outside the table or names a message this peer does not implement. Null is the skip
    ///     signal, not an error: the envelope's length prefix means the reader can step over it.
    /// </summary>
    public Message? Create(int id)
    {
        if (!Negotiated || id < 0 || id >= _byId.Length)
        {
            return null;
        }

        return _byId[id]?.Factory();
    }

    /// <summary>Key for a wire ID, for diagnostics. Null when out of range.</summary>
    public ResourceLocation? GetKey(int id) =>
        id >= 0 && id < _negotiatedOrder.Length ? _negotiatedOrder[id] : null;

    public int GetSchemaVersion(ResourceLocation key) =>
        _byKey.TryGetValue(key, out Registration? registration) ? registration.SchemaVersion : -1;

    private sealed record Registration(ResourceLocation Key, int SchemaVersion, Func<Message> Factory);
}

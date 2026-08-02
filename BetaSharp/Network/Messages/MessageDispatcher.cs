namespace BetaSharp.Network.Messages;

/// <summary>
///     Routes a decoded message to the handler registered for its type.
///     <para>
///         The alternative, and what the first few messages used, is a <c>switch</c> over the
///         message type in each peer's <c>onMessage</c>. That is fine at nine messages and wrong at
///         sixty: it is a linear run of type tests per message, and more importantly it is a closed
///         list living in the engine, which is precisely the property this layer exists to remove.
///         A mod cannot add a case to somebody else's switch. It can register here.
///     </para>
///     <para>
///         This is the counterpart of <c>Packet.Apply(NetHandler)</c>, minus the coupling: a packet
///         names the handler method it wants, which requires the handler to have been written to
///         know about it. A message names nothing, and the peer that cares declares the interest.
///     </para>
/// </summary>
public sealed class MessageDispatcher
{
    private readonly Dictionary<Type, Action<Message>> _handlers = [];

    /// <summary>
    ///     Registers the handler for one message type.
    ///     <para>
    ///         Registering a type twice throws rather than replacing or chaining. Two owners for one
    ///         message is a mistake in every case it can arise — a mod that means to observe a
    ///         message somebody else owns wants an event, not the handler slot — and silently
    ///         letting the second win would make which of them runs depend on load order.
    ///     </para>
    /// </summary>
    public void On<TMessage>(Action<TMessage> handler)
        where TMessage : Message
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryAdd(typeof(TMessage), message => handler((TMessage)message)))
        {
            throw new InvalidOperationException(
                $"A handler for {typeof(TMessage).Name} is already registered on this peer.");
        }
    }

    /// <summary>
    ///     Dispatches a message, returning whether anything handled it.
    ///     <para>
    ///         False is a normal answer, not an error: a message this peer registered to receive but
    ///         has no use for on this side — a client-bound message arriving at a server — is
    ///         dropped. The caller decides whether that is worth reporting.
    ///     </para>
    /// </summary>
    public bool Dispatch(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_handlers.TryGetValue(message.GetType(), out Action<Message>? handler))
        {
            return false;
        }

        handler(message);
        return true;
    }

    /// <summary>Whether a handler exists for this type. For tests and diagnostics.</summary>
    public bool Handles<TMessage>()
        where TMessage : Message => _handlers.ContainsKey(typeof(TMessage));
}

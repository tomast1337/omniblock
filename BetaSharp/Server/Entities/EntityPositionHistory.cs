namespace BetaSharp.Server.Entities;

/// <summary>
///     Where a tracked entity was, for the last couple of seconds of server time.
///     <para>
///         The client renders remote entities in the past — <see cref="Client.Network.EntityInterpolator" />
///         puts them anywhere from 100 ms to three seconds behind the server clock, per entity — so
///         the position an attacking player
///         actually aimed at is never the position the server holds when the attack arrives. Without
///         a record of the former, a hit that landed on screen is rejected by a reach check against
///         a target that has since walked away, and the miss scales with latency.
///     </para>
///     <para>
///         <b>Recorded every tick, not every tracking interval.</b> The tracker speaks about an
///         entity every 2 to 20 ticks depending on its kind, and interpolating between two
///         twenty-tick samples reconstructs a straight line the entity never walked. The history is
///         the simulation's own record, so it is written from the simulation's rate regardless of
///         what the network chose to say about it.
///     </para>
/// </summary>
public sealed class EntityPositionHistory
{
    /// <summary>
    ///     Ticks retained. Two seconds at 20 TPS, which is double <see cref="MaxRewindMs" /> so that
    ///     a rewind at the limit still has a record on both sides of it to interpolate between.
    /// </summary>
    public const int Capacity = 40;

    /// <summary>
    ///     Furthest into the past a rewind may reach, and a gameplay policy rather than a networking
    ///     one — it is the latency above which the server stops believing the attacker and starts
    ///     protecting the target.
    ///     <para>
    ///         A second covers the interpolation delay of everything the client renders at a normal
    ///         update rate, plus a realistic RTT. It does not cover the three-second ceiling
    ///         <c>EntityInterpolator.MaxDelayMs</c> allows, and that is deliberate: an entity delayed
    ///         that far is one the server has said nothing about for seconds, and rewinding a live
    ///         target a full three seconds to reward a hit on a stale ghost is worse than the miss.
    ///     </para>
    /// </summary>
    public const long MaxRewindMs = 1000;

    private readonly Sighting[] _records = new Sighting[Capacity];

    private int _count;
    private int _next;

    /// <summary>Records retained, for tests and diagnostics.</summary>
    public int Count => _count;

    /// <summary>
    ///     Appends this tick's position. <paramref name="serverTimeMs" /> is the simulation instant,
    ///     the same value <c>TickStampMessage</c> carries — so a client's render time and this
    ///     history are quantities in one clock domain, which is the whole reason the rewind can be
    ///     expressed as a time at all.
    /// </summary>
    public void Record(long serverTimeMs, double x, double y, double z)
    {
        // A repeated stamp is the same tick recorded twice, which happens when the tracker runs more
        // often than the simulation. Overwrite rather than append: two records at one instant give
        // the interpolation a zero-width interval to divide by.
        if (_count > 0 && Newest().ServerTimeMs == serverTimeMs)
        {
            _records[(_next - 1 + Capacity) % Capacity] = new Sighting(serverTimeMs, x, y, z);
            return;
        }

        _records[_next] = new Sighting(serverTimeMs, x, y, z);
        _next = (_next + 1) % Capacity;

        if (_count < Capacity)
        {
            _count++;
        }
    }

    /// <summary>
    ///     Where the entity was at <paramref name="serverTimeMs" />, interpolated between the two
    ///     bracketing ticks.
    ///     <para>
    ///         Clamped at both ends rather than refused. Past the newest record is the ordinary case
    ///         for an attack with no rewind information at all, and it must answer with the present
    ///         position; before the oldest is a rewind deeper than the buffer, and the oldest record
    ///         is the closest true statement available. The caller bounds how far back it is willing
    ///         to ask — see <see cref="MaxRewindMs" /> — so a clamp here is a floor under a decision
    ///         already made, not a policy of its own.
    ///     </para>
    /// </summary>
    /// <returns>False when nothing has been recorded, leaving the outputs at zero.</returns>
    public bool Sample(long serverTimeMs, out double x, out double y, out double z)
    {
        x = 0.0;
        y = 0.0;
        z = 0.0;

        if (_count == 0)
        {
            return false;
        }

        Sighting newest = Newest();
        if (serverTimeMs >= newest.ServerTimeMs)
        {
            (x, y, z) = (newest.X, newest.Y, newest.Z);
            return true;
        }

        Sighting oldest = At(0);
        if (serverTimeMs <= oldest.ServerTimeMs)
        {
            (x, y, z) = (oldest.X, oldest.Y, oldest.Z);
            return true;
        }

        for (int i = _count - 1; i > 0; i--)
        {
            Sighting after = At(i);
            Sighting before = At(i - 1);

            if (serverTimeMs < before.ServerTimeMs)
            {
                continue;
            }

            long span = after.ServerTimeMs - before.ServerTimeMs;
            double t = span <= 0 ? 0.0 : (double)(serverTimeMs - before.ServerTimeMs) / span;

            x = before.X + ((after.X - before.X) * t);
            y = before.Y + ((after.Y - before.Y) * t);
            z = before.Z + ((after.Z - before.Z) * t);
            return true;
        }

        (x, y, z) = (oldest.X, oldest.Y, oldest.Z);
        return true;
    }

    /// <summary>
    ///     The instant a rewind to <paramref name="requestedMs" /> actually lands on, given
    ///     <paramref name="nowMs" />. Public so the decision is one function rather than a clamp
    ///     repeated at each call site, and so a test can state the policy directly.
    /// </summary>
    public static long ClampRewind(long requestedMs, long nowMs)
    {
        // Zero is a peer that sent no rewind information — a vanilla client, or one whose clock has
        // not synchronised yet. It gets the present, which is the behaviour that existed before this.
        if (requestedMs <= 0)
        {
            return nowMs;
        }

        return Math.Clamp(requestedMs, nowMs - MaxRewindMs, nowMs);
    }

    private Sighting Newest() => At(_count - 1);

    /// <summary>Index 0 is the oldest retained record.</summary>
    private Sighting At(int index) => _records[(_next - _count + index + Capacity) % Capacity];

    private readonly record struct Sighting(long ServerTimeMs, double X, double Y, double Z);
}

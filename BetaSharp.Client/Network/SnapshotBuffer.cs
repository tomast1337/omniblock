namespace BetaSharp.Client.Network;

/// <summary>
///     One remote entity's position as of one server tick.
/// </summary>
/// <param name="ServerTimeMs">
///     The server-clock instant this describes, from <c>TickStampS2CPacket</c>. Not the arrival
///     time — that is the whole point, see <see cref="SnapshotBuffer" />.
/// </param>
public readonly record struct Snapshot(
    long ServerTimeMs,
    double X,
    double Y,
    double Z,
    float Yaw,
    float Pitch);

/// <summary>The outcome of sampling a buffer, so callers can react to starvation.</summary>
public enum SampleKind
{
    /// <summary>No snapshots at all; nothing can be said.</summary>
    Empty,

    /// <summary>Render time fell between two snapshots. The normal case.</summary>
    Interpolated,

    /// <summary>Render time is before the oldest snapshot, so the oldest is held.</summary>
    Clamped,

    /// <summary>
    ///     Render time has run past the newest snapshot but is within the extrapolation cap, so
    ///     motion continues along the last known velocity.
    /// </summary>
    Extrapolated,

    /// <summary>Past the extrapolation cap. The entity holds still until data arrives.</summary>
    Frozen
}

/// <summary>
///     A short history of one remote entity's positions, sampled at render time against the
///     synchronised server clock rather than advanced by packet arrivals.
///     <para>
///         Phase 4 of <c>docs/time-sync-and-interpolation.md</c>. This is what actually fixes the
///         stutter, and the reason it works is worth stating baldly: <b>arrival time does not appear
///         anywhere in <see cref="Sample" />.</b> Only server timestamps do.
///     </para>
///     <para>
///         The legacy scheme it replaces is a <em>rate</em>, not a schedule — the entity moves 1/N
///         of the remaining distance per tick toward whatever target arrived last. That makes three
///         snapshots delivered together after a TCP stall indistinguishable from three delivered on
///         time, so the entity freezes and then lurches. Here, render time advances at real-time
///         rate off the monotonic clock whether or not packets are arriving; a burst that lands
///         after a stall simply extends the buffered timeline ahead of render time and causes no
///         visible event at all.
///     </para>
///     <para>
///         Twenty entries is one second at 20 TPS, comfortably more than the 100–500 ms of delay the
///         sampler renders behind.
///     </para>
/// </summary>
public sealed class SnapshotBuffer
{
    /// <summary>One second at 20 TPS.</summary>
    public const int Capacity = 20;

    /// <summary>
    ///     How far past the newest snapshot motion is continued before the entity is held still.
    ///     Extrapolating a walking player for a full second walks them through walls, and the
    ///     correction when real data arrives is a worse artefact than the pause.
    /// </summary>
    public const long ExtrapolationCapMs = 250;

    private readonly Snapshot[] _ring = new Snapshot[Capacity];
    private int _next;
    private int _count;

    /// <summary>Number of snapshots currently held, up to <see cref="Capacity" />.</summary>
    public int Count => _count;

    /// <summary>Server time of the newest snapshot, or 0 when empty.</summary>
    public long NewestServerTimeMs => _count == 0 ? 0 : At(_count - 1).ServerTimeMs;

    /// <summary>Server time of the oldest snapshot still held, or 0 when empty.</summary>
    public long OldestServerTimeMs => _count == 0 ? 0 : At(0).ServerTimeMs;

    /// <summary>
    ///     Appends a snapshot.
    ///     <para>
    ///         Snapshots at or before the newest already held are discarded. TCP delivers in order so
    ///         this should not happen, but a duplicate or equal stamp would otherwise create a
    ///         zero-length interval and a division by zero in <see cref="Sample" />.
    ///     </para>
    /// </summary>
    public void Push(in Snapshot snapshot)
    {
        if (_count > 0 && snapshot.ServerTimeMs <= NewestServerTimeMs)
        {
            return;
        }

        _ring[_next] = snapshot;
        _next = (_next + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }
    }

    /// <summary>
    ///     Drops everything. Called when the clock steps: the buffered stamps are on the old
    ///     timeline, and interpolating across that discontinuity would glide the entity through the
    ///     correction instead of cutting to it.
    /// </summary>
    public void Clear()
    {
        _next = 0;
        _count = 0;
    }

    /// <summary>
    ///     The entity's position at <paramref name="renderTimeMs" /> on the server's timeline.
    /// </summary>
    public SampleKind Sample(long renderTimeMs, out Snapshot result)
    {
        result = default;

        if (_count == 0)
        {
            return SampleKind.Empty;
        }

        if (_count == 1)
        {
            // A single snapshot has no interval to interpolate over and no velocity to extrapolate
            // along, so it is held regardless of which side of it render time falls.
            result = At(0);
            return renderTimeMs < result.ServerTimeMs ? SampleKind.Clamped : SampleKind.Frozen;
        }

        Snapshot oldest = At(0);
        if (renderTimeMs <= oldest.ServerTimeMs)
        {
            // Render time is behind everything held — the delay is larger than the history. Holding
            // the oldest is right: the alternative, extrapolating backwards, invents motion.
            result = oldest;
            return SampleKind.Clamped;
        }

        Snapshot newest = At(_count - 1);
        if (renderTimeMs >= newest.ServerTimeMs)
        {
            return SampleAheadOfBuffer(renderTimeMs, newest, out result);
        }

        // Newest-first: during normal playback render time sits near the recent end, so this
        // finds the bracketing pair in one or two steps.
        for (int i = _count - 1; i > 0; i--)
        {
            Snapshot s1 = At(i);
            Snapshot s0 = At(i - 1);

            if (renderTimeMs >= s0.ServerTimeMs && renderTimeMs <= s1.ServerTimeMs)
            {
                double t = (renderTimeMs - s0.ServerTimeMs) / (double)(s1.ServerTimeMs - s0.ServerTimeMs);
                result = Lerp(s0, s1, t, renderTimeMs);
                return SampleKind.Interpolated;
            }
        }

        // Unreachable: render time was shown to lie strictly inside the buffered range above.
        result = newest;
        return SampleKind.Frozen;
    }

    private SampleKind SampleAheadOfBuffer(long renderTimeMs, in Snapshot newest, out Snapshot result)
    {
        long ahead = renderTimeMs - newest.ServerTimeMs;

        if (ahead > ExtrapolationCapMs)
        {
            result = newest;
            return SampleKind.Frozen;
        }

        // Velocity from the last interval. Angles are deliberately not extrapolated — a player
        // flicking their view would spin the model well past where they actually looked, and the
        // snap back is far more noticeable than a held angle.
        Snapshot previous = At(_count - 2);
        double interval = newest.ServerTimeMs - previous.ServerTimeMs;

        if (interval <= 0.0)
        {
            result = newest;
            return SampleKind.Frozen;
        }

        double scale = ahead / interval;

        result = newest with
        {
            ServerTimeMs = renderTimeMs,
            X = newest.X + ((newest.X - previous.X) * scale),
            Y = newest.Y + ((newest.Y - previous.Y) * scale),
            Z = newest.Z + ((newest.Z - previous.Z) * scale)
        };

        return SampleKind.Extrapolated;
    }

    private static Snapshot Lerp(in Snapshot s0, in Snapshot s1, double t, long renderTimeMs) => new(
        renderTimeMs,
        s0.X + ((s1.X - s0.X) * t),
        s0.Y + ((s1.Y - s0.Y) * t),
        s0.Z + ((s1.Z - s0.Z) * t),
        LerpAngle(s0.Yaw, s1.Yaw, t),
        LerpAngle(s0.Pitch, s1.Pitch, t));

    /// <summary>
    ///     Interpolates along the shortest arc. Interpolating 179 to -179 linearly sweeps the long
    ///     way round and spins the model through a full turn for a two-degree change.
    /// </summary>
    public static float LerpAngle(float from, float to, double t)
    {
        double delta = to - from;

        while (delta < -180.0)
        {
            delta += 360.0;
        }

        while (delta >= 180.0)
        {
            delta -= 360.0;
        }

        return (float)(from + (delta * t));
    }

    /// <summary>Oldest-first indexing into the ring; <paramref name="index" /> 0 is the oldest held.</summary>
    private Snapshot At(int index)
    {
        int start = _count == Capacity ? _next : 0;
        return _ring[(start + index) % Capacity];
    }
}

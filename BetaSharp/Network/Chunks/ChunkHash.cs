using System.Security.Cryptography;

namespace OmniBlock.Network.Chunks;

/// <summary>
///     Content hash of an encoded chunk, used to skip sending a chunk the peer already holds.
/// </summary>
public static class ChunkHash
{
    /// <summary>
    ///     Hashes a <see cref="ChunkBlobCodec" /> blob down to sixty-four bits.
    ///     <para>
    ///         <b>Cryptographic, and truncated.</b> SHA-256 costs about 10 µs on a 12 KB blob against
    ///         the 200 µs the encoding itself takes, so the cheaper non-cryptographic hashes buy
    ///         nothing measurable here — and this one is fed by a value the *peer* controls. A client
    ///         advertises hashes for chunks it claims to hold, and a collision it could construct is
    ///         a chunk the server declines to send. That only corrupts the client's own view, so it
    ///         is not much of an attack, but paying nothing to rule it out is the easy call.
    ///     </para>
    ///     <para>
    ///         Sixty-four bits after truncation. Against a cache of a hundred thousand chunks the
    ///         odds of any accidental collision are about one in two billion, and the consequence is
    ///         one stale chunk until it next changes.
    ///     </para>
    /// </summary>
    public static ulong Of(ReadOnlySpan<byte> blob)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(blob, digest);

        ulong hash = 0;
        for (int i = 0; i < sizeof(ulong); i++)
        {
            hash = (hash << 8) | digest[i];
        }

        return hash;
    }
}

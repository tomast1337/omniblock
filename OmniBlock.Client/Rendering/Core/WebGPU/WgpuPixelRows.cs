namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     Puts tightly packed RGBA8 rows into the layout <c>QueueWriteTexture</c> demands.
/// </summary>
/// <remarks>
///     A write of more than one row has to start each row on a 256-byte boundary. Every texture in
///     this game is narrower than that at least sometimes — a 16-pixel tile is 64 bytes — so the
///     rule bites constantly, and wgpu rejects the write rather than tolerating it.
/// </remarks>
internal static class WgpuPixelRows
{
    private const uint Alignment = 256;

    /// <summary>
    ///     The pixels as wgpu wants them, alongside the stride to declare for them. Returns the
    ///     caller's own span untouched whenever the rows already land on the boundary.
    /// </summary>
    public static ReadOnlySpan<byte> Align(ReadOnlySpan<byte> rgba, uint width, uint height, out uint bytesPerRow)
    {
        uint tight = width * 4;
        bytesPerRow = height > 1
            ? (tight + Alignment - 1) / Alignment * Alignment
            : tight;

        if (bytesPerRow == tight) return rgba;

        byte[] padded = new byte[bytesPerRow * height];
        for (uint row = 0; row < height; row++)
        {
            rgba.Slice((int)(row * tight), (int)tight).CopyTo(padded.AsSpan((int)(row * bytesPerRow)));
        }

        return padded;
    }
}

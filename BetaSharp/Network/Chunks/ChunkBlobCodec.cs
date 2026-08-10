using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Network.Chunks;

/// <summary>
///     Encodes a chunk's block and light arrays into a compact wire blob, and back.
///     <para>
///         The inherited format sends a chunk as
///         81,920 raw bytes — 32,768 block ids, then three nibble arrays of 16,384 each for metadata,
///         block light and sky light — zlib'd whole. Every one of those bytes is paid for even where
///         the chunk is nothing but air with full sky above it, which is most of a chunk.
///     </para>
///     <para>
///         <b>Hard invariant.</b> This is a <em>wire</em> encoding and nothing else. It takes and
///         returns plain arrays in the in-memory layout and has no reference to
///         <c>Worlds/Storage/RegionFormat/</c>. The save format and the wire format must never
///         become the same type, and keeping them in separate namespaces with no shared type
///         enforces that structurally rather than by discipline: there is nothing here that could
///         accidentally be written to disk.
///     </para>
///     <para>
///         <b>Not compression.</b> A general compressor still runs over the result and still helps.
///         What this does is remove the redundancy a byte-oriented compressor is worst at — a
///         16-value alphabet stored one value per nibble, and 4,096 identical block ids that zlib
///         must still encode as a run rather than as a single fact.
///     </para>
/// </summary>
public static class ChunkBlobCodec
{
    /// <summary>
    ///     Bumped when the encoding changes in a way an older reader would misread. Peers already
    ///     agree a protocol revision during the handshake, so this exists to fail loudly on a blob
    ///     from a mismatched build rather than to negotiate anything.
    /// </summary>
    public const byte Version = 1;

    /// <summary>Blocks along each horizontal axis of a chunk.</summary>
    public const int ChunkWidth = 16;

    /// <summary>
    ///     Cube edge of one section.
    ///     <para>
    ///         Sixteen, so a chunk is eight sections and a section is 4,096 blocks. The size matters
    ///         for the same reason it does in every voxel format: a palette's value is that a section
    ///         is homogeneous, and homogeneity falls off as the volume grows. Whole-chunk palettes
    ///         were tried in modern Minecraft and abandoned for exactly this.
    ///     </para>
    /// </summary>
    public const int SectionHeight = 16;

    /// <summary>Blocks in one section.</summary>
    public const int SectionVolume = ChunkWidth * ChunkWidth * SectionHeight;

    /// <summary>Sections stacked in one chunk.</summary>
    public static int SectionsPerChunk => ChuckFormat.ChunkHeight / SectionHeight;

    /// <summary>
    ///     Largest palette that still beats sending the section raw. At 256 entries the table costs
    ///     512 bytes and the indices 4,096, against 6,144 for the raw pair — still a win, and past it
    ///     the table grows faster than the packing saves.
    /// </summary>
    public const int MaxPaletteSize = 256;

    /// <summary>How the block states of one section were written.</summary>
    private enum StateEncoding : byte
    {
        /// <summary>Every block in the section is the same state. Three bytes for 4,096 blocks.</summary>
        Uniform = 0,

        /// <summary>A table of distinct states, then one index per block at the narrowest width that fits.</summary>
        Palette = 1,

        /// <summary>
        ///     Ids and metadata in their original layout. Reached only by a section with more than
        ///     <see cref="MaxPaletteSize" /> distinct states, which generated terrain does not
        ///     produce; it exists so a pathological player build is merely uncompressed rather than
        ///     impossible to represent.
        /// </summary>
        Raw = 2,
    }

    /// <summary>How one nibble array of one section was written.</summary>
    private enum NibbleEncoding : byte
    {
        /// <summary>One value throughout — an unlit section, or open sky.</summary>
        Uniform = 0,

        /// <summary>
        ///     Fewer than eight distinct levels, packed at one to three bits. Worth having because
        ///     the alternative is four bits per value and light is usually flat over a small volume.
        /// </summary>
        Palette = 1,

        /// <summary>Four bits per value, as stored.</summary>
        Raw = 2,
    }

    /// <summary>
    ///     Encodes one chunk.
    /// </summary>
    /// <param name="blocks">Block ids, <c>ChuckFormat.ChunkSize</c> entries in <c>x, z, y</c> order.</param>
    /// <param name="meta">Block metadata, one nibble per block.</param>
    /// <param name="blockLight">Emitted light, one nibble per block.</param>
    /// <param name="skyLight">Sky light, one nibble per block.</param>
    public static byte[] Encode(
        ReadOnlySpan<byte> blocks,
        ReadOnlySpan<byte> meta,
        ReadOnlySpan<byte> blockLight,
        ReadOnlySpan<byte> skyLight)
    {
        ValidateSizes(blocks, meta, blockLight, skyLight);

        MemoryStream output = new(8192);
        output.WriteByte(Version);
        output.WriteByte((byte)SectionsPerChunk);

        Span<ushort> states = stackalloc ushort[SectionVolume];
        Span<byte> nibbles = stackalloc byte[SectionVolume];

        // A block state is twelve bits, so this covers every value one can take. Allocated once for
        // the whole chunk rather than per section; BuildPalette clears it.
        Span<short> indexOfState = stackalloc short[1 << 12];

        for (int section = 0; section < SectionsPerChunk; section++)
        {
            ReadSectionStates(blocks, meta, section, states);
            WriteStates(output, states, indexOfState);

            ReadSectionNibbles(blockLight, section, nibbles);
            WriteNibbles(output, nibbles);

            ReadSectionNibbles(skyLight, section, nibbles);
            WriteNibbles(output, nibbles);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Decodes one chunk into caller-owned arrays, which must already be chunk-sized. Writing
    ///     into the destination rather than allocating keeps the client's existing chunk object and
    ///     its heightmap, block entities and lighting state intact — the same contract
    ///     <c>Chunk.LoadFromPacket</c> has.
    /// </summary>
    public static void Decode(
        ReadOnlySpan<byte> blob,
        Span<byte> blocks,
        Span<byte> meta,
        Span<byte> blockLight,
        Span<byte> skyLight)
    {
        ValidateSizes(blocks, meta, blockLight, skyLight);

        int offset = 0;
        byte version = ReadByte(blob, ref offset);
        if (version != Version)
        {
            throw new InvalidDataException($"Chunk blob is version {version}; this build reads {Version}.");
        }

        int sections = ReadByte(blob, ref offset);
        if (sections != SectionsPerChunk)
        {
            throw new InvalidDataException(
                $"Chunk blob has {sections} sections; this world's chunks have {SectionsPerChunk}.");
        }

        Span<ushort> states = stackalloc ushort[SectionVolume];
        Span<byte> nibbles = stackalloc byte[SectionVolume];

        for (int section = 0; section < sections; section++)
        {
            ReadStates(blob, ref offset, states);
            WriteSectionStates(blocks, meta, section, states);

            ReadNibbles(blob, ref offset, nibbles);
            WriteSectionNibbles(blockLight, section, nibbles);

            ReadNibbles(blob, ref offset, nibbles);
            WriteSectionNibbles(skyLight, section, nibbles);
        }
    }

    // ---- section <-> flat array ----

    /// <summary>
    ///     The combined block state, and the reason ids and metadata are not given a palette separately.
    ///     They are strongly correlated — stone is always metadata zero, wool never is — so a palette
    ///     over the pair is smaller than two palettes over the parts, and the client needs both to
    ///     place a block anyway.
    /// </summary>
    private static ushort StateOf(byte id, int metadata) => (ushort)((id << 4) | (metadata & 0xF));

    /// <summary>
    ///     Canonical order within a section: <c>x</c>, then <c>z</c>, then <c>y</c>. It has to be
    ///     stated once and obeyed by both ends; it matches the chunk's own <c>x, z, y</c> layout so
    ///     the walk stays sequential in the source array.
    /// </summary>
    private static int LocalIndex(int x, int z, int y) => (x << 8) | (z << 4) | y;

    private static void ReadSectionStates(
        ReadOnlySpan<byte> blocks, ReadOnlySpan<byte> meta, int section, Span<ushort> states)
    {
        int baseY = section * SectionHeight;

        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int z = 0; z < ChunkWidth; z++)
            {
                for (int y = 0; y < SectionHeight; y++)
                {
                    int index = ChuckFormat.GetIndex(x, baseY + y, z);
                    states[LocalIndex(x, z, y)] = StateOf(blocks[index], GetNibble(meta, index));
                }
            }
        }
    }

    private static void WriteSectionStates(
        Span<byte> blocks, Span<byte> meta, int section, ReadOnlySpan<ushort> states)
    {
        int baseY = section * SectionHeight;

        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int z = 0; z < ChunkWidth; z++)
            {
                for (int y = 0; y < SectionHeight; y++)
                {
                    ushort state = states[LocalIndex(x, z, y)];
                    int index = ChuckFormat.GetIndex(x, baseY + y, z);

                    blocks[index] = (byte)(state >> 4);
                    SetNibble(meta, index, state & 0xF);
                }
            }
        }
    }

    private static void ReadSectionNibbles(ReadOnlySpan<byte> source, int section, Span<byte> values)
    {
        int baseY = section * SectionHeight;

        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int z = 0; z < ChunkWidth; z++)
            {
                for (int y = 0; y < SectionHeight; y++)
                {
                    values[LocalIndex(x, z, y)] = (byte)GetNibble(source, ChuckFormat.GetIndex(x, baseY + y, z));
                }
            }
        }
    }

    private static void WriteSectionNibbles(Span<byte> destination, int section, ReadOnlySpan<byte> values)
    {
        int baseY = section * SectionHeight;

        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int z = 0; z < ChunkWidth; z++)
            {
                for (int y = 0; y < SectionHeight; y++)
                {
                    SetNibble(destination, ChuckFormat.GetIndex(x, baseY + y, z), values[LocalIndex(x, z, y)]);
                }
            }
        }
    }

    /// <summary>
    ///     Nibble access by flat block index, matching <see cref="ChunkNibbleArray" />'s convention:
    ///     the low nibble holds even <c>y</c>. Duplicated here rather than borrowed because
    ///     <see cref="ChunkNibbleArray" /> indexes by coordinate and this walk already has the index.
    /// </summary>
    private static int GetNibble(ReadOnlySpan<byte> nibbles, int index) =>
        (index & 1) == 0 ? nibbles[index >> 1] & 0xF : (nibbles[index >> 1] >> 4) & 0xF;

    private static void SetNibble(Span<byte> nibbles, int index, int value)
    {
        ref byte cell = ref nibbles[index >> 1];

        cell = (index & 1) == 0
            ? (byte)((cell & 0xF0) | (value & 0xF))
            : (byte)((cell & 0x0F) | ((value & 0xF) << 4));
    }

    // ---- state field ----

    private static void WriteStates(Stream output, ReadOnlySpan<ushort> states, Span<short> indexOfState)
    {
        Span<ushort> palette = stackalloc ushort[MaxPaletteSize];
        int paletteSize = BuildPalette(states, palette, indexOfState);

        if (paletteSize == 1)
        {
            output.WriteByte((byte)StateEncoding.Uniform);
            WriteUInt16(output, states[0]);
            return;
        }

        if (paletteSize == 0)
        {
            // More distinct states than a palette can pay for. Ids and metadata go out in their
            // original shape, which is exactly what the inherited format sends.
            output.WriteByte((byte)StateEncoding.Raw);
            for (int i = 0; i < SectionVolume; i++)
            {
                output.WriteByte((byte)(states[i] >> 4));
            }

            for (int i = 0; i < SectionVolume; i += 2)
            {
                output.WriteByte((byte)((states[i] & 0xF) | ((states[i + 1] & 0xF) << 4)));
            }

            return;
        }

        output.WriteByte((byte)StateEncoding.Palette);
        output.WriteByte((byte)(paletteSize - 1));
        for (int i = 0; i < paletteSize; i++)
        {
            WriteUInt16(output, palette[i]);
        }

        int bits = BitsFor(paletteSize);
        Span<ushort> indices = stackalloc ushort[SectionVolume];
        for (int i = 0; i < SectionVolume; i++)
        {
            indices[i] = (ushort)indexOfState[states[i]];
        }

        WritePacked(output, indices, bits);
    }

    private static void ReadStates(ReadOnlySpan<byte> blob, ref int offset, Span<ushort> states)
    {
        StateEncoding encoding = (StateEncoding)ReadByte(blob, ref offset);

        switch (encoding)
        {
            case StateEncoding.Uniform:
                states.Fill(ReadUInt16(blob, ref offset));
                return;

            case StateEncoding.Raw:
                {
                    ReadOnlySpan<byte> ids = Take(blob, ref offset, SectionVolume);
                    ReadOnlySpan<byte> metadata = Take(blob, ref offset, SectionVolume / 2);

                    for (int i = 0; i < SectionVolume; i++)
                    {
                        int nibble = (i & 1) == 0 ? metadata[i >> 1] & 0xF : (metadata[i >> 1] >> 4) & 0xF;
                        states[i] = StateOf(ids[i], nibble);
                    }

                    return;
                }

            case StateEncoding.Palette:
                {
                    int paletteSize = ReadByte(blob, ref offset) + 1;
                    Span<ushort> palette = stackalloc ushort[paletteSize];
                    for (int i = 0; i < paletteSize; i++)
                    {
                        palette[i] = ReadUInt16(blob, ref offset);
                    }

                    int bits = BitsFor(paletteSize);
                    ReadOnlySpan<byte> packed = Take(blob, ref offset, PackedLength(SectionVolume, bits));

                    for (int i = 0; i < SectionVolume; i++)
                    {
                        int index = Unpack(packed, i, bits);
                        if (index >= paletteSize)
                        {
                            throw new InvalidDataException(
                                $"Chunk blob references palette entry {index} of {paletteSize}.");
                        }

                        states[i] = palette[index];
                    }

                    return;
                }

            default:
                throw new InvalidDataException($"Unknown block state encoding {(byte)encoding}.");
        }
    }

    // ---- nibble field ----

    private static void WriteNibbles(Stream output, ReadOnlySpan<byte> values)
    {
        // Sixteen possible levels, so the same reverse-map trick as the state palette, at a size
        // where it is simply free.
        Span<sbyte> indexOfValue = stackalloc sbyte[16];
        indexOfValue.Fill(-1);
        Span<byte> palette = stackalloc byte[16];
        int paletteSize = 0;

        foreach (byte value in values)
        {
            if (indexOfValue[value] >= 0)
            {
                continue;
            }

            indexOfValue[value] = (sbyte)paletteSize;
            palette[paletteSize++] = value;
        }

        if (paletteSize == 1)
        {
            output.WriteByte((byte)NibbleEncoding.Uniform);
            output.WriteByte(values[0]);
            return;
        }

        int bits = BitsFor(paletteSize);

        // Four bits is what the array already costs, so a palette only earns its table below that.
        if (bits >= 4)
        {
            output.WriteByte((byte)NibbleEncoding.Raw);
            for (int i = 0; i < SectionVolume; i += 2)
            {
                output.WriteByte((byte)(values[i] | (values[i + 1] << 4)));
            }

            return;
        }

        output.WriteByte((byte)NibbleEncoding.Palette);
        output.WriteByte((byte)(paletteSize - 1));
        for (int i = 0; i < paletteSize; i++)
        {
            output.WriteByte(palette[i]);
        }

        Span<ushort> indices = stackalloc ushort[SectionVolume];
        for (int i = 0; i < SectionVolume; i++)
        {
            indices[i] = (ushort)indexOfValue[values[i]];
        }

        WritePacked(output, indices, bits);
    }

    private static void ReadNibbles(ReadOnlySpan<byte> blob, ref int offset, Span<byte> values)
    {
        NibbleEncoding encoding = (NibbleEncoding)ReadByte(blob, ref offset);

        switch (encoding)
        {
            case NibbleEncoding.Uniform:
                values.Fill(ReadByte(blob, ref offset));
                return;

            case NibbleEncoding.Raw:
                {
                    ReadOnlySpan<byte> packed = Take(blob, ref offset, SectionVolume / 2);
                    for (int i = 0; i < SectionVolume; i += 2)
                    {
                        values[i] = (byte)(packed[i >> 1] & 0xF);
                        values[i + 1] = (byte)((packed[i >> 1] >> 4) & 0xF);
                    }

                    return;
                }

            case NibbleEncoding.Palette:
                {
                    int paletteSize = ReadByte(blob, ref offset) + 1;
                    ReadOnlySpan<byte> palette = Take(blob, ref offset, paletteSize);

                    int bits = BitsFor(paletteSize);
                    ReadOnlySpan<byte> packed = Take(blob, ref offset, PackedLength(SectionVolume, bits));

                    for (int i = 0; i < SectionVolume; i++)
                    {
                        int index = Unpack(packed, i, bits);
                        if (index >= paletteSize)
                        {
                            throw new InvalidDataException(
                                $"Chunk blob references light palette entry {index} of {paletteSize}.");
                        }

                        values[i] = palette[index];
                    }

                    return;
                }

            default:
                throw new InvalidDataException($"Unknown light encoding {(byte)encoding}.");
        }
    }

    // ---- palette and bit packing ----

    /// <summary>
    ///     Collects distinct states in first-seen order, and fills
    ///     <paramref name="indexOfState" /> with the reverse mapping. Returns 0 when there are more
    ///     than <see cref="MaxPaletteSize" />, meaning the caller should fall back to raw.
    ///     <para>
    ///         The reverse map is what makes this affordable. A state is twelve bits, so every
    ///         possible value indexes a flat table directly and both membership and index lookup are
    ///         one load. Scanning the palette instead is O(blocks x palette) — measured at 1 ms per
    ///         chunk against 0.3 ms to simply zlib the whole raw chunk, which would have made the
    ///         encoding a CPU regression regardless of what it saved on the wire.
    ///     </para>
    /// </summary>
    private static int BuildPalette(ReadOnlySpan<ushort> states, Span<ushort> palette, Span<short> indexOfState)
    {
        indexOfState.Fill(-1);
        int size = 0;

        foreach (ushort state in states)
        {
            if (indexOfState[state] >= 0)
            {
                continue;
            }

            if (size == MaxPaletteSize)
            {
                return 0;
            }

            indexOfState[state] = (short)size;
            palette[size++] = state;
        }

        return size;
    }

    /// <summary>Narrowest index width that addresses <paramref name="paletteSize" /> entries.</summary>
    private static int BitsFor(int paletteSize)
    {
        int bits = 1;
        while (1 << bits < paletteSize)
        {
            bits++;
        }

        return bits;
    }

    private static int PackedLength(int count, int bits) => ((count * bits) + 7) / 8;

    /// <summary>
    ///     Packs indices least-significant-bit first, straddling byte boundaries.
    ///     <para>
    ///         Straddling rather than the aligned scheme modern Minecraft uses, because the widths
    ///         that differ are the ones that matter here. An aligned packer wastes the remainder of
    ///         every word, which for a five-bit index means 4 of every 64 bits; more to the point it
    ///         invites rounding widths up to powers of two, and a 20-state section rounded from 5
    ///         bits to 8 is 60% larger for nothing.
    ///     </para>
    /// </summary>
    private static void WritePacked(Stream output, ReadOnlySpan<ushort> indices, int bits)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bits, 8);

        byte[] packed = new byte[PackedLength(indices.Length, bits)];
        int bitPosition = 0;

        foreach (ushort index in indices)
        {
            int byteIndex = bitPosition >> 3;
            int bitOffset = bitPosition & 7;
            uint shifted = (uint)index << bitOffset;

            packed[byteIndex] |= (byte)shifted;

            // At most two bytes: eight bits offset by at most seven spans fifteen.
            if (bitOffset + bits > 8)
            {
                packed[byteIndex + 1] |= (byte)(shifted >> 8);
            }

            bitPosition += bits;
        }

        output.Write(packed);
    }

    private static int Unpack(ReadOnlySpan<byte> packed, int index, int bits)
    {
        int bitPosition = index * bits;
        int byteIndex = bitPosition >> 3;
        int bitOffset = bitPosition & 7;

        uint window = packed[byteIndex];
        if (byteIndex + 1 < packed.Length)
        {
            window |= (uint)packed[byteIndex + 1] << 8;
        }

        return (int)((window >> bitOffset) & ((1u << bits) - 1));
    }

    // ---- primitives ----

    private static void WriteUInt16(Stream output, ushort value)
    {
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> blob, ref int offset)
    {
        ReadOnlySpan<byte> pair = Take(blob, ref offset, 2);
        return (ushort)((pair[0] << 8) | pair[1]);
    }

    private static byte ReadByte(ReadOnlySpan<byte> blob, ref int offset) => Take(blob, ref offset, 1)[0];

    /// <summary>
    ///     Advances past <paramref name="count" /> bytes, refusing to read past the end.
    ///     <para>
    ///         Every read goes through here so a truncated or hostile blob fails as one exception
    ///         with a position in it, rather than as an index-out-of-range from whichever loop
    ///         happened to run off the end.
    ///     </para>
    /// </summary>
    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> blob, ref int offset, int count)
    {
        if (count < 0 || offset + count > blob.Length)
        {
            throw new InvalidDataException(
                $"Chunk blob is truncated: wanted {count} bytes at {offset} of {blob.Length}.");
        }

        ReadOnlySpan<byte> slice = blob.Slice(offset, count);
        offset += count;
        return slice;
    }

    private static void ValidateSizes(
        ReadOnlySpan<byte> blocks,
        ReadOnlySpan<byte> meta,
        ReadOnlySpan<byte> blockLight,
        ReadOnlySpan<byte> skyLight)
    {
        int volume = ChuckFormat.ChunkSize;

        ArgumentOutOfRangeException.ThrowIfNotEqual(blocks.Length, volume, nameof(blocks));
        ArgumentOutOfRangeException.ThrowIfNotEqual(meta.Length, volume / 2, nameof(meta));
        ArgumentOutOfRangeException.ThrowIfNotEqual(blockLight.Length, volume / 2, nameof(blockLight));
        ArgumentOutOfRangeException.ThrowIfNotEqual(skyLight.Length, volume / 2, nameof(skyLight));
    }
}

using System.Buffers.Binary;
using System.Text;
using BetaSharp.Util;

namespace BetaSharp;

internal static class StreamExtensions
{
    /// <summary>
    ///     Bytes <see cref="WriteVarInt" /> will emit for <paramref name="value" />, so
    ///     <c>Packet.Size()</c> can be computed without serialising.
    /// </summary>
    public static int VarIntSize(int value)
    {
        uint remaining = (uint)value;
        int bytes = 1;

        while (remaining >= 0x80)
        {
            remaining >>= 7;
            bytes++;
        }

        return bytes;
    }

    /// <summary>
    ///     Maps a signed value onto the unsigned range so that small magnitudes of either sign stay
    ///     small: 0, -1, 1, -2, 2 become 0, 1, 2, 3, 4.
    ///     <para>
    ///         <see cref="WriteVarInt" /> alone is no use for signed quantities — it casts through
    ///         <see cref="uint" />, so every negative value occupies the full five bytes. That is
    ///         exactly backwards for a delta, where a step of -1 is as common as +1.
    ///     </para>
    /// </summary>
    public static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));

    public static int UnZigZag(uint value) => (int)(value >> 1) ^ -(int)(value & 1);

    /// <summary>Bytes <see cref="Stream.WriteZigZag" /> will emit, for computing a size without serialising.</summary>
    public static int ZigZagSize(int value)
    {
        uint remaining = ZigZag(value);
        int bytes = 1;

        while (remaining >= 0x80)
        {
            remaining >>= 7;
            bytes++;
        }

        return bytes;
    }

    /// <summary>Bytes <see cref="Stream.WriteByteArray" /> will emit, for computing a size without serialising.</summary>
    public static int ByteArraySize(byte[] value) => VarIntSize(value.Length) + value.Length;

    /// <summary>
    ///     Bytes <see cref="Stream.WriteResourceLocation" /> will emit. Mirrors that writer's
    ///     shortcut for the default namespace, which travels as a single sentinel byte rather than
    ///     as its name.
    /// </summary>
    public static int ResourceLocationSize(ResourceLocation value) =>
        (value.Namespace.GetHashCode() == 0 ? 1 : 1 + value.Namespace.ToString().Length)
        + 1
        + value.Path.Length;

    extension(Stream stream)
    {
        public void WriteBoolean(bool value)
        {
            stream.WriteByte((byte)(value ? 1 : 0));
        }

        public void WriteShort(short value)
        {
            Span<byte> span = stackalloc byte[sizeof(short)];
            BinaryPrimitives.WriteInt16BigEndian(span, value);
            stream.Write(span);
        }

        public void WriteUShort(ushort value)
        {
            Span<byte> span = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16BigEndian(span, value);
            stream.Write(span);
        }

        public void WriteInt(int value)
        {
            Span<byte> span = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(span, value);
            stream.Write(span);
        }

        public void WriteFloat(float value)
        {
            Span<byte> span = stackalloc byte[sizeof(float)];
            BinaryPrimitives.WriteSingleBigEndian(span, value);
            stream.Write(span);
        }

        public void WriteDouble(double value)
        {
            Span<byte> span = stackalloc byte[sizeof(double)];
            BinaryPrimitives.WriteDoubleBigEndian(span, value);
            stream.Write(span);
        }

        public void WriteLong(long value)
        {
            Span<byte> span = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(span, value);
            stream.Write(span);
        }

        /// <summary>
        /// Write as fixed length UTF-8 string
        /// </summary>
        public void WriteString(string value)
        {
            byte[] buffer = ModifiedUtf8.GetBytes(value);

            stream.WriteUShort((ushort)buffer.Length);
            stream.Write(buffer);
        }

        /// <summary>
        /// Write as fixed length UTF-16 string
        /// </summary>
        public void WriteLongString(string value)
        {
            stream.WriteUShort((ushort)value.Length);
            stream.Write(Encoding.BigEndianUnicode.GetBytes(value));
        }

        public bool ReadBoolean()
        {
            return stream.ReadByte() > 0;
        }

        public short ReadShort()
        {
            Span<byte> span = stackalloc byte[sizeof(short)];
            stream.ReadExactly(span);

            return BinaryPrimitives.ReadInt16BigEndian(span);
        }

        public ushort ReadUShort()
        {
            Span<byte> span = stackalloc byte[sizeof(ushort)];
            stream.ReadExactly(span);

            return BinaryPrimitives.ReadUInt16BigEndian(span);
        }

        public int ReadInt()
        {
            Span<byte> span = stackalloc byte[sizeof(int)];
            stream.ReadExactly(span);

            return BinaryPrimitives.ReadInt32BigEndian(span);
        }

        public float ReadFloat()
        {
            Span<byte> span = stackalloc byte[sizeof(float)];
            stream.ReadExactly(span);

            return BinaryPrimitives.ReadSingleBigEndian(span);
        }

        public double ReadDouble()
        {
            Span<byte> span = stackalloc byte[sizeof(double)];
            stream.ReadExactly(span);

            return BinaryPrimitives.ReadDoubleBigEndian(span);
        }

        public long ReadLong()
        {
            Span<byte> span = stackalloc byte[sizeof(long)];
            stream.ReadExactly(span);

            return BinaryPrimitives.ReadInt64BigEndian(span);
        }

        /// <summary>
        /// Read fixed length UTF-8 string
        /// </summary>
        public string ReadString()
        {
            ushort length = stream.ReadUShort();
            byte[] buffer = new byte[length];

            stream.ReadExactly(buffer);

            return ModifiedUtf8.GetString(buffer);
        }

        /// <summary>
        /// Read fixed length UTF-16 string
        /// </summary>
        public string ReadLongString(ushort maximumLength = ushort.MaxValue)
        {
            ushort length = stream.ReadUShort();
            byte[] buffer = new byte[length * 2];

            if (length > maximumLength)
            {
                throw new IOException("Received string length longer than maximum allowed (" + buffer.Length + " > " + maximumLength + ")");
            }

            stream.ReadExactly(buffer);

            return Encoding.BigEndianUnicode.GetString(buffer);
        }

        public string ReadAscii256()
        {
            int length = stream.ReadByte();
            byte[] buffer = new byte[length];
            stream.ReadExactly(buffer);
            return Encoding.ASCII.GetString(buffer);
        }

        public void WriteAscii256(string value)
        {
            byte[] buffer = Encoding.ASCII.GetBytes(value);
            stream.WriteByte((byte)buffer.Length);
            stream.Write(buffer);
        }

        public Namespace ReadNamespace()
        {
            int length = stream.ReadByte();
            if (length == 128) return Namespace.BetaSharp;
            byte[] buffer = new byte[length];
            stream.ReadExactly(buffer);
            return Namespace.Get(Encoding.ASCII.GetString(buffer));
        }

        public void WriteNamespace(Namespace ns)
        {
            if (ns.GetHashCode() == 0) stream.WriteByte(128);
            else stream.WriteAscii256(ns.ToString());
        }

        public ResourceLocation ReadResourceLocation()
        {
            return new ResourceLocation(ReadNamespace(stream), stream.ReadAscii256());
        }

        public void WriteResourceLocation(ResourceLocation resourceLocation)
        {
            stream.WriteNamespace(resourceLocation.Namespace);
            stream.WriteAscii256(resourceLocation.Path);
        }

        /// <summary>
        ///     Writes a 32-bit value as LEB128: seven bits per byte, low group first, high bit set
        ///     on every byte but the last. One to five bytes.
        ///     <para>
        ///         Deliberately not big-endian like the rest of this file. A varint is a compact
        ///         encoding rather than a fixed-width integer, and the message layer uses it for
        ///         lengths and IDs where small values dominate — a message ID below 128 costs one
        ///         byte instead of four.
        ///     </para>
        /// </summary>
        public void WriteVarInt(int value)
        {
            uint remaining = (uint)value;

            while (remaining >= 0x80)
            {
                stream.WriteByte((byte)(remaining | 0x80));
                remaining >>= 7;
            }

            stream.WriteByte((byte)remaining);
        }

        /// <summary>
        ///     Reads a value written by <see cref="WriteVarInt" />. Negative values round-trip: they
        ///     are cast through <see cref="uint" />, so they occupy the full five bytes.
        /// </summary>
        /// <exception cref="InvalidDataException">
        ///     The encoding runs past five bytes. An unbounded read is a denial of service — a peer
        ///     sending 0x80 forever would otherwise spin here — so the length is capped rather than
        ///     trusted.
        /// </exception>
        /// <exception cref="EndOfStreamException">The stream ends mid-value.</exception>
        public int ReadVarInt()
        {
            int result = 0;
            int shift = 0;

            while (true)
            {
                int read = stream.ReadByte();
                if (read < 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading a VarInt.");
                }

                result |= (read & 0x7F) << shift;

                if ((read & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
                if (shift >= 35)
                {
                    throw new InvalidDataException("VarInt is longer than the five bytes a 32-bit value can occupy.");
                }
            }
        }

        /// <summary>
        ///     Writes a signed value as a zig-zagged varint, so a small delta of either sign costs
        ///     one byte. See <see cref="StreamExtensions.ZigZag" />.
        /// </summary>
        public void WriteZigZag(int value)
        {
            uint remaining = ZigZag(value);

            while (remaining >= 0x80)
            {
                stream.WriteByte((byte)(remaining | 0x80));
                remaining >>= 7;
            }

            stream.WriteByte((byte)remaining);
        }

        /// <summary>Reads a value written by <see cref="WriteZigZag" />.</summary>
        public int ReadZigZag() => UnZigZag((uint)stream.ReadVarInt());

        /// <summary>
        ///     Writes a length-prefixed blob. The length is a varint rather than the fixed
        ///     <see cref="ushort" /> the string writers use, because blobs are the one payload here
        ///     that legitimately exceeds 64 KB — a chunk does.
        /// </summary>
        public void WriteByteArray(byte[] value)
        {
            ArgumentNullException.ThrowIfNull(value);

            stream.WriteVarInt(value.Length);
            stream.Write(value);
        }

        /// <summary>
        ///     Reads a blob written by <see cref="WriteByteArray" />, refusing one longer than
        ///     <paramref name="maximumLength" />.
        ///     <para>
        ///         The bound is checked before the allocation rather than after it. A limit applied
        ///         to the result is not a limit — by then the memory the sender asked for has
        ///         already been taken.
        ///     </para>
        /// </summary>
        public byte[] ReadByteArray(int maximumLength = int.MaxValue)
        {
            int length = stream.ReadVarInt();

            if (length < 0 || length > maximumLength)
            {
                throw new InvalidDataException(
                    $"Blob declares {length} bytes; the accepted range is 0 to {maximumLength}.");
            }

            byte[] buffer = new byte[length];
            stream.ReadExactly(buffer);

            return buffer;
        }

        public byte[] ReadUntil(byte terminator)
        {
            List<byte> buffer = new();

            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading until terminator " + terminator);
                }

                if (b == terminator)
                {
                    break;
                }

                buffer.Add((byte)b);
            }

            return buffer.ToArray();
        }
    }
}

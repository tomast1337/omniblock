using System.Buffers.Binary;
using System.Text;
using BetaSharp.Util;

namespace BetaSharp;

internal static class StreamExtensions
{
    extension(Stream stream)
    {
        public void WriteBoolean(bool value)
        {
            stream.WriteByte((byte) (value ? 1 : 0));
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

        public void WriteString(string value)
        {
            byte[] buffer = ModifiedUtf8.GetBytes(value);

            stream.WriteUShort((ushort)buffer.Length);
            stream.Write(buffer);
        }

        public void WriteLongString(string value)
        {
            stream.WriteUShort((ushort)value.Length);

            // foreach (char item in value)
            // {
            //     stream.WriteByte((byte)item);
            //     stream.WriteByte(0);
            // }

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

        public string ReadString()
        {
            ushort length = stream.ReadUShort();
            byte[] buffer = new byte[length];

            stream.ReadExactly(buffer);

            return ModifiedUtf8.GetString(buffer);
        }

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
    }

    extension(FileStream stream)
    {
        public long GetFilePointer()
        {
            return stream.Position;
        }

        public void Seek(long pos)
        {
            if (pos < 0)
            {
                throw new IOException("Negative seek offset.");
            }

            stream.Seek(pos, SeekOrigin.Begin);
        }

        public long Length()
        {
            return stream.Length;
        }

        public void SetLength(long newLength)
        {
            if (newLength < 0)
            {
                throw new IOException("Negative length.");
            }

            stream.SetLength(newLength);
        }

        public int Read()
        {
            return stream.ReadByte();
        }

        public int Read(byte[] buffer)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            return stream.Read(buffer, 0, buffer.Length);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            return stream.Read(buffer, offset, count);
        }

        public void ReadExactly(byte[] buffer)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            stream.ReadExactly(buffer);
        }

        public void ReadExactly(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            stream.ReadExactly(buffer.AsSpan(offset, count));
        }

        public void Write(int value)
        {
            stream.WriteByte(unchecked((byte)value));
        }

        public void Write(byte[] buffer)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            stream.Write(buffer, 0, buffer.Length);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            stream.Write(buffer, offset, count);
        }

        public int SkipBytes(int n)
        {
            if (n <= 0)
            {
                return 0;
            }

            long current = stream.Position;
            long target = Math.Min(stream.Length, current + n);
            stream.Position = target;
            return (int)(target - current);
        }

        public bool ReadBooleanExact()
        {
            int value = stream.ReadByte();
            if (value < 0)
            {
                throw new EndOfStreamException();
            }

            return value != 0;
        }

        public byte ReadByteExact()
        {
            int v = stream.ReadByte();
            if (v < 0)
            {
                throw new EndOfStreamException();
            }

            return (byte)v;
        }

        public sbyte ReadSignedByte()
        {
            return unchecked((sbyte)stream.ReadByteExact());
        }

        public short ReadShort()
        {
            return ((Stream)stream).ReadShort();
        }

        public ushort ReadUnsignedShort()
        {
            return ((Stream)stream).ReadUShort();
        }

        public char ReadChar()
        {
            return (char)stream.ReadUnsignedShort();
        }

        public int ReadInt()
        {
            return ((Stream)stream).ReadInt();
        }

        public long ReadLong()
        {
            return ((Stream)stream).ReadLong();
        }

        public float ReadFloat()
        {
            return ((Stream)stream).ReadFloat();
        }

        public double ReadDouble()
        {
            return ((Stream)stream).ReadDouble();
        }

        public void WriteBoolean(bool value)
        {
            ((Stream)stream).WriteBoolean(value);
        }

        public void WriteByte(byte value)
        {
            stream.WriteByte(value);
        }

        public void WriteSignedByte(sbyte value)
        {
            stream.WriteByte(unchecked((byte)value));
        }

        public void WriteShort(short value)
        {
            ((Stream)stream).WriteShort(value);
        }

        public void WriteChar(char value)
        {
            ((Stream)stream).WriteUShort(value);
        }

        public void WriteUnsignedShort(ushort value)
        {
            ((Stream)stream).WriteUShort(value);
        }

        public void WriteInt(int value)
        {
            ((Stream)stream).WriteInt(value);
        }

        public void WriteLong(long value)
        {
            ((Stream)stream).WriteLong(value);
        }

        public void WriteFloat(float value)
        {
            ((Stream)stream).WriteFloat(value);
        }

        public void WriteDouble(double value)
        {
            ((Stream)stream).WriteDouble(value);
        }

        public void WriteUtf(string value)
        {
            ((Stream)stream).WriteString(value);
        }

        public string ReadUtf()
        {
            return ((Stream)stream).ReadString();
        }

        public void Flush(bool includeMetadata)
        {
            stream.Flush(includeMetadata);
        }
    }
}

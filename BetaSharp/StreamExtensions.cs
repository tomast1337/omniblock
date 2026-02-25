using System.Buffers.Binary;
using System.Text;
using BetaSharp.Util;

namespace BetaSharp;

internal static class StreamExtensions
{
    public static void WriteShort(this Stream stream, short value)
    {
        Span<byte> span = stackalloc byte[sizeof(short)];
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        stream.Write(span);
    }

    public static void WriteUShort(this Stream stream, ushort value)
    {
        Span<byte> span = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        stream.Write(span);
    }

    public static void WriteInt(this Stream stream, int value)
    {
        Span<byte> span = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        stream.Write(span);
    }

    public static void WriteFloat(this Stream stream, float value)
    {
        Span<byte> span = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleBigEndian(span, value);
        stream.Write(span);
    }

    public static void WriteDouble(this Stream stream, double value)
    {
        Span<byte> span = stackalloc byte[sizeof(double)];
        BinaryPrimitives.WriteDoubleBigEndian(span, value);
        stream.Write(span);
    }

    public static void WriteLong(this Stream stream, long value)
    {
        Span<byte> span = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        stream.Write(span);
    }

    public static void WriteString(this Stream stream, string value)
    {
        byte[] buffer = ModifiedUtf8.GetBytes(value);

        stream.WriteUShort((ushort)buffer.Length);
        stream.Write(buffer);
    }

    public static void WriteLongString(this Stream stream, string value)
    {
        stream.WriteUShort((ushort)value.Length);

        foreach (char item in value)
        {
            stream.WriteByte((byte)item);
        }
    }

    public static short ReadShort(this Stream stream)
    {
        Span<byte> span = stackalloc byte[sizeof(short)];
        stream.ReadExactly(span);

        return BinaryPrimitives.ReadInt16BigEndian(span);
    }

    public static ushort ReadUShort(this Stream stream)
    {
        Span<byte> span = stackalloc byte[sizeof(ushort)];
        stream.ReadExactly(span);

        return BinaryPrimitives.ReadUInt16BigEndian(span);
    }

    public static int ReadInt(this Stream stream)
    {
        Span<byte> span = stackalloc byte[sizeof(int)];
        stream.ReadExactly(span);

        return BinaryPrimitives.ReadInt32BigEndian(span);
    }

    public static float ReadFloat(this Stream stream)
    {
        Span<byte> span = stackalloc byte[sizeof(float)];
        stream.ReadExactly(span);

        return BinaryPrimitives.ReadSingleBigEndian(span);
    }

    public static double ReadDouble(this Stream stream)
    {
        Span<byte> span = stackalloc byte[sizeof(double)];
        stream.ReadExactly(span);

        return BinaryPrimitives.ReadDoubleBigEndian(span);
    }

    public static long ReadLong(this Stream stream)
    {
        Span<byte> span = stackalloc byte[sizeof(long)];
        stream.ReadExactly(span);

        return BinaryPrimitives.ReadInt64BigEndian(span);
    }

    public static string ReadString(this Stream stream)
    {
        ushort length = stream.ReadUShort();
        byte[] buffer = new byte[length];

        stream.ReadExactly(buffer);

        return ModifiedUtf8.GetString(buffer);
    }

    public static string ReadLongString(this Stream stream, ushort maximumLength = ushort.MaxValue)
    {
        byte[] buffer = new byte[stream.ReadUShort()];

        if (buffer.Length > maximumLength)
        {
            throw new IOException("Received string length longer than maximum allowed (" + buffer.Length + " > " + maximumLength + ")");
        }

        stream.ReadExactly(buffer);

        var builder = new StringBuilder();

        for (int i = 0; i < buffer.Length; ++i)
        {
            builder.Append(stream.ReadByte());
        }

        return builder.ToString();
    }
}

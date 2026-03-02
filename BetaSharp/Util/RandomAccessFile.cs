using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace BetaSharp.Util;

public sealed class RandomAccessFile : IDisposable
{
    private readonly FileStream _stream;
    private readonly bool _syncMetadata;
    private readonly bool _syncDataOnly;
    private readonly bool _leaveOpen;

    public RandomAccessFile(string path, string mode)
        : this(path, mode, leaveOpen: false)
    {
    }

    public RandomAccessFile(string path, string mode, bool leaveOpen)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (mode is null) throw new ArgumentNullException(nameof(mode));

        _leaveOpen = leaveOpen;

        FileAccess access;
        FileMode fileMode;

        switch (mode)
        {
            case "r":
                access = FileAccess.Read;
                fileMode = FileMode.Open;
                break;

            case "rw":
            case "rws":
            case "rwd":
                access = FileAccess.ReadWrite;
                fileMode = FileMode.OpenOrCreate;
                _syncMetadata = mode == "rws";
                _syncDataOnly = mode == "rwd";
                break;

            default:
                throw new ArgumentException($"Unsupported mode '{mode}'. Use 'r', 'rw', 'rws', or 'rwd'.", nameof(mode));
        }

        _stream = new FileStream(
            path,
            fileMode,
            access,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.RandomAccess);

        if (mode == "r" && !_stream.CanRead)
            throw new IOException("File is not readable.");

        if (mode != "r" && !_stream.CanWrite)
            throw new IOException("File is not writable.");
    }

    public void Dispose()
    {
        if (_leaveOpen) return;
        _stream.Dispose();
    }

    public FileStream BaseStream => _stream;

    public long GetFilePointer() => _stream.Position;

    public void Seek(long pos)
    {
        if (pos < 0) throw new IOException("Negative seek offset.");
        _stream.Seek(pos, SeekOrigin.Begin);
    }

    public long Length() => _stream.Length;

    public void SetLength(long newLength)
    {
        if (newLength < 0) throw new IOException("Negative length.");
        _stream.SetLength(newLength);
        SyncIfNeeded(forceMetadata: true);
    }

    public void Close() => Dispose();

    public int Read() => _stream.ReadByte();

    public int Read(byte[] b) => Read(b, 0, b?.Length ?? 0);

    public int Read(byte[] b, int off, int len)
    {
        if (b is null) throw new ArgumentNullException(nameof(b));
        return _stream.Read(b, off, len);
    }

    public void ReadFully(byte[] b) => ReadFully(b, 0, b?.Length ?? 0);

    public void ReadFully(byte[] b, int off, int len)
    {
        if (b is null) throw new ArgumentNullException(nameof(b));
        int total = 0;
        while (total < len)
        {
            int n = _stream.Read(b, off + total, len - total);
            if (n == 0) throw new EndOfStreamException();
            total += n;
        }
    }

    public void Write(int b)
    {
        _stream.WriteByte((byte)b);
        SyncIfNeeded(forceMetadata: false);
    }

    public void Write(byte[] b) => Write(b, 0, b?.Length ?? 0);

    public void Write(byte[] b, int off, int len)
    {
        if (b is null) throw new ArgumentNullException(nameof(b));
        _stream.Write(b, off, len);
        SyncIfNeeded(forceMetadata: false);
    }

    public void WriteBytes(string s)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));

        Span<byte> buf = s.Length <= 4096 ? stackalloc byte[s.Length] : new byte[s.Length];
        for (int i = 0; i < s.Length; i++)
            buf[i] = unchecked((byte)s[i]);
        _stream.Write(buf);
        SyncIfNeeded(forceMetadata: false);
    }

    public bool ReadBoolean() => ReadByte() != 0;

    public byte ReadByte()
    {
        int v = _stream.ReadByte();
        if (v < 0) throw new EndOfStreamException();
        return (byte)v;
    }

    public sbyte ReadSignedByte() => unchecked((sbyte)ReadByte());

    public short ReadShort()
    {
        Span<byte> b = stackalloc byte[2];
        ReadFully(b);
        return BinaryPrimitives.ReadInt16BigEndian(b);
    }

    public ushort ReadUnsignedShort()
    {
        Span<byte> b = stackalloc byte[2];
        ReadFully(b);
        return BinaryPrimitives.ReadUInt16BigEndian(b);
    }

    public char ReadChar()
    {
        return (char)ReadUnsignedShort();
    }

    public int ReadInt()
    {
        Span<byte> b = stackalloc byte[4];
        ReadFully(b);
        return BinaryPrimitives.ReadInt32BigEndian(b);
    }

    public long ReadLong()
    {
        Span<byte> b = stackalloc byte[8];
        ReadFully(b);
        return BinaryPrimitives.ReadInt64BigEndian(b);
    }

    public float ReadFloat()
    {
        int bits = ReadInt();
        return BitConverter.Int32BitsToSingle(bits);
    }

    public double ReadDouble()
    {
        long bits = ReadLong();
        return BitConverter.Int64BitsToDouble(bits);
    }

    public void WriteBoolean(bool v) => WriteByte(v ? (byte)1 : (byte)0);

    public void WriteByte(byte v)
    {
        _stream.WriteByte(v);
        SyncIfNeeded(forceMetadata: false);
    }

    public void WriteSignedByte(sbyte v) => WriteByte(unchecked((byte)v));

    public void WriteShort(short v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, v);
        _stream.Write(b);
        SyncIfNeeded(forceMetadata: false);
    }

    public void WriteChar(char v) => WriteUnsignedShort(v);

    public void WriteUnsignedShort(ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        _stream.Write(b);
        SyncIfNeeded(forceMetadata: false);
    }

    public void WriteInt(int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        _stream.Write(b);
        SyncIfNeeded(forceMetadata: false);
    }

    public void WriteLong(long v)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        _stream.Write(b);
        SyncIfNeeded(forceMetadata: false);
    }

    public void WriteFloat(float v) => WriteInt(BitConverter.SingleToInt32Bits(v));

    public void WriteDouble(double v) => WriteLong(BitConverter.DoubleToInt64Bits(v));

    public void WriteUTF(string s)
    {
        if (s is null) throw new ArgumentNullException(nameof(s));

        byte[] utf8 = Encoding.UTF8.GetBytes(s);
        if (utf8.Length > ushort.MaxValue)
            throw new IOException("UTF string too long (exceeds 65535 bytes).");

        WriteUnsignedShort((ushort)utf8.Length);
        Write(utf8);
    }

    public int SkipBytes(int n)
    {
        if (n <= 0) return 0;

        long current = _stream.Position;
        long target = Math.Min(_stream.Length, current + n);
        _stream.Position = target;
        return (int)(target - current);
    }

    private void ReadFully(Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = _stream.Read(buffer.Slice(total));
            if (n == 0) throw new EndOfStreamException();
            total += n;
        }
    }

    private void SyncIfNeeded(bool forceMetadata)
    {
        if (_syncMetadata || _syncDataOnly)
        {
            bool flushToDisk = _syncMetadata || forceMetadata;
            _stream.Flush(flushToDisk);
        }
    }
}

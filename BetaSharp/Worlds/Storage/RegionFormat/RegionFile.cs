using System.IO.Compression;
using BetaSharp.Worlds.Chunks.Storage;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Worlds.Storage.RegionFormat;

internal class RegionFile
{
    private static readonly byte[] s_emptySector = new byte[4096];
    private readonly ILogger<RegionFile> _logger = Log.Instance.For<RegionFile>();
    private readonly int[] _chunkSaveTimes = new int[1024];
    private readonly FileStream _dataFile;
    private readonly int[] _offsets = new int[1024];
    private readonly List<bool> _sectorFree;
    private int _sizeDelta;

    public RegionFile(string input)
    {
        _sizeDelta = 0;

        try
        {
            _dataFile = new FileStream(input, FileMode.OpenOrCreate);

            int var2;
            if (_dataFile.Length < 4096L)
            {
                for (var2 = 0; var2 < 1024; ++var2)
                {
                    _dataFile.WriteInt(0);
                }

                for (var2 = 0; var2 < 1024; ++var2)
                {
                    _dataFile.WriteInt(0);
                }

                _sizeDelta += 8192;
            }

            if ((_dataFile.Length & 4095L) != 0L)
            {
                for (var2 = 0; var2 < (_dataFile.Length & 4095L); ++var2)
                {
                    _dataFile.WriteByte(0);
                }
            }

            var2 = (int)_dataFile.Length / 4096;
            _sectorFree = new List<bool>(var2);

            int var3;
            for (var3 = 0; var3 < var2; ++var3)
            {
                _sectorFree.Add(true);
            }

            _sectorFree[0] = false;
            _sectorFree[1] = false;
            _dataFile.Seek(0L, SeekOrigin.Begin);

            int var4;
            for (var3 = 0; var3 < 1024; ++var3)
            {
                var4 = _dataFile.ReadInt();
                _offsets[var3] = var4;
                if (var4 != 0 && (var4 >> 8) + (var4 & 255) <= _sectorFree.Count())
                {
                    for (int var5 = 0; var5 < (var4 & 255); ++var5)
                    {
                        _sectorFree[(var4 >> 8) + var5] = false;
                    }
                }
            }

            for (var3 = 0; var3 < 1024; ++var3)
            {
                var4 = _dataFile.ReadInt();
                _chunkSaveTimes[var3] = var4;
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine(ex);
        }
    }

    public int func_22209_a()
    {
        lock (this)
        {
            int var1 = _sizeDelta;
            _sizeDelta = 0;
            return var1;
        }
    }

    public ChunkDataStream GetChunkDataInputStream(int var1, int var2)
    {
        lock (this)
        {
            if (OutOfBounds(var1, var2))
            {
                return null;
            }

            try
            {
                int var3 = GetOffset(var1, var2);
                if (var3 == 0)
                {
                    return null;
                }

                int var4 = var3 >> 8;
                int var5 = var3 & 255;

                if (var4 + var5 > _sectorFree.Count())
                {
                    return null;
                }

                _dataFile.Seek(var4 * 4096, SeekOrigin.Begin);
                int var6 = _dataFile.ReadInt();
                if (var6 > 4096 * var5)
                {
                    return null;
                }

                CompressionType var7 = (CompressionType)_dataFile.ReadByte();
                byte[] var8;
                Stream var9;

                if (var7 == CompressionType.ZLibDeflate)
                {
                    var8 = new byte[var6 - 1];
                    _dataFile.ReadExactly(var8);
                    var9 = new ZLibStream(new MemoryStream(var8), CompressionMode.Decompress);
                    return new ChunkDataStream(var9, var7);
                }

                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    public Stream GetChunkDataOutputStream(int var1, int var2)
    {
        if (OutOfBounds(var1, var2))
        {
            return null;
        }

        RegionFileChunkBuffer buffer = new(this, var1, var2);
        return new ZLibStream(buffer, CompressionMode.Compress);
    }

    public void Write(int var1, int var2, byte[] var3, int var4)
    {
        lock (this)
        {
            try
            {
                int var5 = GetOffset(var1, var2);
                int var6 = var5 >> 8;
                int var7 = var5 & 255;
                int var8 = (var4 + 5) / 4096 + 1;
                if (var8 >= 256)
                {
                    return;
                }

                if (var6 != 0 && var7 == var8)
                {
                    Write(var6, var3, var4);
                }
                else
                {
                    int var9;
                    for (var9 = 0; var9 < var7; ++var9)
                    {
                        _sectorFree[var6 + var9] = true;
                    }

                    var9 = _sectorFree.IndexOf(true);
                    int var10 = 0;
                    int var11;
                    if (var9 != -1)
                    {
                        for (var11 = var9; var11 < _sectorFree.Count(); ++var11)
                        {
                            if (var10 != 0)
                            {
                                if (_sectorFree[var11])
                                {
                                    ++var10;
                                }
                                else
                                {
                                    var10 = 0;
                                }
                            }
                            else if (_sectorFree[var11])
                            {
                                var9 = var11;
                                var10 = 1;
                            }

                            if (var10 >= var8)
                            {
                                break;
                            }
                        }
                    }

                    if (var10 >= var8)
                    {
                        var6 = var9;
                        SetOffset(var1, var2, (var9 << 8) | var8);

                        for (var11 = 0; var11 < var8; ++var11)
                        {
                            _sectorFree[var6 + var11] = false;
                        }

                        Write(var6, var3, var4);
                    }
                    else
                    {
                        _dataFile.Seek(_dataFile.Length, SeekOrigin.Begin);
                        var6 = _sectorFree.Count();

                        for (var11 = 0; var11 < var8; ++var11)
                        {
                            _dataFile.Write(s_emptySector);
                            _sectorFree.Add(false);
                        }

                        _sizeDelta += 4096 * var8;
                        Write(var6, var3, var4);
                        SetOffset(var1, var2, (var6 << 8) | var8);
                    }
                }

                func_22208_b(var1, var2, (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000L));
            }
            catch (IOException var12)
            {
                _logger.LogError(var12, "Exception");
            }
        }
    }

    private void Write(int var1, byte[] var2, int var3)
    {
        _dataFile.Seek(var1 * 4096, SeekOrigin.Begin);
        _dataFile.WriteInt(var3 + 1);
        _dataFile.WriteByte((byte)CompressionType.ZLibDeflate);
        _dataFile.Write(var2, 0, var3);
    }

    private static bool OutOfBounds(int var1, int var2) => var1 < 0 || var1 >= 32 || var2 < 0 || var2 >= 32;

    private int GetOffset(int var1, int var2) => _offsets[var1 + var2 * 32];

    private void SetOffset(int var1, int var2, int var3)
    {
        _offsets[var1 + var2 * 32] = var3;
        _dataFile.Seek((var1 + var2 * 32) * 4, SeekOrigin.Begin);
        _dataFile.WriteInt(var3);
    }

    private void func_22208_b(int var1, int var2, int var3)
    {
        _chunkSaveTimes[var1 + var2 * 32] = var3;
        _dataFile.Seek(4096 + (var1 + var2 * 32) * 4, SeekOrigin.Begin);
        _dataFile.WriteInt(var3);
    }

    public void Flush()
    {
        _dataFile.Flush();
        _dataFile.Dispose();
    }

    internal enum CompressionType : byte
    {
        GZipUnused = 1,
        ZLibDeflate,
        OldRegionUnused
    }
}

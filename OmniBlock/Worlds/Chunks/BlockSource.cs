using OmniBlock.Blocks;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Worlds.Chunks;

internal class BlockSource
{
    public static void Fill(byte[] blocks, IBlockRuntimeView runtimeBlocks)
    {
        Span<byte> sanitizationTable = stackalloc byte[BlockRegistry.ProtocolIdCapacity];
        for (int i = 0; i < sanitizationTable.Length; i++)
            sanitizationTable[i] = i == 0 || runtimeBlocks.TryGetByProtocolId(i, out _) ? (byte)i : (byte)0;

        Span<byte> blocksSpan = blocks;

        for (int i = 0; i < blocksSpan.Length; i++)
        {
            blocksSpan[i] = sanitizationTable[blocksSpan[i]];
        }
    }
}

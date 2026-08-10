using System;

namespace OmniBlock.Blocks.Entities;

public class BlockEntityType(Func<BlockEntity> factory, string id)
{
    public string Id { get; } = id;
    public BlockEntity Create() => factory();
}

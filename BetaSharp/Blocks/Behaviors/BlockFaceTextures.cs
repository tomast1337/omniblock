namespace OmniBlock.Blocks.Behaviors;

/// <summary>The three textures a block with a distinct top, bottom, and sides draws with.</summary>
public readonly record struct BlockFaceTextures(int Top, int Side, int Bottom);

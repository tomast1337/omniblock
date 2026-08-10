namespace OmniBlock.Worlds.Core.Systems;

public interface ILightProvider
{
    float GetNaturalBrightness(int x, int y, int z, int minLight);
    float GetLuminance(int x, int y, int z);

    /// <summary>
    ///     The sky and block levels for a cell, with the block channel floored at
    ///     <paramref name="minBlockLight" /> for a block that emits.
    /// </summary>
    /// <remarks>
    ///     What <see cref="GetNaturalBrightness" /> returns, stopped short of the point where the two
    ///     channels would have been collapsed and ramped. Applying the floor to the block channel
    ///     alone gives what the collapsed form gave, since raising one side of a max and then taking
    ///     the max again lands in the same place either way round.
    /// </remarks>
    LightLevels GetLightLevels(int x, int y, int z, int minBlockLight);
}

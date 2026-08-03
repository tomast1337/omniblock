using BetaSharp.Client.Rendering.Core;
using Xunit;

namespace BetaSharp.Tests.Rendering;

public class ProgramSlotTests
{
    [Fact]
    public void EverySlotHasAFallbackChainEndingAtBasic()
    {
        // Also the only thing that runs ProgramSlots' own validation, which is what catches a new
        // enum member nobody gave a parent.
        foreach (ProgramSlot slot in Enum.GetValues<ProgramSlot>())
        {
            Assert.Equal(ProgramSlot.Basic, ProgramSlots.ResolutionChain(slot).Last());
        }
    }

    [Fact]
    public void BasicIsTheRoot()
    {
        Assert.False(ProgramSlots.TryGetParent(ProgramSlot.Basic, out _));
        Assert.Equal([ProgramSlot.Basic], ProgramSlots.ResolutionChain(ProgramSlot.Basic));
    }

    [Fact]
    public void AChainVisitsEachSlotOnceAndStartsWithTheSlotAsked()
    {
        foreach (ProgramSlot slot in Enum.GetValues<ProgramSlot>())
        {
            ProgramSlot[] chain = [.. ProgramSlots.ResolutionChain(slot)];

            Assert.Equal(slot, chain[0]);
            Assert.Equal(chain.Length, chain.Distinct().Count());
        }
    }

    [Fact]
    public void ASpecificSlotFallsBackThroughItsWholeAncestry()
    {
        Assert.Equal(
            [
                ProgramSlot.DamagedBlock,
                ProgramSlot.Terrain,
                ProgramSlot.TexturedLit,
                ProgramSlot.Textured,
                ProgramSlot.Basic,
            ],
            ProgramSlots.ResolutionChain(ProgramSlot.DamagedBlock));
    }

    [Fact]
    public void PackNamesRoundTrip()
    {
        foreach (ProgramSlot slot in Enum.GetValues<ProgramSlot>())
        {
            Assert.True(ProgramSlots.TryParsePackName(ProgramSlots.PackName(slot), out ProgramSlot parsed));
            Assert.Equal(slot, parsed);
        }
    }

    [Fact]
    public void PackNamesMatchTheIrisSpellingRatherThanTheEnumSpelling()
    {
        // The two that a mechanical transform of the enum member would get wrong, which is why the
        // names are data.
        Assert.Equal("gbuffers_damagedblock", ProgramSlots.PackName(ProgramSlot.DamagedBlock));
        Assert.Equal("gbuffers_block", ProgramSlots.PackName(ProgramSlot.BlockEntity));
    }

    [Fact]
    public void AnUnknownPackNameIsNotASlot()
    {
        Assert.False(ProgramSlots.TryParsePackName("gbuffers_beaconbeam", out _));
        Assert.False(ProgramSlots.TryParsePackName("composite", out _));
    }
}

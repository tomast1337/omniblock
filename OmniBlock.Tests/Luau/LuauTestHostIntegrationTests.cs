using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauTestHostIntegrationTests
{
    [SkippableFact]
    public void BootstrapExposesPassFailAndTestCapabilityOnlyWhenInstalled()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        var passes = 0;
        string? failure = null;
        var creative = 0;
        var flying = false;
        var teleport = (X: 0, Y: 0, Z: 0);
        var look = (Yaw: 0.0, Pitch: 0.0);
        var movement = (Forward: 0.0, Strafe: 0.0, Vertical: 0.0);
        var screenshots = 0;
        string? terrainDump = null;
        LuauTestHost.Pass = () => passes++;
        LuauTestHost.Fail = reason => failure = reason;
        LuauTestHost.Creative = () => creative++;
        LuauTestHost.SetFlying = value => flying = value;
        LuauTestHost.Teleport = (x, y, z) => teleport = (x, y, z);
        LuauTestHost.SetLook = (yaw, pitch) => look = (yaw, pitch);
        LuauTestHost.SetMovement = (forward, strafe, vertical) => movement = (forward, strafe, vertical);
        LuauTestHost.Screenshot = () => screenshots++;
        LuauTestHost.DumpTerrain = label => terrainDump = label;

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
            Assert.True(state.TryExecute("OMNI.test == nil", out var initiallyMissing), initiallyMissing);
            Assert.Equal("true", initiallyMissing);

            LuauTestHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauTestHost.Bootstrap, out var bootstrapError), bootstrapError);
            Assert.True(state.TryExecute(
                "OMNI.test.pass(); OMNI.test.fail('broken'); OMNI.test.creative(); " +
                "OMNI.test.setFlying(true); OMNI.test.teleport(160, 164, 0); " +
                "OMNI.test.setLook(45.5, 90); OMNI.test.setMovement(1, -0.5, 0.25); " +
                "OMNI.test.screenshot(); OMNI.test.dumpTerrain('airborne'); " +
                "return OMNI.has('test')",
                out var hasTest), hasTest);

            Assert.Equal(1, passes);
            Assert.Equal("broken", failure);
            Assert.Equal(1, creative);
            Assert.True(flying);
            Assert.Equal((160, 164, 0), teleport);
            Assert.Equal((45.5, 90.0), look);
            Assert.Equal((1.0, -0.5, 0.25), movement);
            Assert.Equal(1, screenshots);
            Assert.Equal("airborne", terrainDump);
            Assert.Equal("true", hasTest);
        }
        finally
        {
            LuauTestHost.Pass = null;
            LuauTestHost.Fail = null;
            LuauTestHost.Creative = null;
            LuauTestHost.SetFlying = null;
            LuauTestHost.Teleport = null;
            LuauTestHost.SetLook = null;
            LuauTestHost.SetMovement = null;
            LuauTestHost.Screenshot = null;
            LuauTestHost.DumpTerrain = null;
        }
    }
}

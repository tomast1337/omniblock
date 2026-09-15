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
        (string Id, int Count)? summoned = null;
        var brokenBlock = (X: 0, Y: 0, Z: 0);
        (string Id, int X, int Y, int Z)? setBlock = null;
        var flying = false;
        var teleport = (X: 0, Y: 0, Z: 0);
        var look = (Yaw: 0.0, Pitch: 0.0);
        var movement = (Forward: 0.0, Strafe: 0.0, Vertical: 0.0);
        var path = (AX: 0.0, AY: 0.0, AZ: 0.0, BX: 0.0, BY: 0.0, BZ: 0.0, Seconds: 0.0);
        var screenshots = 0;
        string? terrainDump = null;
        string? profilerDump = null;
        (string Profile, int Radius)? automaticGeneration = null;
        LuauTestHost.Pass = () => passes++;
        LuauTestHost.Fail = reason => failure = reason;
        LuauTestHost.Creative = () => creative++;
        LuauTestHost.Summon = (id, count) =>
        {
            summoned = (id, count);
            return true;
        };
        LuauTestHost.CountEntities = (id, min, max) =>
            id == "omniblock:cow" && min == 32 && max == 128 ? 3 : 0;
        LuauTestHost.BreakBlock = (x, y, z) =>
        {
            brokenBlock = (x, y, z);
            return true;
        };
        LuauTestHost.SetBlock = (id, x, y, z) => setBlock = (id, x, y, z);
        LuauTestHost.IsMeshCurrent = (x, y, z) => (x, y, z) == (4, 61, 7);
        LuauTestHost.MeshDeadlineMissCount = (x, y, z) => x + y + z;
        LuauTestHost.SetFlying = value => flying = value;
        LuauTestHost.Teleport = (x, y, z) => teleport = (x, y, z);
        LuauTestHost.SetLook = (yaw, pitch) => look = (yaw, pitch);
        LuauTestHost.SetMovement = (forward, strafe, vertical) => movement = (forward, strafe, vertical);
        LuauTestHost.FlyPath = (ax, ay, az, bx, by, bz, seconds) =>
            path = (ax, ay, az, bx, by, bz, seconds);
        LuauTestHost.Screenshot = () => screenshots++;
        LuauTestHost.DumpTerrain = label => terrainDump = label;
        LuauTestHost.DumpProfiler = label => profilerDump = label;
        LuauTestHost.WorldGenerationAuto = (profile, radius) =>
        {
            automaticGeneration = (profile, radius);
            return true;
        };
        LuauTestHost.WorldGenerationMetric = metric => metric == "saved" ? 17 : 0;
        LuauTestHost.EntityBaseline = (scene, count, distance) => scene == "sheep" && count == 16 && distance == 32;
        LuauTestHost.EntityBaselineState = state => state == "walk-2";
        LuauTestHost.BeginEntitySample = () => true;
        LuauTestHost.EndEntitySample = label => label == "reference";
        var cleared = false;
        LuauTestHost.ClearEntityBaseline = () => cleared = true;
        LuauTestHost.EntityImpostors = (enabled, forced) => enabled && forced;
        LuauTestHost.ImpostorCache = action => action == "reload";

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
            Assert.True(state.TryExecute("OMNI.test == nil", out var initiallyMissing), initiallyMissing);
            Assert.Equal("true", initiallyMissing);

            LuauTestHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauTestHost.Bootstrap, out var bootstrapError), bootstrapError);
            Assert.True(state.TryExecute(
                "OMNI.test.pass(); OMNI.test.fail('broken'); OMNI.test.creative(); " +
                "assert(OMNI.test.summon('omniblock:cow', 2)); " +
                "assert(OMNI.test.countEntities('omniblock:cow', 32, 128) == 3); " +
                "assert(OMNI.test.breakBlock(4, 61, 7)); " +
                "OMNI.test.setBlock('omniblock:flowing_water', 15, 70, 0); " +
                "assert(OMNI.test.isMeshCurrent(4, 61, 7)); " +
                "assert(OMNI.test.meshDeadlineMissCount(4, 61, 7) == 72); " +
                "OMNI.test.setFlying(true); OMNI.test.teleport(160, 164, 0); " +
                "OMNI.test.setLook(45.5, 90); OMNI.test.setMovement(1, -0.5, 0.25); " +
                "OMNI.test.flyPath(160, 256, 0, 160, 256, 160, 20); " +
                "OMNI.test.screenshot(); OMNI.test.dumpTerrain('airborne'); OMNI.test.dumpProfiler('steady'); " +
                "assert(OMNI.test.worldGenerationAuto('prepare', 24)); " +
                "assert(OMNI.test.worldGenerationMetric('saved') == 17); " +
                "assert(OMNI.test.entityBaseline('sheep', 16, 32)); " +
                "assert(not OMNI.test.entityBaseline('bad', 16, 32)); " +
                "assert(OMNI.test.entityBaselineState('walk-2')); assert(not OMNI.test.entityBaselineState('bad')); " +
                "assert(OMNI.test.beginEntitySample()); assert(OMNI.test.endEntitySample('reference')); " +
                "OMNI.test.clearEntityBaseline(); " +
                "assert(OMNI.test.entityImpostors(true, true)); assert(not OMNI.test.entityImpostors(false)); " +
                "assert(OMNI.test.impostorCache('reload')); assert(not OMNI.test.impostorCache('unknown')); " +
                "return OMNI.has('test')",
                out var hasTest), hasTest);

            Assert.Equal(1, passes);
            Assert.Equal("broken", failure);
            Assert.Equal(1, creative);
            Assert.Equal(("omniblock:cow", 2), summoned);
            Assert.Equal((4, 61, 7), brokenBlock);
            Assert.Equal(("omniblock:flowing_water", 15, 70, 0), setBlock);
            Assert.True(flying);
            Assert.Equal((160, 164, 0), teleport);
            Assert.Equal((45.5, 90.0), look);
            Assert.Equal((1.0, -0.5, 0.25), movement);
            Assert.Equal((160.0, 256.0, 0.0, 160.0, 256.0, 160.0, 20.0), path);
            Assert.Equal(1, screenshots);
            Assert.Equal("airborne", terrainDump);
            Assert.Equal("steady", profilerDump);
            Assert.Equal(("prepare", 24), automaticGeneration);
            Assert.Equal("true", hasTest);
            Assert.True(cleared);
        }
        finally
        {
            LuauTestHost.Pass = null;
            LuauTestHost.Fail = null;
            LuauTestHost.Creative = null;
            LuauTestHost.Summon = null;
            LuauTestHost.CountEntities = null;
            LuauTestHost.BreakBlock = null;
            LuauTestHost.SetBlock = null;
            LuauTestHost.IsMeshCurrent = null;
            LuauTestHost.MeshDeadlineMissCount = null;
            LuauTestHost.SetFlying = null;
            LuauTestHost.Teleport = null;
            LuauTestHost.SetLook = null;
            LuauTestHost.SetMovement = null;
            LuauTestHost.FlyPath = null;
            LuauTestHost.Screenshot = null;
            LuauTestHost.DumpTerrain = null;
            LuauTestHost.DumpProfiler = null;
            LuauTestHost.WorldGenerationAuto = null;
            LuauTestHost.WorldGenerationMetric = null;
            LuauTestHost.EntityBaseline = null;
            LuauTestHost.EntityBaselineState = null;
            LuauTestHost.BeginEntitySample = null;
            LuauTestHost.EndEntitySample = null;
            LuauTestHost.ClearEntityBaseline = null;
            LuauTestHost.EntityImpostors = null;
            LuauTestHost.ImpostorCache = null;
        }
    }
}

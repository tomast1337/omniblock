using OmniBlock.Luau;
using OmniBlock.Luau.Host;

namespace OmniBlock.Tests.Luau;

[Collection(LuauHostCollection.Name)]
public sealed class LuauWorldGenerationHostIntegrationTests
{
    [SkippableFact]
    public void BootstrapExposesReadOnlySnapshotsAndQueuesLifecycleOperations()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        var info = Info("running", nextTarget: 7);
        (string Id, int Dimension, int X, int Z, int Radius)? start = null;
        (string Action, string Id)? change = null;
        LuauWorldGenerationHost.Available = () => true;
        LuauWorldGenerationHost.List = () => [info];
        LuauWorldGenerationHost.Inspect = id => id == info.Id ? info : null;
        LuauWorldGenerationHost.Start = (id, dimension, x, z, radius) =>
        {
            start = (id, dimension, x, z, radius);
            return new(true);
        };
        LuauWorldGenerationHost.Change = (action, id) =>
        {
            change = (action, id);
            return new(true);
        };

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
            LuauWorldGenerationHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauWorldGenerationHost.Bootstrap, out var bootstrapError), bootstrapError);

            Assert.True(state.TryExecute("OMNI.worldgen.available", out var available), available);
            Assert.True(state.TryExecute("#OMNI.worldgen.list()", out var count), count);
            Assert.True(state.TryExecute("OMNI.worldgen.list()[1].status", out var status), status);
            Assert.True(state.TryExecute("OMNI.worldgen.get('job').progress.nextTarget", out var next), next);
            Assert.True(state.TryExecute("OMNI.has('worldgen')", out var capability), capability);
            Assert.True(state.TryExecute(
                "local j=OMNI.worldgen.start({id='new',dimension='omniblock:nether',center={x=-17,z=33},radiusChunks=12}); j:pause(); return j.id",
                out var id), id);
            Assert.True(state.TryExecute(
                "local j=OMNI.worldgen.get('job'); return pcall(function() j.id='changed' end)",
                out var mutableJob), mutableJob);
            Assert.True(state.TryExecute(
                "local p=OMNI.worldgen.get('job').progress; return pcall(function() p.status='failed' end)",
                out var mutableProgress), mutableProgress);

            Assert.Equal("true", available);
            Assert.Equal("1", count);
            Assert.Equal("running", status);
            Assert.Equal("7", next);
            Assert.Equal("true", capability);
            Assert.Equal("new", id);
            Assert.Equal(("new", -1, -2, 2, 12), start);
            Assert.Equal(("pause", "new"), change);
            Assert.StartsWith("false, ", mutableJob, StringComparison.Ordinal);
            Assert.StartsWith("false, ", mutableProgress, StringComparison.Ordinal);
        }
        finally
        {
            LuauWorldGenerationHost.Available = null;
            LuauWorldGenerationHost.List = null;
            LuauWorldGenerationHost.Inspect = null;
            LuauWorldGenerationHost.Start = null;
            LuauWorldGenerationHost.Change = null;
        }
    }

    [SkippableFact]
    public void RejectedControlRequestRaisesALuauError()
    {
        Skip.IfNot(LuauQuickRun.IsAvailable(), "native library not resolvable for this checkout.");
        using LuauState state = new();
        state.ResetInstructionBudget(100_000);
        LuauWorldGenerationHost.Available = () => false;
        LuauWorldGenerationHost.List = () => [];
        LuauWorldGenerationHost.Inspect = _ => null;
        LuauWorldGenerationHost.Start = (_, _, _, _, _) => new(false, "not authorized");
        LuauWorldGenerationHost.Change = (_, _) => new(false, "not authorized");

        try
        {
            Assert.True(state.TryExecute(LuauDomHost.Bootstrap, out var omniError), omniError);
            LuauWorldGenerationHost.Install(state.Handle);
            Assert.True(state.TryExecute(LuauWorldGenerationHost.Bootstrap, out var bootstrapError), bootstrapError);
            Assert.True(state.TryExecute(
                "return pcall(function() OMNI.worldgen.start({id='nope',center={x=0,z=0},radiusChunks=1}) end)",
                out var accepted), accepted);
            Assert.StartsWith("false, ", accepted, StringComparison.Ordinal);
        }
        finally
        {
            LuauWorldGenerationHost.Available = null;
            LuauWorldGenerationHost.List = null;
            LuauWorldGenerationHost.Inspect = null;
            LuauWorldGenerationHost.Start = null;
            LuauWorldGenerationHost.Change = null;
        }
    }

    private static LuauWorldGenerationInfo Info(string status, double nextTarget) => new(
        "job", "World", 0, "123", "default", "options", "content", 1, -2, 8, 197,
        status, nextTarget, 190, 7, 6, 5, 1, 10, 1024, 2048, 4096, 2.5,
        "within budget", null, "2026-09-19T00:00:00.0000000+00:00",
        "2026-09-19T00:00:01.0000000+00:00");
}

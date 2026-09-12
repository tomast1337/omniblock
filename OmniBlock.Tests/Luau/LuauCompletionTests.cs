using OmniBlock.Client.Diagnostics;

namespace OmniBlock.Tests.Luau;

public sealed class LuauCompletionTests
{
    [Theory]
    [InlineData("OM", "OMNI")]
    [InlineData("OMNI.u", "ui")]
    [InlineData("OMNI.ui.que", "querySelector")]
    [InlineData("OMNI.ui.scr", "screen")]
    [InlineData("OMNI.ui.root.chi", "child")]
    [InlineData("OMNI.ui.root.cli", "click")]
    [InlineData("OMNI.ui.root.i", "id")]
    [InlineData("OMNI.config.mu", "music")]
    [InlineData("OMNI.config.pause", "pauseOnFocusLoss")]
    [InlineData("OMNI.config.capture", "captureMouse")]
    [InlineData("OMNI.config.sim", "simulationDistance")]
    [InlineData("OMNI.config.opt", "options")]
    [InlineData("OMNI.cli", "client")]
    [InlineData("OMNI.client.wor", "worlds")]
    [InlineData("OMNI.client.sta", "state")]
    [InlineData("OMNI.client.state.worldL", "worldLoaded")]
    [InlineData("OMNI.client.state.playerR", "playerReady")]
    [InlineData("OMNI.client.state.residentM", "residentMeshCount")]
    [InlineData("OMNI.client.state.presentedM", "presentedMeshCount")]
    [InlineData("OMNI.client.state.foreground", "foregroundPending")]
    [InlineData("OMNI.client.state.background", "backgroundPending")]
    [InlineData("OMNI.client.state.oldest", "oldestForegroundAge")]
    [InlineData("OMNI.client.state.presentation", "presentationRegressionCount")]
    [InlineData("OMNI.client.state.renderD", "renderDistance")]
    [InlineData("OMNI.client.state.simulationD", "simulationDistance")]
    [InlineData("OMNI.client.state.visibilityC", "visibilityCandidates")]
    [InlineData("OMNI.client.state.frustumT", "frustumTests")]
    [InlineData("OMNI.client.state.terrainSubmit", "terrainSubmitCpuMs")]
    [InlineData("OMNI.client.state.debug", "debugOpen")]
    [InlineData("OMNI.client.state.meshCoop", "meshCooperativeCancellationCount")]
    [InlineData("OMNI.client.state.meshCriticalDeadline", "meshCriticalDeadlineMissCount")]
    [InlineData("OMNI.client.worlds.lo", "load")]
    [InlineData("OMNI.ru", "run")]
    [InlineData("OMNI.wa", "wait")]
    [InlineData("OMNI.waitU", "waitUntil")]
    [InlineData("OMNI.test.pa", "pass")]
    [InlineData("OMNI.test.fly", "flyPath")]
    [InlineData("OMNI.test.break", "breakBlock")]
    [InlineData("OMNI.test.setB", "setBlock")]
    [InlineData("OMNI.test.isMesh", "isMeshCurrent")]
    [InlineData("OMNI.test.meshDeadline", "meshDeadlineMissCount")]
    [InlineData("OMNI.test.setL", "setLook")]
    [InlineData("OMNI.test.setM", "setMovement")]
    public void CompletesGlobalsAndDomMembers(string source, string expected)
    {
        LuauCompletion completion = new();

        var edit = completion.Complete(source, source.Length);

        Assert.Contains(expected, edit.Matches);
        Assert.Equal(expected, edit.Replacement);
    }

    [Fact]
    public void CompletesToSharedPrefixWhenSeveralNamesMatch()
    {
        var edit = new LuauCompletion().Complete("OMNI.ui.root.hit", 16);

        Assert.Equal("hitTestVisible", edit.Replacement);
    }

    [Fact]
    public void LearnsSuccessfulGlobalAssignmentsAndFunctions()
    {
        LuauCompletion completion = new();
        completion.ObserveSuccessfulSubmission("player = OMNI.ui.root\nfunction teleport() end\nlocal hidden = 1");

        Assert.Contains("player", completion.Complete("pla", 3).Matches);
        Assert.Contains("teleport", completion.Complete("tele", 4).Matches);
        Assert.DoesNotContain("hidden", completion.Complete("hid", 3).Matches);
    }
}

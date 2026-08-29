using OmniBlock.Client.Diagnostics;

namespace OmniBlock.Tests.Luau;

public sealed class LuauCompletionTests
{
    [Theory]
    [InlineData("OM", "OMNI")]
    [InlineData("OMNI.que", "querySelector")]
    [InlineData("OMNI.root.chi", "child")]
    public void CompletesGlobalsAndDomMembers(string source, string expected)
    {
        LuauCompletion completion = new();

        CompletionEdit edit = completion.Complete(source, source.Length);

        Assert.Contains(expected, edit.Matches);
        Assert.Equal(expected, edit.Replacement);
    }

    [Fact]
    public void CompletesToSharedPrefixWhenSeveralNamesMatch()
    {
        CompletionEdit edit = new LuauCompletion().Complete("OMNI.root.hit", 13);

        Assert.Equal("hitTestVisible", edit.Replacement);
    }

    [Fact]
    public void LearnsSuccessfulGlobalAssignmentsAndFunctions()
    {
        LuauCompletion completion = new();
        completion.ObserveSuccessfulSubmission("player = OMNI.root\nfunction teleport() end\nlocal hidden = 1");

        Assert.Contains("player", completion.Complete("pla", 3).Matches);
        Assert.Contains("teleport", completion.Complete("tele", 4).Matches);
        Assert.DoesNotContain("hidden", completion.Complete("hid", 3).Matches);
    }
}

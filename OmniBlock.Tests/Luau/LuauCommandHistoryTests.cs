using OmniBlock.Client.Diagnostics;

namespace OmniBlock.Tests.Luau;

public sealed class LuauCommandHistoryTests
{
    [Fact]
    public void Navigation_walksCommandsAndRestoresDraft()
    {
        LuauCommandHistory history = new();
        history.Add("first()\nsecond()");
        history.Add("third()");

        Assert.Equal("third()", history.Navigate("unfinished", older: true));
        Assert.Equal("first()\nsecond()", history.Navigate("third()", older: true));
        Assert.Equal("first()\nsecond()", history.Navigate("first()\nsecond()", older: true));
        Assert.Equal("third()", history.Navigate("first()\nsecond()", older: false));
        Assert.Equal("unfinished", history.Navigate("third()", older: false));
    }

    [Fact]
    public void History_isBoundedAndSkipsConsecutiveDuplicates()
    {
        LuauCommandHistory history = new(capacity: 2);
        history.Add("one");
        history.Add("one");
        history.Add("two");
        history.Add("three");

        Assert.Equal("three", history.Navigate("", older: true));
        Assert.Equal("two", history.Navigate("three", older: true));
        Assert.Equal("two", history.Navigate("two", older: true));
    }

    [Fact]
    public void History_persistsAsEscapedPlainTextAndReloadsWholeCommands()
    {
        string directory = Path.Combine(Path.GetTempPath(), "omniblock-history-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, ".luau_history");
        try
        {
            LuauCommandHistory writer = new(path);
            writer.Add("print(\\\"literal \\\\n\\\")\nprint(2)");
            writer.Add("return 3");

            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\\n", lines[0]);

            LuauCommandHistory reader = new(path);
            Assert.Equal("return 3", reader.Navigate("", older: true));
            Assert.Equal("print(\\\"literal \\\\n\\\")\nprint(2)", reader.Navigate("return 3", older: true));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}

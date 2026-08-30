using OmniBlock.Client;

namespace OmniBlock.Tests;

public sealed class ClientLaunchOptionsTests
{
    [Fact]
    public void Parse_preservesExistingClientArguments()
    {
        ClientLaunchOptions options = ClientLaunchOptions.Parse(
            ["--username", "TestPlayer", "--token", "session-token", "--debug"]);

        Assert.Equal("TestPlayer", options.Username);
        Assert.Equal("session-token", options.SessionToken);
        Assert.True(options.Debug);
        Assert.Null(options.StartupScript);
    }

    [Fact]
    public void Parse_loadsStartupScriptFromAnAbsolutePath()
    {
        string directory = Directory.CreateTempSubdirectory("omniblock-startup-script-").FullName;
        string path = Path.Combine(directory, "smoke.luau");

        try
        {
            File.WriteAllText(path, "print('ready')");

            ClientLaunchOptions options = ClientLaunchOptions.Parse(
                ["--username", "TestPlayer", "--startup-script", path]);

            Assert.NotNull(options.StartupScript);
            Assert.Equal(Path.GetFullPath(path), options.StartupScript.Path);
            Assert.Equal("print('ready')", options.StartupScript.Source);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("--username")]
    [InlineData("--token")]
    [InlineData("--startup-script")]
    public void Parse_rejectsAnOptionWithoutAValue(string option)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => ClientLaunchOptions.Parse([option]));

        Assert.Contains("requires a value", error.Message);
    }

    [Fact]
    public void Parse_reportsAMissingStartupScript()
    {
        string path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.luau");

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            ClientLaunchOptions.Parse(["--username", "TestPlayer", "--startup-script", path]));

        Assert.Contains("Could not load startup script", error.Message);
        Assert.Contains(Path.GetFullPath(path), error.Message);
    }
}

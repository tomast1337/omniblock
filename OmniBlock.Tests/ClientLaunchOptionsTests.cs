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
        Assert.Null(options.E2ETest);
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
    [InlineData("--e2e-script")]
    [InlineData("--e2e-timeout")]
    [InlineData("--e2e-artifacts")]
    public void Parse_rejectsAnOptionWithoutAValue(string option)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => ClientLaunchOptions.Parse([option]));

        Assert.Contains("requires a value", error.Message);
    }

    [Fact]
    public void Parse_loadsE2EScriptTimeoutAndArtifacts()
    {
        string directory = Directory.CreateTempSubdirectory("omniblock-e2e-options-").FullName;
        string script = Path.Combine(directory, "smoke.luau");
        string artifacts = Path.Combine(directory, "artifacts");

        try
        {
            File.WriteAllText(script, "OMNI.test.pass()");

            ClientLaunchOptions options = ClientLaunchOptions.Parse(
                ["--username", "TestPlayer", "--e2e-script", script, "--e2e-timeout", "12.5", "--e2e-artifacts", artifacts]);

            Assert.NotNull(options.E2ETest);
            Assert.Equal("OMNI.test.pass()", options.E2ETest.Script.Source);
            Assert.Equal(TimeSpan.FromSeconds(12.5), options.E2ETest.Timeout);
            Assert.Equal(Path.GetFullPath(artifacts), options.E2ETest.ArtifactsPath);
            Assert.Null(options.StartupScript);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Parse_rejectsCombiningStartupAndE2EScripts()
    {
        string path = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentException>(() => ClientLaunchOptions.Parse(
                ["--username", "TestPlayer", "--startup-script", path, "--e2e-script", path]));
        }
        finally
        {
            File.Delete(path);
        }
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

using System.Text.Json;
using OmniBlock.Client;

namespace OmniBlock.Tests;

public sealed class E2ETestControllerTests
{
    [Fact]
    public void Pass_writesArtifactsAndKeepsTheFirstResult()
    {
        string directory = Directory.CreateTempSubdirectory("omniblock-e2e-result-").FullName;
        int shutdownRequests = 0;
        E2ETestLaunchOptions options = new(
            new StartupScript("smoke.luau", "OMNI.test.pass()"),
            TimeSpan.FromSeconds(10),
            directory);

        try
        {
            using E2ETestController controller = new(options, () => shutdownRequests++);

            controller.Pass();
            controller.Fail("too late");

            Assert.Equal(0, controller.ExitCode);
            Assert.Equal(1, shutdownRequests);
            Assert.True(File.Exists(Path.Combine(directory, "client.log")));
            using JsonDocument result = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "result.json")));
            Assert.Equal("passed", result.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, result.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Watchdog_timesOutAndRequestsShutdown()
    {
        string directory = Directory.CreateTempSubdirectory("omniblock-e2e-timeout-").FullName;
        using ManualResetEventSlim shutdown = new();
        E2ETestLaunchOptions options = new(
            new StartupScript("hung.luau", "OMNI.wait(100)"),
            TimeSpan.FromMilliseconds(50),
            directory);

        try
        {
            using E2ETestController controller = new(options, shutdown.Set);

            Assert.True(shutdown.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(E2ETestController.TimedOutExitCode, controller.ExitCode);
            using JsonDocument result = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "result.json")));
            Assert.Equal("timeout", result.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

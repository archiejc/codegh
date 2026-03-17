using FluentAssertions;
using LiveCanvas.SmokeHarness;

namespace LiveCanvas.E2E.Tests;

public class SmokeHarnessRunnerTests
{
    [Fact]
    public async Task smoke_harness_can_validate_mock_bridge_and_mcp_stdio_end_to_end()
    {
        var repoRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "livecanvas-smoke-tests",
            Guid.NewGuid().ToString("N"));

        var result = await new SmokeHarnessRunner().RunAsync(new SmokeHarnessOptions(
            AgentHostProjectPath: Path.Combine(repoRoot, "src", "LiveCanvas.AgentHost", "LiveCanvas.AgentHost.csproj"),
            OutputDirectory: outputDirectory,
            RunBridgeDirectCheck: true,
            RunMcpCheck: true));

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Errors));
        result.CompletedChecks.Should().Contain(["bridge-jsonrpc", "mcp-stdio"]);
        File.Exists(Path.Combine(outputDirectory, "preview.png")).Should().BeTrue();
        File.Exists(Path.Combine(outputDirectory, "smoke.gh")).Should().BeTrue();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LiveCanvas.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the LiveCanvas repository root.");
    }
}

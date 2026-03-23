using System.Text.Json;
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
        result.CompletedChecks.Should().Contain(["bridge-jsonrpc-mock", "mcp-stdio-mock"]);
        File.Exists(Path.Combine(outputDirectory, "preview.png")).Should().BeTrue();
        File.Exists(Path.Combine(outputDirectory, "smoke.gh")).Should().BeTrue();
        File.Exists(Path.Combine(outputDirectory, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputDirectory, "transcript.json")).Should().BeTrue();

        var manifest = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(outputDirectory, "manifest.json")));
        manifest.RootElement.GetProperty("mode").GetString().Should().Be("mock");
        manifest.RootElement.GetProperty("scenario").GetString().Should().Be("Smoke");
        manifest.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        manifest.RootElement.GetProperty("completedChecks").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(["bridge-jsonrpc-mock", "mcp-stdio-mock"]);

        var transcript = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(outputDirectory, "transcript.json")));
        transcript.RootElement.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task absolute_towers_scenario_can_run_against_mock_bridge_and_mcp_stdio()
    {
        var repoRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "livecanvas-absolute-towers-tests",
            Guid.NewGuid().ToString("N"));

        var result = await new SmokeHarnessRunner().RunAsync(new SmokeHarnessOptions(
            AgentHostProjectPath: Path.Combine(repoRoot, "src", "LiveCanvas.AgentHost", "LiveCanvas.AgentHost.csproj"),
            Scenario: SmokeHarnessScenario.AbsoluteTowers,
            OutputDirectory: outputDirectory,
            RunBridgeDirectCheck: true,
            RunMcpCheck: true));

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Errors));
        result.CompletedChecks.Should().Contain(["bridge-jsonrpc-mock", "mcp-stdio-mock"]);
        File.Exists(Path.Combine(outputDirectory, "preview.png")).Should().BeTrue();
        File.Exists(Path.Combine(outputDirectory, "absolute-towers.gh")).Should().BeTrue();

        var manifest = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(outputDirectory, "manifest.json")));
        manifest.RootElement.GetProperty("scenario").GetString().Should().Be("AbsoluteTowers");
        manifest.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task copilot_absolute_towers_scenario_can_run_against_mock_provider_bridge_and_mcp_stdio()
    {
        var repoRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "livecanvas-copilot-absolute-towers-tests",
            Guid.NewGuid().ToString("N"));

        var result = await new SmokeHarnessRunner().RunAsync(new SmokeHarnessOptions(
            AgentHostProjectPath: Path.Combine(repoRoot, "src", "LiveCanvas.AgentHost", "LiveCanvas.AgentHost.csproj"),
            Scenario: SmokeHarnessScenario.CopilotAbsoluteTowers,
            OutputDirectory: outputDirectory,
            RunBridgeDirectCheck: true,
            RunMcpCheck: true));

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Errors));
        result.CompletedChecks.Should().Contain(["bridge-jsonrpc-mock", "mcp-stdio-mock"]);
        File.Exists(Path.Combine(outputDirectory, "preview.png")).Should().BeTrue();
        File.Exists(Path.Combine(outputDirectory, "document.gh")).Should().BeTrue();

        var manifest = await JsonDocument.ParseAsync(File.OpenRead(Path.Combine(outputDirectory, "manifest.json")));
        manifest.RootElement.GetProperty("scenario").GetString().Should().Be("CopilotAbsoluteTowers");
        manifest.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task live_smoke_can_run_when_explicitly_enabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LIVECANVAS_RUN_LIVE_SMOKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var repoRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "livecanvas-live-smoke-tests",
            Guid.NewGuid().ToString("N"));

        var result = await new SmokeHarnessRunner().RunAsync(new SmokeHarnessOptions(
            AgentHostProjectPath: Path.Combine(repoRoot, "src", "LiveCanvas.AgentHost", "LiveCanvas.AgentHost.csproj"),
            Mode: SmokeHarnessMode.Live,
            OutputDirectory: outputDirectory,
            RunBridgeDirectCheck: true,
            RunMcpCheck: true));

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Errors));
        result.CompletedChecks.Should().Contain(["bridge-jsonrpc-live", "mcp-stdio-live"]);
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

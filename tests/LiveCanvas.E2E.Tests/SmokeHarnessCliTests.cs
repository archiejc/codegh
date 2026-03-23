using FluentAssertions;
using LiveCanvas.SmokeHarness;

namespace LiveCanvas.E2E.Tests;

public class SmokeHarnessCliTests
{
    [Fact]
    public void parse_defaults_to_mock_mode()
    {
        var options = SmokeHarnessCli.Parse([]);

        options.Mode.Should().Be(SmokeHarnessMode.Mock);
        options.RunBridgeDirectCheck.Should().BeTrue();
        options.RunMcpCheck.Should().BeTrue();
        options.Configuration.Should().Be("Debug");
        options.LivePreflightTimeoutSeconds.Should().Be(10);
        options.Scenario.Should().Be(SmokeHarnessScenario.Smoke);
    }

    [Fact]
    public void parse_supports_live_mode_scenarios_and_optional_flags()
    {
        var options = SmokeHarnessCli.Parse(
            [
                "--mode", "live",
                "--scenario", "absolute-towers",
                "--bridge-uri", "ws://127.0.0.1:17881/livecanvas/v0",
                "--live-preflight-timeout-seconds", "15",
                "--skip-build-agent-host",
                "--configuration", "Release"
            ]);

        options.Mode.Should().Be(SmokeHarnessMode.Live);
        options.Scenario.Should().Be(SmokeHarnessScenario.AbsoluteTowers);
        options.BridgeUri.Should().Be("ws://127.0.0.1:17881/livecanvas/v0");
        options.LivePreflightTimeoutSeconds.Should().Be(15);
        options.SkipBuildAgentHost.Should().BeTrue();
        options.Configuration.Should().Be("Release");
    }

    [Fact]
    public void parse_supports_copilot_absolute_towers_scenario()
    {
        var options = SmokeHarnessCli.Parse(["--scenario", "copilot-absolute-towers"]);

        options.Scenario.Should().Be(SmokeHarnessScenario.CopilotAbsoluteTowers);
    }

    [Fact]
    public void parse_rejects_invalid_flag_combinations()
    {
        var act = () => SmokeHarnessCli.Parse(["--bridge-only", "--mcp-only"]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be combined*");
    }

    [Fact]
    public void parse_rejects_invalid_flag_combinations_in_reverse_order()
    {
        var act = () => SmokeHarnessCli.Parse(["--mcp-only", "--bridge-only"]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be combined*");
    }

    [Fact]
    public void parse_rejects_mock_bridge_uri()
    {
        var act = () => SmokeHarnessCli.Parse(["--bridge-uri", "ws://127.0.0.1:17881/livecanvas/v0"]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*only valid in --mode live*");
    }
}

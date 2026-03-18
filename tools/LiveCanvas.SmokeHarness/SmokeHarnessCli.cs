namespace LiveCanvas.SmokeHarness;

internal static class SmokeHarnessCli
{
    public static SmokeHarnessOptions Parse(string[] args)
    {
        var repoRoot = FindRepositoryRoot();
        var agentHostProjectPath = Path.Combine(repoRoot, "src", "LiveCanvas.AgentHost", "LiveCanvas.AgentHost.csproj");
        string? agentHostDllPath = null;
        string? outputDirectory = null;
        var runBridgeDirectCheck = true;
        var runMcpCheck = true;
        var configuration = "Debug";
        var mode = SmokeHarnessMode.Mock;
        var scenario = SmokeHarnessScenario.Smoke;
        string? bridgeUri = null;
        var livePreflightTimeoutSeconds = 10;
        var skipBuildAgentHost = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode":
                    mode = ParseMode(RequireValue(args, ref i, "--mode"));
                    break;
                case "--agent-host-project":
                    agentHostProjectPath = RequireValue(args, ref i, "--agent-host-project");
                    break;
                case "--scenario":
                    scenario = ParseScenario(RequireValue(args, ref i, "--scenario"));
                    break;
                case "--agent-host-dll":
                    agentHostDllPath = RequireValue(args, ref i, "--agent-host-dll");
                    break;
                case "--skip-build-agent-host":
                    skipBuildAgentHost = true;
                    break;
                case "--output-dir":
                    outputDirectory = RequireValue(args, ref i, "--output-dir");
                    break;
                case "--configuration":
                    configuration = ParseConfiguration(RequireValue(args, ref i, "--configuration"));
                    break;
                case "--bridge-only":
                    if (!runBridgeDirectCheck || !runMcpCheck)
                    {
                        throw new ArgumentException("--bridge-only cannot be combined with --mcp-only.");
                    }

                    runBridgeDirectCheck = true;
                    runMcpCheck = false;
                    break;
                case "--mcp-only":
                    if (!runBridgeDirectCheck || !runMcpCheck)
                    {
                        throw new ArgumentException("--mcp-only cannot be combined with --bridge-only.");
                    }

                    runBridgeDirectCheck = false;
                    runMcpCheck = true;
                    break;
                case "--bridge-uri":
                    bridgeUri = RequireValue(args, ref i, "--bridge-uri");
                    break;
                case "--live-preflight-timeout-seconds":
                    livePreflightTimeoutSeconds = ParsePositiveInt(
                        RequireValue(args, ref i, "--live-preflight-timeout-seconds"),
                        "--live-preflight-timeout-seconds");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (mode == SmokeHarnessMode.Mock && bridgeUri is not null)
        {
            throw new ArgumentException("--bridge-uri is only valid in --mode live.");
        }

        if (mode == SmokeHarnessMode.Mock && livePreflightTimeoutSeconds != 10)
        {
            throw new ArgumentException("--live-preflight-timeout-seconds is only valid in --mode live.");
        }

        if (bridgeUri is not null)
        {
            ValidateLiveBridgeUri(bridgeUri);
        }

        return new SmokeHarnessOptions(
            AgentHostProjectPath: agentHostProjectPath,
            Mode: mode,
            Scenario: scenario,
            AgentHostDllPath: agentHostDllPath,
            SkipBuildAgentHost: skipBuildAgentHost,
            OutputDirectory: outputDirectory,
            RunBridgeDirectCheck: runBridgeDirectCheck,
            RunMcpCheck: runMcpCheck,
            Configuration: configuration,
            BridgeUri: bridgeUri,
            LivePreflightTimeoutSeconds: livePreflightTimeoutSeconds);
    }

    internal static string FindRepositoryRoot()
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

    private static SmokeHarnessMode ParseMode(string rawValue) =>
        rawValue.ToLowerInvariant() switch
        {
            "mock" => SmokeHarnessMode.Mock,
            "live" => SmokeHarnessMode.Live,
            _ => throw new ArgumentException($"Unsupported mode '{rawValue}'. Expected 'mock' or 'live'.")
        };

    private static SmokeHarnessScenario ParseScenario(string rawValue) =>
        rawValue.ToLowerInvariant() switch
        {
            "smoke" => SmokeHarnessScenario.Smoke,
            "absolute-towers" => SmokeHarnessScenario.AbsoluteTowers,
            _ => throw new ArgumentException($"Unsupported scenario '{rawValue}'. Expected 'smoke' or 'absolute-towers'.")
        };

    private static string ParseConfiguration(string rawValue) =>
        rawValue switch
        {
            "Debug" or "Release" => rawValue,
            _ => throw new ArgumentException($"Unsupported configuration '{rawValue}'. Expected 'Debug' or 'Release'.")
        };

    private static int ParsePositiveInt(string rawValue, string optionName) =>
        int.TryParse(rawValue, out var value) && value > 0
            ? value
            : throw new ArgumentException($"{optionName} requires a positive integer.");

    private static void ValidateLiveBridgeUri(string bridgeUri)
    {
        if (!Uri.TryCreate(bridgeUri, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Malformed live bridge URI '{bridgeUri}'. Expected a ws:// URI.");
        }
    }

    private static string RequireValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{optionName} requires a value.");
        }

        index++;
        return args[index];
    }
}

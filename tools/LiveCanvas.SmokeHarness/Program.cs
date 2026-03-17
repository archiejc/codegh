using LiveCanvas.SmokeHarness;

var options = ParseArgs(args);
var result = await new SmokeHarnessRunner().RunAsync(options);

foreach (var check in result.CompletedChecks)
{
    Console.WriteLine($"[ok] {check}");
}

foreach (var error in result.Errors)
{
    Console.Error.WriteLine($"[error] {error}");
}

Console.WriteLine($"output_dir={result.OutputDirectory}");
Environment.ExitCode = result.Success ? 0 : 1;

static SmokeHarnessOptions ParseArgs(string[] args)
{
    var repoRoot = FindRepositoryRoot();
    var agentHostProjectPath = Path.Combine(repoRoot, "src", "LiveCanvas.AgentHost", "LiveCanvas.AgentHost.csproj");
    string? outputDirectory = null;
    var runBridgeDirectCheck = true;
    var runMcpCheck = true;
    var configuration = "Debug";

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--agent-host-project":
                agentHostProjectPath = RequireValue(args, ref i, "--agent-host-project");
                break;
            case "--output-dir":
                outputDirectory = RequireValue(args, ref i, "--output-dir");
                break;
            case "--configuration":
                configuration = RequireValue(args, ref i, "--configuration");
                break;
            case "--bridge-only":
                runBridgeDirectCheck = true;
                runMcpCheck = false;
                break;
            case "--mcp-only":
                runBridgeDirectCheck = false;
                runMcpCheck = true;
                break;
            default:
                throw new ArgumentException($"Unknown argument '{args[i]}'.");
        }
    }

    return new SmokeHarnessOptions(
        AgentHostProjectPath: agentHostProjectPath,
        OutputDirectory: outputDirectory,
        RunBridgeDirectCheck: runBridgeDirectCheck,
        RunMcpCheck: runMcpCheck,
        Configuration: configuration);
}

static string RequireValue(string[] args, ref int index, string optionName)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"{optionName} requires a value.");
    }

    index++;
    return args[index];
}

static string FindRepositoryRoot()
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

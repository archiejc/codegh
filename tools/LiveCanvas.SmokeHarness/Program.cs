using LiveCanvas.SmokeHarness;

SmokeHarnessOptions options;

try
{
    options = SmokeHarnessCli.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine($"[error] cli_usage: {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

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

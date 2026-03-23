using LiveCanvas.Core.AllowedComponents;

namespace LiveCanvas.Core.Validation;

public sealed class ConnectionValidator
{
    private readonly AllowedComponentRegistry registry;

    public ConnectionValidator(AllowedComponentRegistry registry)
    {
        this.registry = registry;
    }

    public bool IsValid(string sourceComponentKey, string sourceOutput, string targetComponentKey, string targetInput)
    {
        var source = registry.GetRequired(sourceComponentKey);
        var target = registry.GetRequired(targetComponentKey);

        return IsValid(source, sourceOutput, target, targetInput);
    }

    public bool CanValidate(string sourceComponentKey, string targetComponentKey) =>
        registry.TryGet(sourceComponentKey, out _) && registry.TryGet(targetComponentKey, out _);

    private static bool IsValid(
        LiveCanvas.Contracts.Components.AllowedComponentDefinition source,
        string sourceOutput,
        LiveCanvas.Contracts.Components.AllowedComponentDefinition target,
        string targetInput)
    {
        var hasOutput = source.Outputs.Any(port => string.Equals(port.Name, sourceOutput, StringComparison.Ordinal));
        var hasInput = target.Inputs.Any(port => string.Equals(port.Name, targetInput, StringComparison.Ordinal));

        return hasOutput && hasInput;
    }
}

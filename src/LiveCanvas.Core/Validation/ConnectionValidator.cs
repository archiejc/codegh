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

        var hasOutput = source.Outputs.Any(port => string.Equals(port.Name, sourceOutput, StringComparison.Ordinal));
        var hasInput = target.Inputs.Any(port => string.Equals(port.Name, targetInput, StringComparison.Ordinal));

        return hasOutput && hasInput;
    }
}

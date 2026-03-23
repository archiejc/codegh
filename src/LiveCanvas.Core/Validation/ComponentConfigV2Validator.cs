using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.AllowedComponents;

namespace LiveCanvas.Core.Validation;

public sealed class ComponentConfigV2Validator
{
    private const string ExpectedSchemaVersion = "gh_component_config/v2";
    private readonly AllowedComponentRegistry registry;

    public ComponentConfigV2Validator(AllowedComponentRegistry registry)
    {
        this.registry = registry;
    }

    public GhComponentConfigV2 ValidateAndNormalize(string? componentKey, GhComponentConfigV2 config)
    {
        if (!string.Equals(config.SchemaVersion, ExpectedSchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported component config schema_version '{config.SchemaVersion}'.");
        }

        if (config.Ops is null || config.Ops.Count == 0)
        {
            throw new ArgumentException("Component config v2 must include at least one op.");
        }

        var definition = componentKey is not null && registry.TryGet(componentKey, out var registered)
            ? registered
            : null;

        var normalized = new List<GhComponentConfigOp>(config.Ops.Count);
        foreach (var op in config.Ops)
        {
            normalized.Add(op switch
            {
                SetNicknameComponentConfigOp setNickname => NormalizeSetNickname(setNickname),
                SetInputPersistentDataComponentConfigOp setPersistent => NormalizeSetPersistentData(definition, setPersistent),
                ClearInputPersistentDataComponentConfigOp clearPersistent => NormalizeClearPersistentData(definition, clearPersistent),
                SetParamFlagsComponentConfigOp setParamFlags => NormalizeParamFlags(definition, setParamFlags),
                AdapterConfigComponentConfigOp adapterConfig => NormalizeAdapterConfig(adapterConfig),
                _ => throw new ArgumentException($"Unsupported component config op '{op.GetType().Name}'.")
            });
        }

        return new GhComponentConfigV2(config.SchemaVersion, normalized);
    }

    private static SetNicknameComponentConfigOp NormalizeSetNickname(SetNicknameComponentConfigOp op) =>
        new(op.Value?.Trim() ?? string.Empty);

    private static SetInputPersistentDataComponentConfigOp NormalizeSetPersistentData(
        AllowedComponentDefinition? definition,
        SetInputPersistentDataComponentConfigOp op)
    {
        EnsureInputPort(definition, op.Input);
        EnsureSupportedValue(op.Value);
        return op;
    }

    private static ClearInputPersistentDataComponentConfigOp NormalizeClearPersistentData(
        AllowedComponentDefinition? definition,
        ClearInputPersistentDataComponentConfigOp op)
    {
        EnsureInputPort(definition, op.Input);
        return op;
    }

    private static SetParamFlagsComponentConfigOp NormalizeParamFlags(
        AllowedComponentDefinition? definition,
        SetParamFlagsComponentConfigOp op)
    {
        if (string.IsNullOrWhiteSpace(op.Param))
        {
            throw new ArgumentException("set_param_flags requires a non-empty 'param' name.");
        }

        if (definition is not null
            && !definition.Inputs.Any(port => string.Equals(port.Name, op.Param, StringComparison.Ordinal))
            && !definition.Outputs.Any(port => string.Equals(port.Name, op.Param, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Unknown param '{op.Param}' for component '{definition.ComponentKey}'.");
        }

        if (op.Flatten == true && op.Graft == true)
        {
            throw new ArgumentException("flatten and graft cannot both be true on the same param.");
        }

        return op;
    }

    private static AdapterConfigComponentConfigOp NormalizeAdapterConfig(AdapterConfigComponentConfigOp op) =>
        new(op.Config switch
        {
            NumberSliderAdapterConfig slider => NormalizeSlider(slider),
            PanelAdapterConfig panel => NormalizePanel(panel),
            ColourSwatchAdapterConfig colour => NormalizeColour(colour),
            BooleanToggleAdapterConfig toggle => new BooleanToggleAdapterConfig(toggle.Value ?? false),
            ValueListAdapterConfig valueList => NormalizeValueList(valueList),
            _ => throw new ArgumentException($"Unsupported component adapter config '{op.Config.GetType().Name}'.")
        });

    private static NumberSliderAdapterConfig NormalizeSlider(NumberSliderAdapterConfig slider)
    {
        var min = slider.Min ?? DimensionClampPolicy.MinWidthOrDepth;
        var max = slider.Max ?? DimensionClampPolicy.MaxWidthOrDepth;
        if (max < min)
        {
            (min, max) = (max, min);
        }

        var value = slider.Value ?? min;
        value = Math.Clamp(value, min, max);
        return new NumberSliderAdapterConfig(min, max, value, slider.Integer);
    }

    private static PanelAdapterConfig NormalizePanel(PanelAdapterConfig panel) =>
        new(panel.Text ?? string.Empty, panel.Multiline ?? false);

    private static ColourSwatchAdapterConfig NormalizeColour(ColourSwatchAdapterConfig colour) =>
        new(
            ClampByte(colour.R ?? 255),
            ClampByte(colour.G ?? 255),
            ClampByte(colour.B ?? 255),
            ClampByte(colour.A ?? 255));

    private static ValueListAdapterConfig NormalizeValueList(ValueListAdapterConfig valueList)
    {
        IReadOnlyList<GhValueListItemConfig>? normalizedItems = null;
        if (valueList.Items is not null)
        {
            normalizedItems = valueList.Items
                .Select(item =>
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                    {
                        throw new ArgumentException("Value List items require a non-empty name.");
                    }

                    if (string.IsNullOrWhiteSpace(item.Expression))
                    {
                        throw new ArgumentException("Value List items require a non-empty expression.");
                    }

                    return new GhValueListItemConfig(item.Name.Trim(), item.Expression.Trim());
                })
                .ToArray();
        }

        return new ValueListAdapterConfig(
            normalizedItems,
            valueList.SelectedIndex,
            valueList.SelectedName?.Trim(),
            valueList.SelectedExpression?.Trim());
    }

    private static void EnsureInputPort(AllowedComponentDefinition? definition, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Component config op requires a non-empty input name.");
        }

        if (definition is not null && !definition.Inputs.Any(port => string.Equals(port.Name, input, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Unknown input '{input}' for component '{definition.ComponentKey}'.");
        }
    }

    private static void EnsureSupportedValue(GhValue value)
    {
        switch (value)
        {
            case GhNumberValue:
            case GhIntegerValue:
            case GhBooleanValue:
            case GhStringValue:
            case GhPoint3dValue:
            case GhVector3dValue:
            case GhColorValue:
                return;
            case GhListValue list:
                foreach (var item in list.Items)
                {
                    EnsureSupportedValue(item);
                }

                return;
            default:
                throw new ArgumentException($"Unsupported persistent data value type '{value.GetType().Name}'.");
        }
    }

    private static int ClampByte(int value) => Math.Clamp(value, 0, 255);
}

using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.AllowedComponents;

namespace LiveCanvas.Core.Validation;

public sealed class ComponentConfigValidator
{
    private readonly AllowedComponentRegistry registry;

    public ComponentConfigValidator(AllowedComponentRegistry registry)
    {
        this.registry = registry;
    }

    public GhComponentConfig ValidateAndNormalize(string componentKey, GhComponentConfig config)
    {
        _ = registry.GetRequired(componentKey);

        return componentKey switch
        {
            V0ComponentKeys.NumberSlider => NormalizeSlider(config),
            V0ComponentKeys.Panel => NormalizePanel(config),
            V0ComponentKeys.ColourSwatch => NormalizeColour(config),
            _ => new GhComponentConfig(Nickname: config.Nickname)
        };
    }

    private static GhComponentConfig NormalizeSlider(GhComponentConfig config)
    {
        var slider = config.Slider ?? new SliderConfig();
        var min = slider.Min ?? DimensionClampPolicy.MinWidthOrDepth;
        var max = slider.Max ?? DimensionClampPolicy.MaxWidthOrDepth;
        if (max < min)
        {
            (min, max) = (max, min);
        }

        var value = slider.Value ?? min;
        value = Math.Clamp(value, min, max);

        return new GhComponentConfig(
            Nickname: config.Nickname,
            Slider: new SliderConfig(min, max, value, slider.Integer));
    }

    private static GhComponentConfig NormalizePanel(GhComponentConfig config) =>
        new(config.Nickname, Panel: new PanelConfig(config.Panel?.Text ?? string.Empty, config.Panel?.Multiline ?? false));

    private static GhComponentConfig NormalizeColour(GhComponentConfig config)
    {
        var colour = config.Colour ?? new ColourSwatchConfig(255, 255, 255, 255);
        return new GhComponentConfig(
            Nickname: config.Nickname,
            Colour: new ColourSwatchConfig(
                ClampByte(colour.R ?? 255),
                ClampByte(colour.G ?? 255),
                ClampByte(colour.B ?? 255),
                ClampByte(colour.A ?? 255)));
    }

    private static int ClampByte(int value) => Math.Clamp(value, 0, 255);
}

using FluentAssertions;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.Core.Tests.Validation;

public class ComponentConfigValidatorTests
{
    private readonly ComponentConfigValidator validator = new(new AllowedComponentRegistry());

    [Fact]
    public void accepts_number_slider_min_max_value()
    {
        var normalized = validator.ValidateAndNormalize(
            V0ComponentKeys.NumberSlider,
            new GhComponentConfig(Slider: new SliderConfig(0, 100, 36, false)));

        normalized.Slider.Should().BeEquivalentTo(new SliderConfig(0, 100, 36, false));
    }

    [Fact]
    public void clamps_slider_value_outside_range()
    {
        var normalized = validator.ValidateAndNormalize(
            V0ComponentKeys.NumberSlider,
            new GhComponentConfig(Slider: new SliderConfig(0, 100, 150, false)));

        normalized.Slider!.Value.Should().Be(100);
    }

    [Fact]
    public void clamps_dimensions_to_v0_ranges()
    {
        var dimensions = DimensionClampPolicy.Clamp(new ApproxDimensions(500, 1, 1000));

        dimensions.Should().BeEquivalentTo(new ApproxDimensions(200, 5, 400));
    }
}

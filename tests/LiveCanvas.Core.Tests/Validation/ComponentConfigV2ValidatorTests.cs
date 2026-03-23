using FluentAssertions;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.Core.Tests.Validation;

public class ComponentConfigV2ValidatorTests
{
    private readonly ComponentConfigV2Validator validator = new(new AllowedComponentRegistry());

    [Fact]
    public void normalizes_slider_adapter_values()
    {
        var normalized = validator.ValidateAndNormalize(
            V0ComponentKeys.NumberSlider,
            new GhComponentConfigV2(
                "gh_component_config/v2",
                [
                    new AdapterConfigComponentConfigOp(new NumberSliderAdapterConfig(0, 100, 150, false))
                ]));

        var op = normalized.Ops.Should().ContainSingle().Subject.Should().BeOfType<AdapterConfigComponentConfigOp>().Subject;
        ((NumberSliderAdapterConfig)op.Config).Value.Should().Be(100);
    }

    [Fact]
    public void rejects_flatten_and_graft_on_same_param()
    {
        var act = () => validator.ValidateAndNormalize(
            V0ComponentKeys.Rectangle,
            new GhComponentConfigV2(
                "gh_component_config/v2",
                [
                    new SetParamFlagsComponentConfigOp("P", Flatten: true, Graft: true)
                ]));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*flatten*and*graft*");
    }

    [Fact]
    public void accepts_dynamic_component_when_definition_is_not_locally_known()
    {
        var normalized = validator.ValidateAndNormalize(
            componentKey: null,
            new GhComponentConfigV2(
                "gh_component_config/v2",
                [
                    new SetInputPersistentDataComponentConfigOp("X", new GhListValue([new GhIntegerValue(1), new GhIntegerValue(2)]))
                ]));

        normalized.Ops.Should().ContainSingle();
    }
}

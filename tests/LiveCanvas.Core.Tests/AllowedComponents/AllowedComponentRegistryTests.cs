using FluentAssertions;
using LiveCanvas.Core.AllowedComponents;

namespace LiveCanvas.Core.Tests.AllowedComponents;

public class AllowedComponentRegistryTests
{
    private readonly AllowedComponentRegistry registry = new();

    [Fact]
    public void contains_exact_v0_whitelist()
    {
        registry.All().Select(component => component.ComponentKey)
            .Should()
            .BeEquivalentTo(
            [
                V0ComponentKeys.NumberSlider,
                V0ComponentKeys.Panel,
                V0ComponentKeys.ConstructPoint,
                V0ComponentKeys.VectorXyz,
                V0ComponentKeys.UnitZ,
                V0ComponentKeys.XyPlane,
                V0ComponentKeys.Rectangle,
                V0ComponentKeys.Polyline,
                V0ComponentKeys.Move,
                V0ComponentKeys.Rotate,
                V0ComponentKeys.Scale,
                V0ComponentKeys.ConstructDomain,
                V0ComponentKeys.Series,
                V0ComponentKeys.Range,
                V0ComponentKeys.ListItem,
                V0ComponentKeys.BoundarySurfaces,
                V0ComponentKeys.Extrude,
                V0ComponentKeys.Loft,
                V0ComponentKeys.BoundingBox,
                V0ComponentKeys.CustomPreview,
                V0ComponentKeys.ColourSwatch
            ]);
    }

    [Fact]
    public void returns_canonical_port_names_for_rectangle()
    {
        var rectangle = registry.GetRequired(V0ComponentKeys.Rectangle);

        rectangle.Inputs.Select(input => input.Name).Should().Equal("P", "X", "Y", "R");
        rectangle.Outputs.Select(output => output.Name).Should().Equal("R");
    }

    [Fact]
    public void returns_slider_config_schema()
    {
        var slider = registry.GetRequired(V0ComponentKeys.NumberSlider);

        slider.ConfigFields.Select(field => field.Name)
            .Should()
            .Contain(["min", "max", "value", "integer"]);
    }
}

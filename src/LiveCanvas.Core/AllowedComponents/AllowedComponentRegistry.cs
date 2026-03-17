using LiveCanvas.Contracts.Components;

namespace LiveCanvas.Core.AllowedComponents;

public sealed class AllowedComponentRegistry
{
    private readonly IReadOnlyDictionary<string, AllowedComponentDefinition> definitions;

    public AllowedComponentRegistry()
    {
        definitions = BuildDefinitions()
            .ToDictionary(def => def.ComponentKey, StringComparer.Ordinal);
    }

    public IReadOnlyList<AllowedComponentDefinition> All() =>
        definitions.Values.OrderBy(def => def.ComponentKey, StringComparer.Ordinal).ToList();

    public bool TryGet(string componentKey, out AllowedComponentDefinition? definition) =>
        definitions.TryGetValue(componentKey, out definition);

    public AllowedComponentDefinition GetRequired(string componentKey) =>
        definitions.TryGetValue(componentKey, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown component key '{componentKey}'.");

    private static IEnumerable<AllowedComponentDefinition> BuildDefinitions()
    {
        yield return Define(V0ComponentKeys.NumberSlider, "Number Slider", "Input", [],
            [Output("N", 0)],
            Config("nickname", "string"), Config("min", "number"), Config("max", "number"), Config("value", "number"), Config("integer", "boolean"));
        yield return Define(V0ComponentKeys.Panel, "Panel", "Input", [Input("T", 0)], [Output("T", 0)],
            Config("nickname", "string"), Config("text", "string"), Config("multiline", "boolean"));
        yield return Define(V0ComponentKeys.ConstructPoint, "Construct Point", "Vector",
            [Input("X", 0), Input("Y", 1), Input("Z", 2)], [Output("P", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.VectorXyz, "Vector XYZ", "Vector",
            [Input("X", 0), Input("Y", 1), Input("Z", 2)], [Output("V", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.UnitZ, "Unit Z", "Vector", [], [Output("V", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.XyPlane, "XY Plane", "Vector", [], [Output("P", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Rectangle, "Rectangle", "Curve",
            [Input("P", 0), Input("X", 1), Input("Y", 2), Input("R", 3)], [Output("R", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Polyline, "Polyline", "Curve",
            [Input("V", 0), Input("C", 1)], [Output("P", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Move, "Move", "Transform",
            [Input("G", 0), Input("T", 1)], [Output("G", 0), Output("X", 1)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Rotate, "Rotate", "Transform",
            [Input("G", 0), Input("A", 1), Input("P", 2)], [Output("G", 0), Output("X", 1)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Scale, "Scale", "Transform",
            [Input("G", 0), Input("P", 1), Input("F", 2)], [Output("G", 0), Output("X", 1)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.ConstructDomain, "Construct Domain", "Maths",
            [Input("S", 0), Input("E", 1)], [Output("I", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Series, "Series", "Sets",
            [Input("S", 0), Input("N", 1), Input("C", 2)], [Output("S", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Range, "Range", "Sets",
            [Input("D", 0), Input("N", 1)], [Output("R", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.ListItem, "List Item", "Sets",
            [Input("L", 0), Input("i", 1), Input("W", 2)], [Output("i", 0), Output("R", 1)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.BoundarySurfaces, "Boundary Surfaces", "Surface",
            [Input("E", 0)], [Output("S", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Extrude, "Extrude", "Surface",
            [Input("B", 0), Input("D", 1)], [Output("E", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.Loft, "Loft", "Surface",
            [Input("C", 0), Input("O", 1)], [Output("L", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.BoundingBox, "Bounding Box", "Surface",
            [Input("G", 0), Input("P", 1)], [Output("B", 0), Output("C", 1)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.CustomPreview, "Custom Preview", "Display",
            [Input("G", 0), Input("S", 1)], [Output("G", 0)], Config("nickname", "string"));
        yield return Define(V0ComponentKeys.ColourSwatch, "Colour Swatch", "Display",
            [], [Output("C", 0)],
            Config("nickname", "string"), Config("r", "number"), Config("g", "number"), Config("b", "number"), Config("a", "number"));
    }

    private static AllowedComponentDefinition Define(
        string componentKey,
        string displayName,
        string category,
        IReadOnlyList<AllowedComponentPortInfo> inputs,
        IReadOnlyList<AllowedComponentPortInfo> outputs,
        params AllowedComponentConfigField[] configFields) =>
        new(componentKey, displayName, category, inputs, outputs, configFields);

    private static AllowedComponentPortInfo Input(string name, int index) => new(name, "input", index);
    private static AllowedComponentPortInfo Output(string name, int index) => new(name, "output", index);
    private static AllowedComponentConfigField Config(string name, string type) => new(name, type, false);
}

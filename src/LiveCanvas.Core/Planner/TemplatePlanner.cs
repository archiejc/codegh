using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.ReferenceInterpretation;

namespace LiveCanvas.Core.Planner;

public sealed class TemplatePlanner
{
    public TemplateGraphPlan CreatePlan(ReferenceBrief brief)
    {
        return brief.MassingStrategy switch
        {
            MassingTemplate.SingleExtrusion => BuildSingleExtrusion(),
            MassingTemplate.PodiumTower => BuildPodiumTower(),
            MassingTemplate.SteppedExtrusions => BuildSteppedExtrusions(),
            MassingTemplate.StackedBars => BuildStackedBars(),
            MassingTemplate.LoftedTaper => BuildLoftedTaper(),
            _ => BuildSingleExtrusion()
        };
    }

    private static TemplateGraphPlan BuildSingleExtrusion()
    {
        var components = new List<GraphComponentPlan>
        {
            Component("plane", V0ComponentKeys.XyPlane, 0, 0),
            Component("width", V0ComponentKeys.NumberSlider, 0, 1),
            Component("depth", V0ComponentKeys.NumberSlider, 0, 2),
            Component("rect", V0ComponentKeys.Rectangle, 1, 1),
            Component("surface", V0ComponentKeys.BoundarySurfaces, 2, 1),
            Component("vector", V0ComponentKeys.VectorXyz, 3, 1),
            Component("height", V0ComponentKeys.NumberSlider, 3, 2),
            Component("extrude", V0ComponentKeys.Extrude, 4, 1),
            Component("colour", V0ComponentKeys.ColourSwatch, 5, 0),
            Component("preview", V0ComponentKeys.CustomPreview, 5, 1),
            Component("bounds", V0ComponentKeys.BoundingBox, 5, 2)
        };

        var connections = new List<GraphConnectionPlan>
        {
            new("plane", "P", "rect", "P"),
            new("width", "N", "rect", "X"),
            new("depth", "N", "rect", "Y"),
            new("rect", "R", "surface", "E"),
            new("surface", "S", "extrude", "B"),
            new("height", "N", "vector", "Z"),
            new("vector", "V", "extrude", "D"),
            new("extrude", "E", "preview", "G"),
            new("colour", "C", "preview", "S"),
            new("extrude", "E", "bounds", "G")
        };

        return new TemplateGraphPlan(MassingTemplate.SingleExtrusion, components, connections);
    }

    private static TemplateGraphPlan BuildPodiumTower()
    {
        var plan = BuildSingleExtrusion();
        return plan with { TemplateName = MassingTemplate.PodiumTower };
    }

    private static TemplateGraphPlan BuildSteppedExtrusions()
    {
        var plan = BuildSingleExtrusion();
        return plan with { TemplateName = MassingTemplate.SteppedExtrusions };
    }

    private static TemplateGraphPlan BuildStackedBars()
    {
        var plan = BuildSingleExtrusion();
        return plan with { TemplateName = MassingTemplate.StackedBars };
    }

    private static TemplateGraphPlan BuildLoftedTaper()
    {
        var components = new List<GraphComponentPlan>
        {
            Component("plane", V0ComponentKeys.XyPlane, 0, 0),
            Component("base_width", V0ComponentKeys.NumberSlider, 0, 1),
            Component("base_depth", V0ComponentKeys.NumberSlider, 0, 2),
            Component("base_rect", V0ComponentKeys.Rectangle, 1, 1),
            Component("top_width", V0ComponentKeys.NumberSlider, 0, 3),
            Component("top_depth", V0ComponentKeys.NumberSlider, 0, 4),
            Component("top_rect", V0ComponentKeys.Rectangle, 1, 3),
            Component("height", V0ComponentKeys.NumberSlider, 2, 3),
            Component("vector", V0ComponentKeys.VectorXyz, 2, 4),
            Component("move", V0ComponentKeys.Move, 3, 3),
            Component("loft", V0ComponentKeys.Loft, 4, 2),
            Component("colour", V0ComponentKeys.ColourSwatch, 5, 1),
            Component("preview", V0ComponentKeys.CustomPreview, 5, 2),
            Component("bounds", V0ComponentKeys.BoundingBox, 5, 3)
        };

        var connections = new List<GraphConnectionPlan>
        {
            new("plane", "P", "base_rect", "P"),
            new("base_width", "N", "base_rect", "X"),
            new("base_depth", "N", "base_rect", "Y"),
            new("plane", "P", "top_rect", "P"),
            new("top_width", "N", "top_rect", "X"),
            new("top_depth", "N", "top_rect", "Y"),
            new("height", "N", "vector", "Z"),
            new("top_rect", "R", "move", "G"),
            new("vector", "V", "move", "T"),
            new("base_rect", "R", "loft", "C"),
            new("move", "G", "loft", "C"),
            new("loft", "L", "preview", "G"),
            new("colour", "C", "preview", "S"),
            new("loft", "L", "bounds", "G")
        };

        return new TemplateGraphPlan(MassingTemplate.LoftedTaper, components, connections);
    }

    private static GraphComponentPlan Component(string alias, string componentKey, int column, int row)
    {
        var (x, y) = CanvasLayoutPolicy.Position(column, row);
        return new(alias, componentKey, x, y, new GhComponentConfig());
    }
}

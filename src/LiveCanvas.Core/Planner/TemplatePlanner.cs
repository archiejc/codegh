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
            MassingTemplate.SteppedExtrusions => BuildSteppedExtrusions(brief),
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
        var components = new List<GraphComponentPlan>
        {
            Component("plane", V0ComponentKeys.XyPlane, 0, 0),
            Component("podium_width", V0ComponentKeys.NumberSlider, 0, 1),
            Component("podium_depth", V0ComponentKeys.NumberSlider, 0, 2),
            Component("podium_height", V0ComponentKeys.NumberSlider, 2, 2),
            Component("podium_rect", V0ComponentKeys.Rectangle, 1, 1),
            Component("podium_surface", V0ComponentKeys.BoundarySurfaces, 2, 1),
            Component("podium_vector", V0ComponentKeys.VectorXyz, 3, 1),
            Component("podium_extrude", V0ComponentKeys.Extrude, 4, 1),

            Component("tower_width", V0ComponentKeys.NumberSlider, 0, 4),
            Component("tower_depth", V0ComponentKeys.NumberSlider, 0, 5),
            Component("tower_height", V0ComponentKeys.NumberSlider, 2, 5),
            Component("tower_rect", V0ComponentKeys.Rectangle, 1, 4),
            Component("tower_surface", V0ComponentKeys.BoundarySurfaces, 2, 4),
            Component("tower_vector", V0ComponentKeys.VectorXyz, 3, 4),
            Component("tower_extrude", V0ComponentKeys.Extrude, 4, 4),
            Component("tower_lift_vector", V0ComponentKeys.VectorXyz, 3, 5),
            Component("tower_lift", V0ComponentKeys.Move, 5, 4),

            Component("colour", V0ComponentKeys.ColourSwatch, 6, 2),
            Component("preview", V0ComponentKeys.CustomPreview, 7, 2),
            Component("bounds", V0ComponentKeys.BoundingBox, 7, 3)
        };

        var connections = new List<GraphConnectionPlan>
        {
            new("plane", "P", "podium_rect", "P"),
            new("podium_width", "N", "podium_rect", "X"),
            new("podium_depth", "N", "podium_rect", "Y"),
            new("podium_rect", "R", "podium_surface", "E"),
            new("podium_surface", "S", "podium_extrude", "B"),
            new("podium_height", "N", "podium_vector", "Z"),
            new("podium_vector", "V", "podium_extrude", "D"),

            new("plane", "P", "tower_rect", "P"),
            new("tower_width", "N", "tower_rect", "X"),
            new("tower_depth", "N", "tower_rect", "Y"),
            new("tower_rect", "R", "tower_surface", "E"),
            new("tower_surface", "S", "tower_extrude", "B"),
            new("tower_height", "N", "tower_vector", "Z"),
            new("tower_vector", "V", "tower_extrude", "D"),
            new("podium_height", "N", "tower_lift_vector", "Z"),
            new("tower_extrude", "E", "tower_lift", "G"),
            new("tower_lift_vector", "V", "tower_lift", "T"),

            new("podium_extrude", "E", "preview", "G"),
            new("tower_lift", "G", "preview", "G"),
            new("colour", "C", "preview", "S"),
            new("podium_extrude", "E", "bounds", "G"),
            new("tower_lift", "G", "bounds", "G")
        };

        return new TemplateGraphPlan(MassingTemplate.PodiumTower, components, connections);
    }

    private static TemplateGraphPlan BuildSteppedExtrusions(ReferenceBrief brief)
    {
        var tierCount = Math.Clamp(brief.Leveling.StepCount ?? 3, 2, 5);
        var components = new List<GraphComponentPlan>
        {
            Component("plane", V0ComponentKeys.XyPlane, 0, 0),
            Component("colour", V0ComponentKeys.ColourSwatch, 8, 1),
            Component("preview", V0ComponentKeys.CustomPreview, 9, 1),
            Component("bounds", V0ComponentKeys.BoundingBox, 9, 2)
        };
        var connections = new List<GraphConnectionPlan>
        {
            new("colour", "C", "preview", "S")
        };

        for (var tier = 1; tier <= tierCount; tier++)
        {
            var row = tier * 2;
            var prefix = $"tier_{tier}";

            components.Add(Component($"{prefix}_width", V0ComponentKeys.NumberSlider, 0, row));
            components.Add(Component($"{prefix}_depth", V0ComponentKeys.NumberSlider, 0, row + 1));
            components.Add(Component($"{prefix}_height", V0ComponentKeys.NumberSlider, 2, row + 1));
            components.Add(Component($"{prefix}_rect", V0ComponentKeys.Rectangle, 1, row));
            components.Add(Component($"{prefix}_surface", V0ComponentKeys.BoundarySurfaces, 2, row));
            components.Add(Component($"{prefix}_vector", V0ComponentKeys.VectorXyz, 3, row));
            components.Add(Component($"{prefix}_extrude", V0ComponentKeys.Extrude, 4, row));

            connections.Add(new("plane", "P", $"{prefix}_rect", "P"));
            connections.Add(new($"{prefix}_width", "N", $"{prefix}_rect", "X"));
            connections.Add(new($"{prefix}_depth", "N", $"{prefix}_rect", "Y"));
            connections.Add(new($"{prefix}_rect", "R", $"{prefix}_surface", "E"));
            connections.Add(new($"{prefix}_surface", "S", $"{prefix}_extrude", "B"));
            connections.Add(new($"{prefix}_height", "N", $"{prefix}_vector", "Z"));
            connections.Add(new($"{prefix}_vector", "V", $"{prefix}_extrude", "D"));

            var geometryAlias = $"{prefix}_extrude";
            var geometryPort = "E";

            if (tier > 1)
            {
                components.Add(Component($"{prefix}_offset", V0ComponentKeys.NumberSlider, 2, row));
                components.Add(Component($"{prefix}_offset_vector", V0ComponentKeys.VectorXyz, 5, row));
                components.Add(Component($"{prefix}_move", V0ComponentKeys.Move, 6, row));
                connections.Add(new($"{prefix}_offset", "N", $"{prefix}_offset_vector", "Z"));
                connections.Add(new($"{prefix}_extrude", "E", $"{prefix}_move", "G"));
                connections.Add(new($"{prefix}_offset_vector", "V", $"{prefix}_move", "T"));

                geometryAlias = $"{prefix}_move";
                geometryPort = "G";
            }

            connections.Add(new(geometryAlias, geometryPort, "preview", "G"));
            connections.Add(new(geometryAlias, geometryPort, "bounds", "G"));
        }

        return new TemplateGraphPlan(MassingTemplate.SteppedExtrusions, components, connections);
    }

    private static TemplateGraphPlan BuildStackedBars()
    {
        var components = new List<GraphComponentPlan>
        {
            Component("plane", V0ComponentKeys.XyPlane, 0, 0),

            Component("bar_1_width", V0ComponentKeys.NumberSlider, 0, 1),
            Component("bar_1_depth", V0ComponentKeys.NumberSlider, 0, 2),
            Component("bar_1_height", V0ComponentKeys.NumberSlider, 2, 2),
            Component("bar_1_rect", V0ComponentKeys.Rectangle, 1, 1),
            Component("bar_1_surface", V0ComponentKeys.BoundarySurfaces, 2, 1),
            Component("bar_1_vector", V0ComponentKeys.VectorXyz, 3, 1),
            Component("bar_1_extrude", V0ComponentKeys.Extrude, 4, 1),

            Component("bar_2_width", V0ComponentKeys.NumberSlider, 0, 4),
            Component("bar_2_depth", V0ComponentKeys.NumberSlider, 0, 5),
            Component("bar_2_height", V0ComponentKeys.NumberSlider, 2, 5),
            Component("bar_2_offset_x", V0ComponentKeys.NumberSlider, 2, 3),
            Component("bar_2_offset_y", V0ComponentKeys.NumberSlider, 2, 4),
            Component("bar_2_rect", V0ComponentKeys.Rectangle, 1, 4),
            Component("bar_2_surface", V0ComponentKeys.BoundarySurfaces, 2, 4),
            Component("bar_2_vector", V0ComponentKeys.VectorXyz, 3, 4),
            Component("bar_2_extrude", V0ComponentKeys.Extrude, 4, 4),
            Component("bar_2_offset_vector", V0ComponentKeys.VectorXyz, 5, 4),
            Component("bar_2_move", V0ComponentKeys.Move, 6, 4),

            Component("bar_3_width", V0ComponentKeys.NumberSlider, 0, 7),
            Component("bar_3_depth", V0ComponentKeys.NumberSlider, 0, 8),
            Component("bar_3_height", V0ComponentKeys.NumberSlider, 2, 8),
            Component("bar_3_offset_x", V0ComponentKeys.NumberSlider, 2, 6),
            Component("bar_3_offset_y", V0ComponentKeys.NumberSlider, 2, 7),
            Component("bar_3_rect", V0ComponentKeys.Rectangle, 1, 7),
            Component("bar_3_surface", V0ComponentKeys.BoundarySurfaces, 2, 7),
            Component("bar_3_vector", V0ComponentKeys.VectorXyz, 3, 7),
            Component("bar_3_extrude", V0ComponentKeys.Extrude, 4, 7),
            Component("bar_3_offset_vector", V0ComponentKeys.VectorXyz, 5, 7),
            Component("bar_3_move", V0ComponentKeys.Move, 6, 7),

            Component("colour", V0ComponentKeys.ColourSwatch, 7, 3),
            Component("preview", V0ComponentKeys.CustomPreview, 8, 3),
            Component("bounds", V0ComponentKeys.BoundingBox, 8, 4)
        };

        var connections = new List<GraphConnectionPlan>
        {
            new("plane", "P", "bar_1_rect", "P"),
            new("bar_1_width", "N", "bar_1_rect", "X"),
            new("bar_1_depth", "N", "bar_1_rect", "Y"),
            new("bar_1_rect", "R", "bar_1_surface", "E"),
            new("bar_1_surface", "S", "bar_1_extrude", "B"),
            new("bar_1_height", "N", "bar_1_vector", "Z"),
            new("bar_1_vector", "V", "bar_1_extrude", "D"),

            new("plane", "P", "bar_2_rect", "P"),
            new("bar_2_width", "N", "bar_2_rect", "X"),
            new("bar_2_depth", "N", "bar_2_rect", "Y"),
            new("bar_2_rect", "R", "bar_2_surface", "E"),
            new("bar_2_surface", "S", "bar_2_extrude", "B"),
            new("bar_2_height", "N", "bar_2_vector", "Z"),
            new("bar_2_vector", "V", "bar_2_extrude", "D"),
            new("bar_2_offset_x", "N", "bar_2_offset_vector", "X"),
            new("bar_2_offset_y", "N", "bar_2_offset_vector", "Y"),
            new("bar_2_extrude", "E", "bar_2_move", "G"),
            new("bar_2_offset_vector", "V", "bar_2_move", "T"),

            new("plane", "P", "bar_3_rect", "P"),
            new("bar_3_width", "N", "bar_3_rect", "X"),
            new("bar_3_depth", "N", "bar_3_rect", "Y"),
            new("bar_3_rect", "R", "bar_3_surface", "E"),
            new("bar_3_surface", "S", "bar_3_extrude", "B"),
            new("bar_3_height", "N", "bar_3_vector", "Z"),
            new("bar_3_vector", "V", "bar_3_extrude", "D"),
            new("bar_3_offset_x", "N", "bar_3_offset_vector", "X"),
            new("bar_3_offset_y", "N", "bar_3_offset_vector", "Y"),
            new("bar_3_extrude", "E", "bar_3_move", "G"),
            new("bar_3_offset_vector", "V", "bar_3_move", "T"),

            new("bar_1_extrude", "E", "preview", "G"),
            new("bar_2_move", "G", "preview", "G"),
            new("bar_3_move", "G", "preview", "G"),
            new("colour", "C", "preview", "S"),
            new("bar_1_extrude", "E", "bounds", "G"),
            new("bar_2_move", "G", "bounds", "G"),
            new("bar_3_move", "G", "bounds", "G")
        };

        return new TemplateGraphPlan(MassingTemplate.StackedBars, components, connections);
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

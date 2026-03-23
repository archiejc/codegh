using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Planner;
using LiveCanvas.Contracts.ReferenceInterpretation;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.ReferenceInterpretation;
using LiveCanvas.Core.Validation;

namespace LiveCanvas.Core.Planner;

public sealed class TemplateGraphParameterizer
{
    public TemplateGraphPlan Parameterize(TemplateGraphPlan plan, ReferenceBrief brief)
    {
        var dimensions = DimensionClampPolicy.Clamp(brief.ApproxDimensions);
        var taperRatio = ClampTaperRatio(brief.TransformHints.TaperRatio);
        var sliderValues = BuildSliderValueMap(plan.TemplateName, dimensions, brief.Leveling, taperRatio);

        var parameterizedComponents = plan.Components
            .Select(component => ParameterizeComponent(component, sliderValues))
            .ToArray();

        return plan with { Components = parameterizedComponents };
    }

    private static GraphComponentPlan ParameterizeComponent(
        GraphComponentPlan component,
        IReadOnlyDictionary<string, double> sliderValues)
    {
        if (string.Equals(component.ComponentKey, V0ComponentKeys.ColourSwatch, StringComparison.Ordinal))
        {
            return component with
            {
                Config = component.Config with
                {
                    Colour = new ColourSwatchConfig(255, 255, 255, 255)
                }
            };
        }

        if (!string.Equals(component.ComponentKey, V0ComponentKeys.NumberSlider, StringComparison.Ordinal))
        {
            return component;
        }

        if (!sliderValues.TryGetValue(component.Alias, out var value))
        {
            return component;
        }

        var (min, max) = InferRange(component.Alias);
        return component with
        {
            Config = component.Config with
            {
                Slider = new SliderConfig(
                    Min: min,
                    Max: max,
                    Value: Math.Clamp(value, min, max),
                    Integer: false)
            }
        };
    }

    private static IReadOnlyDictionary<string, double> BuildSliderValueMap(
        string templateName,
        ApproxDimensions dimensions,
        LevelingHints leveling,
        double taperRatio)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["width"] = dimensions.Width,
            ["depth"] = dimensions.Depth,
            ["height"] = dimensions.Height,
            ["podium_width"] = dimensions.Width,
            ["podium_depth"] = dimensions.Depth,
            ["base_width"] = dimensions.Width,
            ["base_depth"] = dimensions.Depth,
            ["top_width"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Width * taperRatio),
            ["top_depth"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Depth * taperRatio)
        };

        if (string.Equals(templateName, MassingTemplate.PodiumTower, StringComparison.Ordinal))
        {
            var podiumHeight = Math.Clamp(
                leveling.PodiumHeight ?? dimensions.Height * 0.2,
                DimensionClampPolicy.MinHeight,
                dimensions.Height * 0.5);
            var towerHeight = Math.Clamp(
                leveling.TowerHeight ?? (dimensions.Height - podiumHeight),
                DimensionClampPolicy.MinHeight,
                DimensionClampPolicy.MaxHeight);

            values["podium_height"] = podiumHeight;
            values["tower_height"] = towerHeight;
            values["tower_width"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Width * 0.6);
            values["tower_depth"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Depth * 0.6);
        }

        if (string.Equals(templateName, MassingTemplate.StackedBars, StringComparison.Ordinal))
        {
            values["bar_1_width"] = dimensions.Width;
            values["bar_1_depth"] = dimensions.Depth;
            values["bar_1_height"] = dimensions.Height * 0.45;
            values["bar_2_width"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Width * 0.9);
            values["bar_2_depth"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Depth * 0.9);
            values["bar_2_height"] = dimensions.Height * 0.35;
            values["bar_2_offset_x"] = dimensions.Width * 0.12;
            values["bar_2_offset_y"] = dimensions.Depth * 0.08;
            values["bar_3_width"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Width * 0.8);
            values["bar_3_depth"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Depth * 0.8);
            values["bar_3_height"] = dimensions.Height * 0.20;
            values["bar_3_offset_x"] = -dimensions.Width * 0.10;
            values["bar_3_offset_y"] = dimensions.Depth * 0.10;
        }

        if (string.Equals(templateName, MassingTemplate.SteppedExtrusions, StringComparison.Ordinal))
        {
            var tierCount = Math.Clamp(leveling.StepCount ?? 3, 2, 5);
            var tierHeight = Math.Max(dimensions.Height / tierCount, DimensionClampPolicy.MinHeight);
            var cumulativeHeight = tierHeight;

            for (var i = 1; i <= tierCount; i++)
            {
                var shrink = 1 - ((i - 1) * 0.1);
                values[$"tier_{i}_width"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Width * shrink);
                values[$"tier_{i}_depth"] = DimensionClampPolicy.ClampWidthOrDepth(dimensions.Depth * shrink);
                values[$"tier_{i}_height"] = tierHeight;
                if (i > 1)
                {
                    values[$"tier_{i}_offset"] = cumulativeHeight;
                }

                cumulativeHeight += tierHeight;
            }
        }

        return values;
    }

    private static (double Min, double Max) InferRange(string alias)
    {
        if (alias.Contains("height", StringComparison.Ordinal))
        {
            return (DimensionClampPolicy.MinHeight, DimensionClampPolicy.MaxHeight);
        }

        if (alias.Contains("offset", StringComparison.Ordinal))
        {
            return (-DimensionClampPolicy.MaxWidthOrDepth, DimensionClampPolicy.MaxWidthOrDepth);
        }

        return (DimensionClampPolicy.MinWidthOrDepth, DimensionClampPolicy.MaxWidthOrDepth);
    }

    private static double ClampTaperRatio(double? taperRatio)
    {
        var raw = taperRatio ?? 0.7;
        return Math.Clamp(raw, 0.2, 1.0);
    }
}

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using LiveCanvas.Contracts.Components;

namespace LiveCanvas.RhinoPlugin.Runtime;

internal sealed class GrasshopperComponentDiscovery
{
    internal const string GuidKeyPrefix = "gh_guid:";

    public static bool TryParseGuidKey(string componentKey, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(componentKey))
        {
            return false;
        }

        if (!componentKey.StartsWith(GuidKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var token = componentKey[GuidKeyPrefix.Length..].Trim();
        return Guid.TryParseExact(token, "N", out guid) || Guid.TryParse(token, out guid);
    }

    public IReadOnlyList<AllowedComponentDefinition> DiscoverAll()
    {
        var definitions = new List<AllowedComponentDefinition>(capacity: 256);

        foreach (var proxy in Instances.ComponentServer.ObjectProxies)
        {
            if (proxy is null || proxy.Obsolete)
            {
                continue;
            }

            var def = TryCreateDefinition(
                proxy.Guid,
                proxy.Desc?.Name,
                proxy.Desc?.Category,
                proxy.Desc?.SubCategory);
            if (def is not null)
            {
                definitions.Add(def);
            }
        }

        return definitions;
    }

    public AllowedComponentDefinition? TryCreateDefinition(
        Guid proxyGuid,
        string? proxyDisplayName = null,
        string? proxyCategory = null,
        string? proxySubCategory = null)
    {
        IGH_DocumentObject? emitted;
        try
        {
            emitted = Instances.ComponentServer.EmitObject(proxyGuid);
        }
        catch
        {
            return null;
        }

        if (emitted is null)
        {
            return null;
        }

        var displayName = proxyDisplayName ?? emitted.Name ?? emitted.NickName ?? emitted.GetType().Name;
        var category = CombineCategory(proxyCategory, proxySubCategory);
        if (IsBlockedForAutomation(displayName, category, emitted))
        {
            return null;
        }

        var componentKey = $"{GuidKeyPrefix}{proxyGuid:N}";

        var (inputs, outputs) = ExtractPorts(emitted);
        var configFields = ExtractConfigFields(emitted);
        var configOps = ExtractConfigOps(emitted, inputs.Count > 0);

        return new AllowedComponentDefinition(
            ComponentKey: componentKey,
            DisplayName: displayName,
            Category: category,
            Inputs: inputs,
            Outputs: outputs,
            ConfigFields: configFields,
            ConfigOps: configOps);
    }

    private static string CombineCategory(string? category, string? subCategory)
    {
        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        subCategory = string.IsNullOrWhiteSpace(subCategory) ? null : subCategory.Trim();

        if (category is null && subCategory is null)
        {
            return "Unknown";
        }

        if (category is not null && subCategory is not null)
        {
            return $"{category}/{subCategory}";
        }

        return category ?? subCategory ?? "Unknown";
    }

    private static (IReadOnlyList<AllowedComponentPortInfo> Inputs, IReadOnlyList<AllowedComponentPortInfo> Outputs) ExtractPorts(IGH_DocumentObject emitted)
    {
        if (emitted is IGH_Component component)
        {
            var inputs = component.Params.Input
                .Select((param, index) => new AllowedComponentPortInfo(PortName(param, $"in{index}"), "input", index))
                .ToArray();
            var outputs = component.Params.Output
                .Select((param, index) => new AllowedComponentPortInfo(PortName(param, $"out{index}"), "output", index))
                .ToArray();
            return (inputs, outputs);
        }

        if (emitted is IGH_Param paramObj)
        {
            // For most params, treat as a single in/out port. Preserve v0 special behavior for key ones.
            if (paramObj is GH_NumberSlider)
            {
                return (Array.Empty<AllowedComponentPortInfo>(), [new AllowedComponentPortInfo(PortName(paramObj, "N"), "output", 0)]);
            }

            if (paramObj is GH_ColourSwatch)
            {
                return (Array.Empty<AllowedComponentPortInfo>(), [new AllowedComponentPortInfo(PortName(paramObj, "C"), "output", 0)]);
            }

            if (paramObj is GH_Panel)
            {
                var name = PortName(paramObj, "T");
                return ([new AllowedComponentPortInfo(name, "input", 0)], [new AllowedComponentPortInfo(name, "output", 0)]);
            }

            var fallback = PortName(paramObj, "X");
            return ([new AllowedComponentPortInfo(fallback, "input", 0)], [new AllowedComponentPortInfo(fallback, "output", 0)]);
        }

        return (Array.Empty<AllowedComponentPortInfo>(), Array.Empty<AllowedComponentPortInfo>());
    }

    private static IReadOnlyList<AllowedComponentConfigField> ExtractConfigFields(IGH_DocumentObject emitted)
    {
        // Keep v0-shaped config schemas for key special objects so existing copilot/apply logic continues to work.
        if (emitted is GH_NumberSlider)
        {
            return
            [
                Field("nickname", "string"),
                Field("min", "number"),
                Field("max", "number"),
                Field("value", "number"),
                Field("integer", "boolean")
            ];
        }

        if (emitted is GH_Panel)
        {
            return
            [
                Field("nickname", "string"),
                Field("text", "string"),
                Field("multiline", "boolean")
            ];
        }

        if (emitted is GH_ColourSwatch)
        {
            return
            [
                Field("nickname", "string"),
                Field("r", "number"),
                Field("g", "number"),
                Field("b", "number"),
                Field("a", "number")
            ];
        }

        return [Field("nickname", "string")];
    }

    private static IReadOnlyList<AllowedComponentConfigOpDescriptor> ExtractConfigOps(IGH_DocumentObject emitted, bool hasInputs)
    {
        var ops = new List<AllowedComponentConfigOpDescriptor>
        {
            new("set_nickname")
        };

        if (hasInputs)
        {
            ops.Add(new("set_input_persistent_data", SupportedValueTypes: ["number", "integer", "boolean", "string", "point3d", "vector3d", "color", "list"]));
            ops.Add(new("clear_input_persistent_data"));
            ops.Add(new("set_param_flags", SupportedFlags: ["flatten", "graft", "simplify"]));
        }

        switch (emitted)
        {
            case GH_NumberSlider:
                ops.Add(new("adapter_config", Adapter: "number_slider"));
                break;
            case GH_Panel:
                ops.Add(new("adapter_config", Adapter: "panel"));
                break;
            case GH_ColourSwatch:
                ops.Add(new("adapter_config", Adapter: "colour_swatch"));
                break;
            case GH_BooleanToggle:
                ops.Add(new("adapter_config", Adapter: "boolean_toggle"));
                break;
            case GH_ValueList:
                ops.Add(new("adapter_config", Adapter: "value_list"));
                break;
        }

        return ops;
    }

    private static bool IsBlockedForAutomation(string displayName, string category, IGH_DocumentObject emitted)
    {
        var haystack = string.Join(
            ' ',
            new[]
            {
                displayName,
                category,
                emitted.Name,
                emitted.NickName,
                emitted.GetType().FullName,
                emitted.GetType().Name
            }.Where(value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

        return haystack.Contains("script", StringComparison.Ordinal)
            || haystack.Contains("python", StringComparison.Ordinal)
            || haystack.Contains("csharp", StringComparison.Ordinal)
            || haystack.Contains("c#", StringComparison.Ordinal)
            || haystack.Contains("vb script", StringComparison.Ordinal)
            || haystack.Contains("fsharp", StringComparison.Ordinal)
            || haystack.Contains("f#", StringComparison.Ordinal);
    }

    private static AllowedComponentConfigField Field(string name, string type) => new(name, type, Required: false);

    private static string PortName(IGH_Param param, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(param.NickName))
        {
            return param.NickName;
        }

        if (!string.IsNullOrWhiteSpace(param.Name))
        {
            return param.Name;
        }

        return fallback;
    }
}

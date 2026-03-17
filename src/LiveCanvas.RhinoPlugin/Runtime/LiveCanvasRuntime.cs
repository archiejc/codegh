using System.Drawing;
using Grasshopper;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Session;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.PlugIns;

namespace LiveCanvas.RhinoPlugin.Runtime;

public sealed class LiveCanvasRuntime
{
    private readonly AllowedComponentRegistry allowedComponentRegistry;
    private readonly ComponentConfigValidator componentConfigValidator;
    private readonly ConnectionValidator connectionValidator;
    private readonly Dictionary<string, string> componentKeysById = new(StringComparer.Ordinal);
    private readonly List<StoredConnection> connections = [];
    private GH_Document? ownedDocument;

    public LiveCanvasRuntime(
        AllowedComponentRegistry allowedComponentRegistry,
        ComponentConfigValidator componentConfigValidator,
        ConnectionValidator connectionValidator)
    {
        this.allowedComponentRegistry = allowedComponentRegistry;
        this.componentConfigValidator = componentConfigValidator;
        this.connectionValidator = connectionValidator;
    }

    public GhSessionInfoResponse GetSessionInfo()
    {
        var activeRhinoDocument = RhinoDoc.ActiveDoc;
        var activeGrasshopperDocument = ownedDocument ?? Instances.ActiveCanvas?.Document;

        return new GhSessionInfoResponse(
            RhinoRunning: true,
            RhinoVersion: RhinoApp.Version.ToString(),
            Platform: GetPlatformName(),
            GrasshopperLoaded: IsGrasshopperAvailable(),
            ActiveDocumentName: activeGrasshopperDocument?.DisplayName,
            DocumentObjectCount: activeGrasshopperDocument?.ObjectCount ?? 0,
            Units: activeRhinoDocument?.ModelUnitSystem.ToString() ?? "Meters",
            ModelTolerance: activeRhinoDocument?.ModelAbsoluteTolerance ?? 0.01,
            ToolVersion: "0.1.0");
    }

    public GhNewDocumentResponse NewDocument(GhNewDocumentRequest request)
    {
        EnsureGrasshopperEditorLoaded();
        RemoveOwnedDocumentIfPresent();

        var document = new GH_Document
        {
            Enabled = true
        };

        document.AssociateWithRhinoDocument();
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            document.Properties.ProjectFileName = request.Name;
        }

        Instances.DocumentServer.AddDocument(document);
        ownedDocument = document;
        componentKeysById.Clear();
        connections.Clear();
        ActivateOwnedDocument();

        return new GhNewDocumentResponse(
            document.DocumentID.ToString("N"),
            request.Name ?? document.DisplayName,
            Cleared: true);
    }

    public GhListAllowedComponentsResponse ListAllowedComponents() =>
        new(allowedComponentRegistry.All());

    public GhAddComponentResponse AddComponent(GhAddComponentRequest request)
    {
        var document = RequireOwnedDocument();
        var definition = allowedComponentRegistry.GetRequired(request.ComponentKey);
        var docObject = CreateDocumentObject(request.ComponentKey);

        docObject.CreateAttributes();
        docObject.Attributes.Pivot = new PointF((float)request.X, (float)request.Y);
        document.AddObject(docObject, false, document.ObjectCount);
        componentKeysById[docObject.InstanceGuid.ToString("N")] = request.ComponentKey;

        return new GhAddComponentResponse(
            ComponentId: docObject.InstanceGuid.ToString("N"),
            ComponentKey: request.ComponentKey,
            InstanceGuid: docObject.InstanceGuid.ToString("N"),
            DisplayName: definition.DisplayName,
            X: request.X,
            Y: request.Y,
            Inputs: definition.Inputs.Select(port => port.Name).ToArray(),
            Outputs: definition.Outputs.Select(port => port.Name).ToArray());
    }

    public GhConfigureComponentResponse ConfigureComponent(GhConfigureComponentRequest request)
    {
        var docObject = ResolveOwnedObject(request.ComponentId);
        var componentKey = ResolveComponentKey(request.ComponentId);
        var normalized = componentConfigValidator.ValidateAndNormalize(componentKey, request.Config);

        if (!string.IsNullOrWhiteSpace(normalized.Nickname))
        {
            docObject.NickName = normalized.Nickname;
        }

        switch (componentKey)
        {
            case V0ComponentKeys.NumberSlider:
                ConfigureSlider((GH_NumberSlider)docObject, normalized.Slider!);
                break;
            case V0ComponentKeys.Panel:
                ConfigurePanel((GH_Panel)docObject, normalized.Panel!);
                break;
            case V0ComponentKeys.ColourSwatch:
                ConfigureColourSwatch((GH_ColourSwatch)docObject, normalized.Colour!);
                break;
        }

        docObject.ExpireSolution(false);

        return new GhConfigureComponentResponse(
            request.ComponentId,
            Applied: true,
            NormalizedConfig: normalized,
            Warnings: Array.Empty<string>());
    }

    public GhConnectResponse Connect(GhConnectRequest request)
    {
        var sourceKey = ResolveComponentKey(request.SourceId);
        var targetKey = ResolveComponentKey(request.TargetId);

        if (!connectionValidator.IsValid(sourceKey, request.SourceOutput, targetKey, request.TargetInput))
        {
            throw new ArgumentException("Invalid connection request for whitelisted components/ports.");
        }

        var sourcePort = ResolvePort(request.SourceId, request.SourceOutput, isInput: false);
        var targetPort = ResolvePort(request.TargetId, request.TargetInput, isInput: true);
        targetPort.AddSource(sourcePort);

        var connectionId = CreateConnectionId(request.SourceId, request.SourceOutput, request.TargetId, request.TargetInput);
        if (!connections.Any(existing => existing.ConnectionId == connectionId))
        {
            connections.Add(new StoredConnection(connectionId, request.SourceId, request.SourceOutput, request.TargetId, request.TargetInput));
        }

        return new GhConnectResponse(true, connectionId);
    }

    public GhDeleteComponentResponse DeleteComponent(GhDeleteComponentRequest request)
    {
        var document = RequireOwnedDocument();
        var docObject = ResolveOwnedObject(request.ComponentId);
        var removedConnections = connections.RemoveAll(connection =>
            string.Equals(connection.SourceId, request.ComponentId, StringComparison.Ordinal)
            || string.Equals(connection.TargetId, request.ComponentId, StringComparison.Ordinal));

        var deleted = document.RemoveObject(docObject, false);
        componentKeysById.Remove(request.ComponentId);

        return new GhDeleteComponentResponse(deleted, removedConnections);
    }

    public GhSolveResponse Solve(GhSolveRequest request)
    {
        var document = RequireOwnedDocument();
        ActivateOwnedDocument();
        document.NewSolution(request.ExpireAll, GH_SolutionMode.Silent);

        var messages = CollectRuntimeMessages(document);
        var warningCount = messages.Count(message => string.Equals(message.Level, "warning", StringComparison.Ordinal));
        var errorCount = messages.Count(message => string.Equals(message.Level, "error", StringComparison.Ordinal));

        return new GhSolveResponse(
            Solved: true,
            Status: errorCount > 0 ? "error" : warningCount > 0 ? "warning" : "ok",
            ObjectCount: document.ObjectCount,
            WarningCount: warningCount,
            ErrorCount: errorCount,
            Messages: messages);
    }

    public GhInspectDocumentResponse InspectDocument(GhInspectDocumentRequest request)
    {
        var document = RequireOwnedDocument();
        var components = componentKeysById.Keys
            .Select(componentId => BuildComponentSnapshot(componentId))
            .OrderBy(component => component.X)
            .ThenBy(component => component.Y)
            .ToArray();

        var bounds = document.PreviewBoundingBox;

        return new GhInspectDocumentResponse(
            DocumentId: document.DocumentID.ToString("N"),
            Components: components,
            Connections: request.IncludeConnections
                ? connections.Select(connection => new GhDocumentConnectionSnapshot(connection.SourceId, connection.SourceOutput, connection.TargetId, connection.TargetInput)).ToArray()
                : Array.Empty<GhDocumentConnectionSnapshot>(),
            RuntimeMessages: request.IncludeRuntimeMessages ? CollectRuntimeMessages(document) : Array.Empty<GhRuntimeMessage>(),
            BoundingBox: bounds.IsValid
                ? new GhBounds(bounds.Min.X, bounds.Min.Y, bounds.Min.Z, bounds.Max.X, bounds.Max.Y, bounds.Max.Z)
                : null,
            PreviewSummary: new GhPreviewSummary(
                HasGeometry: bounds.IsValid,
                PreviewObjectCount: document.Objects.OfType<IGH_PreviewObject>().Count(preview => preview.IsPreviewCapable && !preview.Hidden)));
    }

    public GhCapturePreviewResponse CapturePreview(GhCapturePreviewRequest request)
    {
        var document = RequireOwnedDocument();
        if (!string.Equals(request.Mode, "rhino_viewport", StringComparison.Ordinal))
        {
            throw new ArgumentException("Only rhino_viewport capture mode is supported in v0.");
        }

        if (request.Width <= 0 || request.Height <= 0)
        {
            throw new ArgumentException("Capture dimensions must be positive.");
        }

        ActivateOwnedDocument();
        document.NewSolution(expireAllObjects: false, GH_SolutionMode.Silent);

        var activeRhinoDocument = RhinoDoc.ActiveDoc ?? throw new InvalidOperationException("No active Rhino document is available.");
        var activeView = activeRhinoDocument.Views.ActiveView ?? throw new InvalidOperationException("No active Rhino view is available for preview capture.");

        Directory.CreateDirectory(Path.GetDirectoryName(request.Path) ?? throw new ArgumentException("Capture path must include a directory.", nameof(request.Path)));

        using var bitmap = new ViewCapture
        {
            Width = request.Width,
            Height = request.Height,
            DrawAxes = false,
            DrawGrid = false,
            DrawGridAxes = false,
            ScaleScreenItems = false,
            TransparentBackground = false
        }.CaptureToBitmap(activeView) ?? throw new InvalidOperationException("Rhino failed to capture the active viewport.");

        bitmap.Save(request.Path);

        return new GhCapturePreviewResponse(true, request.Path, request.Width, request.Height);
    }

    public GhSaveDocumentResponse SaveDocument(GhSaveDocumentRequest request)
    {
        var document = RequireOwnedDocument();
        if (!string.Equals(Path.GetExtension(request.Path), ".gh", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only .gh saves are supported in v0.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.Path) ?? throw new ArgumentException("Save path must include a directory.", nameof(request.Path)));

        var io = new GH_DocumentIO(document);
        if (!io.SaveQuiet(request.Path))
        {
            throw new InvalidOperationException($"Grasshopper failed to save '{request.Path}'.");
        }

        return new GhSaveDocumentResponse(true, request.Path, "gh");
    }

    private static string GetPlatformName()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        return "unknown";
    }

    private static bool IsGrasshopperAvailable() =>
        PlugIn.LoadPlugIn(Instances.GrasshopperPluginId, loadQuietly: true, forceLoad: false);

    private static void EnsureGrasshopperEditorLoaded()
    {
        if (!PlugIn.LoadPlugIn(Instances.GrasshopperPluginId, loadQuietly: true, forceLoad: false))
        {
            throw new InvalidOperationException("Rhino could not load the Grasshopper plugin.");
        }

        var grasshopperApi = RhinoApp.GetPlugInObject(Instances.GrasshopperPluginId)
            ?? throw new InvalidOperationException("Grasshopper automation interface is unavailable.");

        grasshopperApi.GetType().GetMethod("LoadEditor")?.Invoke(grasshopperApi, null);
    }

    private void RemoveOwnedDocumentIfPresent()
    {
        if (ownedDocument is null)
        {
            return;
        }

        if (Instances.IsDocumentServer && Instances.DocumentServer.Contains(ownedDocument))
        {
            Instances.DocumentServer.RemoveDocument(ownedDocument);
        }

        ownedDocument = null;
    }

    private GH_Document RequireOwnedDocument()
    {
        EnsureGrasshopperEditorLoaded();
        if (ownedDocument is null)
        {
            throw new InvalidOperationException("No LiveCanvas-owned Grasshopper document exists. Call gh_new_document first.");
        }

        return ownedDocument;
    }

    private void ActivateOwnedDocument()
    {
        var document = RequireOwnedDocument();

        if (!Instances.DocumentServer.Contains(document))
        {
            Instances.DocumentServer.AddDocument(document);
        }

        Instances.DocumentServer.PromoteDocument(document);
        if (Instances.ActiveCanvas is not null)
        {
            Instances.ActiveCanvas.Document = document;
        }
    }

    private IGH_DocumentObject CreateDocumentObject(string componentKey) =>
        componentKey switch
        {
            V0ComponentKeys.NumberSlider => new GH_NumberSlider(),
            V0ComponentKeys.Panel => new GH_Panel(),
            V0ComponentKeys.ColourSwatch => new GH_ColourSwatch(),
            _ => EmitBuiltinObject(componentKey)
        };

    private IGH_DocumentObject EmitBuiltinObject(string componentKey)
    {
        var definition = allowedComponentRegistry.GetRequired(componentKey);
        var proxy = Instances.ComponentServer.FindObjectByName(definition.DisplayName, true, true)
            ?? throw new InvalidOperationException($"Grasshopper could not resolve builtin component '{definition.DisplayName}'.");

        return Instances.ComponentServer.EmitObject(proxy.Guid)
            ?? throw new InvalidOperationException($"Grasshopper could not emit builtin component '{definition.DisplayName}'.");
    }

    private IGH_DocumentObject ResolveOwnedObject(string componentId)
    {
        var document = RequireOwnedDocument();
        if (!Guid.TryParse(componentId, out var instanceGuid))
        {
            throw new ArgumentException($"'{componentId}' is not a valid Grasshopper component identifier.", nameof(componentId));
        }

        return document.FindObject(instanceGuid, true)
            ?? throw new KeyNotFoundException($"Grasshopper component '{componentId}' was not found in the LiveCanvas document.");
    }

    private string ResolveComponentKey(string componentId) =>
        componentKeysById.TryGetValue(componentId, out var componentKey)
            ? componentKey
            : throw new KeyNotFoundException($"Component '{componentId}' is not tracked by the LiveCanvas runtime.");

    private IGH_Param ResolvePort(string componentId, string portName, bool isInput)
    {
        var componentKey = ResolveComponentKey(componentId);
        var definition = allowedComponentRegistry.GetRequired(componentKey);
        var portDefinition = (isInput ? definition.Inputs : definition.Outputs)
            .SingleOrDefault(port => string.Equals(port.Name, portName, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown {(isInput ? "input" : "output")} port '{portName}' for '{componentKey}'.");

        var docObject = ResolveOwnedObject(componentId);
        return docObject switch
        {
            IGH_Component component => isInput ? component.Params.Input[portDefinition.Index] : component.Params.Output[portDefinition.Index],
            IGH_Param parameter when portDefinition.Index == 0 => parameter,
            _ => throw new InvalidOperationException($"Component '{componentId}' does not expose the requested port layout.")
        };
    }

    private static void ConfigureSlider(GH_NumberSlider slider, SliderConfig sliderConfig)
    {
        var sliderBase = slider.Slider;
        sliderBase.RaiseEvents = false;
        sliderBase.Type = sliderConfig.Integer == true ? GH_SliderAccuracy.Integer : GH_SliderAccuracy.Float;
        sliderBase.DecimalPlaces = sliderConfig.Integer == true ? 0 : DetermineDecimalPlaces(sliderConfig);
        sliderBase.Minimum = (decimal)(sliderConfig.Min ?? 0);
        sliderBase.Maximum = (decimal)(sliderConfig.Max ?? sliderConfig.Min ?? 0);

        var targetValue = (decimal)(sliderConfig.Value ?? sliderConfig.Min ?? 0);
        if (!slider.TrySetSliderValue(targetValue))
        {
            slider.SetSliderValue(targetValue);
        }

        sliderBase.RaiseEvents = true;
    }

    private static void ConfigurePanel(GH_Panel panel, PanelConfig panelConfig)
    {
        panel.Properties.Multiline = panelConfig.Multiline ?? false;
        panel.SetUserText(panelConfig.Text ?? string.Empty);
    }

    private static void ConfigureColourSwatch(GH_ColourSwatch swatch, ColourSwatchConfig colourConfig)
    {
        swatch.SwatchColour = Color.FromArgb(
            colourConfig.A ?? 255,
            colourConfig.R ?? 255,
            colourConfig.G ?? 255,
            colourConfig.B ?? 255);
    }

    private static int DetermineDecimalPlaces(SliderConfig sliderConfig)
    {
        var values = new[] { sliderConfig.Min, sliderConfig.Max, sliderConfig.Value }
            .Where(value => value.HasValue)
            .Select(value => value!.Value);

        return values.Any()
            ? Math.Min(6, values.Max(CountDecimalPlaces))
            : 2;
    }

    private static int CountDecimalPlaces(double value)
    {
        var text = value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        var decimalIndex = text.IndexOf('.', StringComparison.Ordinal);
        return decimalIndex < 0 ? 0 : text.Length - decimalIndex - 1;
    }

    private static string CreateConnectionId(string sourceId, string sourceOutput, string targetId, string targetInput) =>
        $"{sourceId}:{sourceOutput}->{targetId}:{targetInput}";

    private GhDocumentComponentSnapshot BuildComponentSnapshot(string componentId)
    {
        var docObject = ResolveOwnedObject(componentId);
        var componentKey = ResolveComponentKey(componentId);

        return new GhDocumentComponentSnapshot(
            ComponentId: componentId,
            ComponentKey: componentKey,
            DisplayName: docObject.Name,
            X: docObject.Attributes.Pivot.X,
            Y: docObject.Attributes.Pivot.Y);
    }

    private static GhRuntimeMessage[] CollectRuntimeMessages(GH_Document document)
    {
        var collected = new List<GhRuntimeMessage>();

        foreach (var activeObject in document.ActiveObjects())
        {
            collected.AddRange(ReadRuntimeMessages(activeObject, GH_RuntimeMessageLevel.Warning, "warning"));
            collected.AddRange(ReadRuntimeMessages(activeObject, GH_RuntimeMessageLevel.Error, "error"));
            collected.AddRange(ReadRuntimeMessages(activeObject, GH_RuntimeMessageLevel.Remark, "remark"));
        }

        return collected.ToArray();
    }

    private static IEnumerable<GhRuntimeMessage> ReadRuntimeMessages(IGH_ActiveObject activeObject, GH_RuntimeMessageLevel level, string levelName)
    {
        foreach (var message in activeObject.RuntimeMessages(level))
        {
            yield return new GhRuntimeMessage(activeObject.InstanceGuid.ToString("N"), levelName, message);
        }
    }

    private sealed record StoredConnection(
        string ConnectionId,
        string SourceId,
        string SourceOutput,
        string TargetId,
        string TargetInput);
}

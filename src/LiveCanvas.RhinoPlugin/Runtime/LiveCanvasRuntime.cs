using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel.Data;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using LiveCanvas.Contracts.Components;
using LiveCanvas.Contracts.Documents;
using LiveCanvas.Contracts.Session;
using LiveCanvas.Core.AllowedComponents;
using LiveCanvas.Core.Validation;
using LiveCanvas.RhinoPlugin.Diagnostics;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.PlugIns;

namespace LiveCanvas.RhinoPlugin.Runtime;

public sealed class LiveCanvasRuntime
{
    private readonly AllowedComponentRegistry allowedComponentRegistry;
    private readonly ComponentConfigValidator componentConfigValidator;
    private readonly ComponentConfigV2Validator componentConfigV2Validator;
    private readonly ConnectionValidator connectionValidator;
    private readonly GrasshopperComponentDiscovery componentDiscovery = new();
    private bool hasDiscoveredComponents;
    private readonly Dictionary<string, Guid> builtinProxyIdsByComponentKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> componentKeysById = new(StringComparer.Ordinal);
    private readonly List<StoredConnection> connections = [];
    private GH_Document? ownedDocument;

    public LiveCanvasRuntime(
        AllowedComponentRegistry allowedComponentRegistry,
        ComponentConfigValidator componentConfigValidator,
        ComponentConfigV2Validator componentConfigV2Validator,
        ConnectionValidator connectionValidator)
    {
        this.allowedComponentRegistry = allowedComponentRegistry;
        this.componentConfigValidator = componentConfigValidator;
        this.componentConfigV2Validator = componentConfigV2Validator;
        this.connectionValidator = connectionValidator;
    }

    public GhSessionInfoResponse GetSessionInfo()
    {
        LiveCanvasLog.Write("runtime gh_session_info start");
        var activeRhinoDocument = RhinoDoc.ActiveDoc;
        var activeGrasshopperDocument = TryGetGrasshopperDocument();
        var grasshopperLoaded = IsGrasshopperAvailable();
        LiveCanvasLog.Write($"runtime gh_session_info snapshot rhinoDoc={(activeRhinoDocument is not null)} ghDoc={(activeGrasshopperDocument is not null)} ghLoaded={grasshopperLoaded}");

        var response = new GhSessionInfoResponse(
            RhinoRunning: true,
            RhinoVersion: RhinoApp.Version.ToString(),
            Platform: GetPlatformName(),
            GrasshopperLoaded: grasshopperLoaded,
            ActiveDocumentName: activeGrasshopperDocument?.DisplayName,
            DocumentObjectCount: activeGrasshopperDocument?.ObjectCount ?? 0,
            Units: activeRhinoDocument?.ModelUnitSystem.ToString() ?? "Meters",
            ModelTolerance: activeRhinoDocument?.ModelAbsoluteTolerance ?? 0.01,
            ToolVersion: "0.1.0");

        LiveCanvasLog.Write("runtime gh_session_info end");
        return response;
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
        new(EnsureDiscoveredAllowedComponents());

    public GhAddComponentResponse AddComponent(GhAddComponentRequest request)
    {
        var document = RequireOwnedDocument();
        var definition = EnsureAllowedComponentDefinition(request.ComponentKey);
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

    public GhConfigureComponentV2Response ConfigureComponentV2(GhConfigureComponentV2Request request)
    {
        var docObject = ResolveOwnedObject(request.ComponentId);
        var componentKey = ResolveComponentKey(request.ComponentId);
        var normalized = componentConfigV2Validator.ValidateAndNormalize(componentKey, request.Config);
        var warnings = new List<string>();

        foreach (var op in normalized.Ops)
        {
            switch (op)
            {
                case SetNicknameComponentConfigOp setNickname:
                    docObject.NickName = setNickname.Value;
                    break;
                case SetInputPersistentDataComponentConfigOp setPersistentData:
                    ApplyPersistentData(request.ComponentId, setPersistentData.Input, setPersistentData.Value);
                    break;
                case ClearInputPersistentDataComponentConfigOp clearPersistentData:
                    ClearPersistentData(request.ComponentId, clearPersistentData.Input);
                    break;
                case SetParamFlagsComponentConfigOp setParamFlags:
                    ApplyParamFlags(request.ComponentId, setParamFlags);
                    break;
                case AdapterConfigComponentConfigOp adapterConfig:
                    ApplyAdapterConfig(docObject, adapterConfig.Config, warnings);
                    break;
                default:
                    throw new ArgumentException($"Unsupported component config op '{op.GetType().Name}'.");
            }
        }

        docObject.ExpireSolution(false);
        return new GhConfigureComponentV2Response(
            request.ComponentId,
            Applied: true,
            NormalizedConfig: normalized,
            Warnings: warnings);
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

        var previewState = InspectPreviewState(document);

        return new GhInspectDocumentResponse(
            DocumentId: document.DocumentID.ToString("N"),
            Components: components,
            Connections: request.IncludeConnections
                ? connections.Select(connection => new GhDocumentConnectionSnapshot(connection.SourceId, connection.SourceOutput, connection.TargetId, connection.TargetInput)).ToArray()
                : Array.Empty<GhDocumentConnectionSnapshot>(),
            RuntimeMessages: request.IncludeRuntimeMessages ? CollectRuntimeMessages(document) : Array.Empty<GhRuntimeMessage>(),
            BoundingBox: previewState.Bounds.IsValid
                ? new GhBounds(previewState.Bounds.Min.X, previewState.Bounds.Min.Y, previewState.Bounds.Min.Z, previewState.Bounds.Max.X, previewState.Bounds.Max.Y, previewState.Bounds.Max.Z)
                : null,
            PreviewSummary: new GhPreviewSummary(
                HasGeometry: previewState.Bounds.IsValid,
                PreviewObjectCount: previewState.PreviewObjectCount));
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

    private GH_Document? TryGetGrasshopperDocument()
    {
        if (ownedDocument is not null)
        {
            return ownedDocument;
        }

        return Instances.ActiveCanvas?.Document;
    }

    private bool IsGrasshopperAvailable() =>
        TryGetGrasshopperDocument() is not null
        || RhinoApp.GetPlugInObject(Instances.GrasshopperPluginId) is not null;

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
            _ => EmitComponentObject(componentKey)
        };

    private IGH_DocumentObject EmitComponentObject(string componentKey)
    {
        if (GrasshopperComponentDiscovery.TryParseGuidKey(componentKey, out var proxyGuid))
        {
            var emitted = Instances.ComponentServer.EmitObject(proxyGuid);
            if (emitted is null)
            {
                throw new InvalidOperationException($"Grasshopper could not emit component proxy '{proxyGuid:N}'.");
            }

            return emitted;
        }

        return EmitBuiltinObject(componentKey);
    }

    private IGH_DocumentObject EmitBuiltinObject(string componentKey)
    {
        var definition = allowedComponentRegistry.GetRequired(componentKey);
        if (builtinProxyIdsByComponentKey.TryGetValue(componentKey, out var cachedProxyId))
        {
            var cachedObject = Instances.ComponentServer.EmitObject(cachedProxyId);
            if (cachedObject is not null)
            {
                return cachedObject;
            }
        }

        IGH_DocumentObject? bestObject = null;
        Guid bestProxyId = Guid.Empty;
        var bestScore = int.MinValue;

        foreach (var proxy in Instances.ComponentServer.ObjectProxies.Where(proxy => !proxy.Obsolete))
        {
            IGH_DocumentObject? emitted;
            try
            {
                emitted = Instances.ComponentServer.EmitObject(proxy.Guid);
            }
            catch
            {
                continue;
            }

            if (emitted is null)
            {
                continue;
            }

            var score = ScoreProxyMatch(componentKey, proxy.Desc.Name, emitted, definition);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestProxyId = proxy.Guid;
            bestObject = emitted;
        }

        if (bestObject is null || bestScore <= 0)
        {
            throw new InvalidOperationException($"Grasshopper could not resolve builtin component '{definition.DisplayName}'.");
        }

        builtinProxyIdsByComponentKey[componentKey] = bestProxyId;
        LiveCanvasLog.Write($"runtime resolved componentKey={componentKey} displayName={definition.DisplayName} proxy={bestProxyId} score={bestScore} type={bestObject.GetType().FullName}");
        return bestObject;
    }

    private IReadOnlyList<AllowedComponentDefinition> EnsureDiscoveredAllowedComponents()
    {
        EnsureGrasshopperEditorLoaded();
        if (!hasDiscoveredComponents)
        {
            var discovered = componentDiscovery.DiscoverAll();
            allowedComponentRegistry.UpsertRange(discovered);
            hasDiscoveredComponents = true;
        }

        return allowedComponentRegistry.All();
    }

    private AllowedComponentDefinition EnsureAllowedComponentDefinition(string componentKey)
    {
        if (allowedComponentRegistry.TryGet(componentKey, out var definition) && definition is not null)
        {
            return definition;
        }

        EnsureGrasshopperEditorLoaded();

        if (GrasshopperComponentDiscovery.TryParseGuidKey(componentKey, out var proxyGuid))
        {
            var discovered = componentDiscovery.TryCreateDefinition(proxyGuid);
            if (discovered is not null)
            {
                allowedComponentRegistry.Upsert(discovered);
                return discovered;
            }
        }

        // Fall back to the existing registry behavior for v0 keys (or anything already registered).
        return allowedComponentRegistry.GetRequired(componentKey);
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

    private static bool MatchesPortLayout(IGH_DocumentObject docObject, AllowedComponentDefinition definition) =>
        docObject switch
        {
            IGH_Component component => PortsMatch(component.Params.Input, definition.Inputs)
                && PortsMatch(component.Params.Output, definition.Outputs),
            IGH_Param _ => definition.Inputs.Count <= 1 && definition.Outputs.Count <= 1,
            _ => false
        };

    private static int ScoreProxyMatch(string componentKey, string proxyName, IGH_DocumentObject docObject, AllowedComponentDefinition definition)
    {
        var exactNameBonus = string.Equals(proxyName, definition.DisplayName, StringComparison.OrdinalIgnoreCase) ? 50 : 0;
        var strongNameOrTypeMatch = IsStrongNameOrTypeMatch(componentKey, definition.DisplayName, proxyName, docObject);
        if (!strongNameOrTypeMatch)
        {
            return 0;
        }

        if (!MatchesPortLayout(docObject, definition))
        {
            return HasRequiredPortCapacity(docObject, definition)
                ? 75 + exactNameBonus
                : 0;
        }

        return docObject switch
        {
            IGH_Component => 100 + exactNameBonus + (strongNameOrTypeMatch ? 25 : 0),
            IGH_Param => 10 + exactNameBonus + (strongNameOrTypeMatch ? 5 : 0),
            _ => 0
        };
    }

    private static bool PortsMatch(IReadOnlyList<IGH_Param> actualPorts, IReadOnlyList<AllowedComponentPortInfo> expectedPorts)
    {
        for (var index = 0; index < expectedPorts.Count; index++)
        {
            var expectedPort = expectedPorts[index];
            if (expectedPort.Index < 0 || expectedPort.Index >= actualPorts.Count)
            {
                return false;
            }

            var actualPort = actualPorts[expectedPort.Index];
            var actualName = actualPort.NickName;
            if (!string.Equals(actualName, expectedPort.Name, StringComparison.Ordinal)
                && !string.Equals(actualPort.Name, expectedPort.Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasRequiredPortCapacity(IGH_DocumentObject docObject, AllowedComponentDefinition definition) =>
        docObject switch
        {
            IGH_Component component => component.Params.Input.Count >= definition.Inputs.Count
                && component.Params.Output.Count >= definition.Outputs.Count,
            IGH_Param _ => definition.Inputs.Count <= 1 && definition.Outputs.Count <= 1,
            _ => false
        };

    private static bool IsStrongNameOrTypeMatch(
        string componentKey,
        string displayName,
        string proxyName,
        IGH_DocumentObject docObject)
    {
        var normalizedKey = NormalizeLookupToken(componentKey);
        if (normalizedKey.Length == 0)
        {
            return false;
        }

        return MatchesLookupToken(normalizedKey, displayName)
            || MatchesLookupToken(normalizedKey, proxyName)
            || MatchesLookupToken(normalizedKey, docObject.Name)
            || MatchesLookupToken(normalizedKey, docObject.NickName)
            || MatchesLookupToken(normalizedKey, docObject.GetType().Name)
            || MatchesLookupToken(normalizedKey, docObject.GetType().FullName);
    }

    private static bool MatchesLookupToken(string normalizedKey, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && NormalizeLookupToken(candidate).Contains(normalizedKey, StringComparison.Ordinal);

    private static string NormalizeLookupToken(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer[length++] = char.ToLowerInvariant(ch);
            }
        }

        return length == 0 ? string.Empty : new string(buffer[..length]);
    }

    private void ApplyPersistentData(string componentId, string inputName, GhValue value)
    {
        var input = ResolvePort(componentId, inputName, isInput: true);
        if (input.SourceCount > 0)
        {
            throw new ArgumentException($"Cannot set persistent data on input '{inputName}' because it already has source wires.");
        }

        var values = FlattenPersistentDataValues(value).ToArray();
        var setPersistentData = input.GetType().GetMethod("SetPersistentData", [typeof(object[])]);
        var clearPersistentData = input.GetType().GetMethod("Script_ClearPersistentData", Type.EmptyTypes);
        if (setPersistentData is null || clearPersistentData is null)
        {
            throw new ArgumentException($"Input '{inputName}' does not support persistent data.");
        }

        clearPersistentData.Invoke(input, []);
        setPersistentData.Invoke(input, [values]);
        input.ExpireSolution(false);
    }

    private void ClearPersistentData(string componentId, string inputName)
    {
        var input = ResolvePort(componentId, inputName, isInput: true);
        var clearPersistentData = input.GetType().GetMethod("Script_ClearPersistentData", Type.EmptyTypes);
        if (clearPersistentData is null)
        {
            throw new ArgumentException($"Input '{inputName}' does not support persistent data.");
        }

        clearPersistentData.Invoke(input, []);
        input.ExpireSolution(false);
    }

    private void ApplyParamFlags(string componentId, SetParamFlagsComponentConfigOp op)
    {
        var componentKey = ResolveComponentKey(componentId);
        var definition = allowedComponentRegistry.GetRequired(componentKey);
        var isInput = definition.Inputs.Any(port => string.Equals(port.Name, op.Param, StringComparison.Ordinal));
        var param = ResolvePort(componentId, op.Param, isInput);

        if (op.Flatten == true)
        {
            param.DataMapping = GH_DataMapping.Flatten;
        }
        else if (op.Graft == true)
        {
            param.DataMapping = GH_DataMapping.Graft;
        }
        else if (op.Flatten == false || op.Graft == false)
        {
            param.DataMapping = GH_DataMapping.None;
        }

        if (op.Simplify.HasValue)
        {
            param.Simplify = op.Simplify.Value;
        }

        param.ExpireSolution(false);
    }

    private void ApplyAdapterConfig(IGH_DocumentObject docObject, GhComponentAdapterConfig config, List<string> warnings)
    {
        switch (config)
        {
            case NumberSliderAdapterConfig slider when docObject is GH_NumberSlider sliderObject:
                ConfigureSlider(sliderObject, new SliderConfig(slider.Min, slider.Max, slider.Value, slider.Integer));
                return;
            case PanelAdapterConfig panel when docObject is GH_Panel panelObject:
                ConfigurePanel(panelObject, new PanelConfig(panel.Text, panel.Multiline));
                return;
            case ColourSwatchAdapterConfig colour when docObject is GH_ColourSwatch swatch:
                ConfigureColourSwatch(swatch, new ColourSwatchConfig(colour.R, colour.G, colour.B, colour.A));
                return;
            case BooleanToggleAdapterConfig toggle when docObject is GH_BooleanToggle booleanToggle:
                booleanToggle.Value = toggle.Value ?? false;
                return;
            case ValueListAdapterConfig valueList when docObject is GH_ValueList ghValueList:
                ConfigureValueList(ghValueList, valueList, warnings);
                return;
            default:
                throw new ArgumentException($"Adapter config '{config.GetType().Name}' is not supported for component '{docObject.Name}'.");
        }
    }

    private static void ConfigureValueList(GH_ValueList valueList, ValueListAdapterConfig config, List<string> warnings)
    {
        if (config.Items is not null)
        {
            valueList.ListItems.Clear();
            foreach (var item in config.Items)
            {
                valueList.ListItems.Add(new GH_ValueListItem(item.Name, item.Expression));
            }
        }

        if (config.SelectedIndex.HasValue)
        {
            if (config.SelectedIndex.Value < 0 || config.SelectedIndex.Value >= valueList.ListItems.Count)
            {
                throw new ArgumentException($"Value List selected_index {config.SelectedIndex.Value} is out of range.");
            }

            valueList.SelectItem(config.SelectedIndex.Value);
            return;
        }

        if (!string.IsNullOrWhiteSpace(config.SelectedName))
        {
            var index = valueList.ListItems.FindIndex(item => string.Equals(item.Name, config.SelectedName, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new ArgumentException($"Value List item '{config.SelectedName}' does not exist.");
            }

            valueList.SelectItem(index);
            return;
        }

        if (!string.IsNullOrWhiteSpace(config.SelectedExpression))
        {
            var index = valueList.ListItems.FindIndex(item => string.Equals(item.Expression, config.SelectedExpression, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new ArgumentException($"Value List expression '{config.SelectedExpression}' does not exist.");
            }

            valueList.SelectItem(index);
            return;
        }

        if (config.Items is not null && valueList.ListItems.Count > 0)
        {
            warnings.Add("value_list_selection_preserved");
        }
    }

    private static IEnumerable<object> FlattenPersistentDataValues(GhValue value) =>
        value switch
        {
            GhNumberValue number => [number.Value],
            GhIntegerValue integer => [integer.Value],
            GhBooleanValue boolean => [boolean.Value],
            GhStringValue text => [text.Value],
            GhPoint3dValue point => [new Point3d(point.X, point.Y, point.Z)],
            GhVector3dValue vector => [new Vector3d(vector.X, vector.Y, vector.Z)],
            GhColorValue color => [Color.FromArgb(color.A, color.R, color.G, color.B)],
            GhListValue list => list.Items.SelectMany(FlattenPersistentDataValues),
            _ => throw new ArgumentException($"Unsupported persistent data value type '{value.GetType().Name}'.")
        };

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

    private static PreviewInspectionState InspectPreviewState(GH_Document document)
    {
        var previewObjects = document.Objects
            .OfType<IGH_PreviewObject>()
            .Where(preview => preview.IsPreviewCapable && !preview.Hidden)
            .ToArray();

        var bounds = BoundingBox.Unset;
        foreach (var preview in previewObjects)
        {
            try
            {
                var clippingBox = preview.ClippingBox;
                var label = preview is IGH_DocumentObject docObject
                    ? $"{docObject.Name} ({docObject.InstanceGuid:N})"
                    : preview.GetType().FullName ?? preview.GetType().Name;

                LiveCanvasLog.Write($"runtime inspect preview label={label} valid={clippingBox.IsValid} min={FormatPoint(clippingBox.Min)} max={FormatPoint(clippingBox.Max)}");

                if (!clippingBox.IsValid)
                {
                    continue;
                }

                if (!bounds.IsValid)
                {
                    bounds = clippingBox;
                    continue;
                }

                bounds.Union(clippingBox);
            }
            catch (Exception ex)
            {
                LiveCanvasLog.Write($"runtime inspect preview failed for {preview.GetType().FullName}: {ex}");
            }
        }

        return new PreviewInspectionState(bounds, previewObjects.Length);
    }

    private static string FormatPoint(Point3d point) =>
        point.IsValid
            ? $"{point.X:0.###},{point.Y:0.###},{point.Z:0.###}"
            : "invalid";

    private sealed record StoredConnection(
        string ConnectionId,
        string SourceId,
        string SourceOutput,
        string TargetId,
        string TargetInput);

    private sealed record PreviewInspectionState(
        BoundingBox Bounds,
        int PreviewObjectCount);
}

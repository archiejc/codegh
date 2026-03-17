namespace LiveCanvas.Contracts.Session;

public sealed record GhSessionInfoRequest : IToolRequest;

public sealed record GhSessionInfoResponse(
    bool RhinoRunning,
    string? RhinoVersion,
    string Platform,
    bool GrasshopperLoaded,
    string? ActiveDocumentName,
    int DocumentObjectCount,
    string Units,
    double ModelTolerance,
    string ToolVersion);

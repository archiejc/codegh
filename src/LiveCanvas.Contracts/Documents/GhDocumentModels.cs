namespace LiveCanvas.Contracts.Documents;

public sealed record GhNewDocumentRequest(string? Name = null) : IToolRequest;

public sealed record GhNewDocumentResponse(
    string DocumentId,
    string Name,
    bool Cleared);

public sealed record GhSaveDocumentRequest(string Path) : IToolRequest;

public sealed record GhSaveDocumentResponse(
    bool Saved,
    string Path,
    string Format);

public sealed record GhCapturePreviewRequest(
    string Path,
    string Mode,
    int Width,
    int Height) : IToolRequest;

public sealed record GhCapturePreviewResponse(
    bool Captured,
    string Path,
    int Width,
    int Height);

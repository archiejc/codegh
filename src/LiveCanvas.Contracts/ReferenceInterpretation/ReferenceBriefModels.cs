namespace LiveCanvas.Contracts.ReferenceInterpretation;

public sealed record ApproxDimensions(
    double Width,
    double Depth,
    double Height);

public sealed record LevelingHints(
    double? PodiumHeight,
    double? TowerHeight,
    int? StepCount);

public sealed record TransformHints(
    double? RotationDegrees,
    double? TaperRatio,
    string? OffsetPattern);

public sealed record StyleHints(
    IReadOnlyList<int>? Color,
    string? Silhouette);

public sealed record ReferenceBrief(
    string BuildingType,
    string SiteContext,
    string MassingStrategy,
    ApproxDimensions ApproxDimensions,
    LevelingHints Leveling,
    TransformHints TransformHints,
    StyleHints StyleHints,
    double Confidence,
    IReadOnlyList<string> Assumptions);

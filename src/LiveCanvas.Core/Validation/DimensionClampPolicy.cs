using LiveCanvas.Contracts.ReferenceInterpretation;

namespace LiveCanvas.Core.Validation;

public static class DimensionClampPolicy
{
    public const double MinWidthOrDepth = 5;
    public const double MaxWidthOrDepth = 200;
    public const double MinHeight = 3;
    public const double MaxHeight = 400;
    public const int MinStepCount = 1;
    public const int MaxStepCount = 12;

    public static ApproxDimensions Clamp(ApproxDimensions dimensions) =>
        new(
            ClampWidthOrDepth(dimensions.Width),
            ClampWidthOrDepth(dimensions.Depth),
            Math.Clamp(dimensions.Height, MinHeight, MaxHeight));

    public static int ClampStepCount(int value) =>
        Math.Clamp(value, MinStepCount, MaxStepCount);

    public static double ClampWidthOrDepth(double value) =>
        Math.Clamp(value, MinWidthOrDepth, MaxWidthOrDepth);
}

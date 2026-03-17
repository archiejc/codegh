namespace LiveCanvas.Core.Planner;

public static class CanvasLayoutPolicy
{
    public const int HorizontalSpacing = 140;
    public const int VerticalSpacing = 90;

    public static (double X, double Y) Position(int column, int row) =>
        (column * HorizontalSpacing, row * VerticalSpacing);
}

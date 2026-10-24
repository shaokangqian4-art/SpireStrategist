namespace SpireStrategist.Core;

public sealed record PanelLayout(float X = 16, float Y = 110, bool Collapsed = false)
{
    public PanelLayout Clamp(float width, float height, float viewportWidth, float viewportHeight) => this with
    {
        X = Math.Clamp(float.IsFinite(X) ? X : 16, 0, Math.Max(0, viewportWidth - Math.Min(width, viewportWidth))),
        Y = Math.Clamp(float.IsFinite(Y) ? Y : 110, 0, Math.Max(0, viewportHeight - Math.Min(height, viewportHeight)))
    };
}

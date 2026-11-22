using System;
using Godot;

namespace CSVM.Tooling;

/// <summary>Non-interactive top-down view of one chapter-world ribbon before mission setup.</summary>
[Tool]
internal sealed partial class CampaignDangerRibbonPreview : Control
{
    private CampaignDangerRibbon? _ribbon;

    public CampaignDangerRibbonPreview()
    {
        CustomMinimumSize = new Vector2(0f, 150f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var rect = new Rect2(0f, 0f, Size.X, Size.Y);
        DrawRect(rect, new Color(0.08f, 0.09f, 0.11f, 1f));
        DrawRect(rect, new Color(0.55f, 0.58f, 0.62f, 1f), false, 1f);
        if (_ribbon == null || _ribbon.Points.Length < 2)
        {
            return;
        }

        float minX = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;
        foreach (Vector3 point in _ribbon.Points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Z))
            {
                return;
            }

            minX = MathF.Min(minX, point.X);
            maxX = MathF.Max(maxX, point.X);
            minZ = MathF.Min(minZ, point.Z);
            maxZ = MathF.Max(maxZ, point.Z);
        }

        float width = MathF.Max(1f, maxX - minX);
        float height = MathF.Max(1f, maxZ - minZ);
        if (!float.IsFinite(width) || !float.IsFinite(height))
        {
            return;
        }

        float scale = MathF.Min(MathF.Max(1f, Size.X - 24f) / width, MathF.Max(1f, Size.Y - 24f) / height);
        var center = new Vector2(minX + (width * 0.5f), minZ + (height * 0.5f));
        var screenCenter = Size * 0.5f;
        Vector2 Project(Vector3 point) =>
            screenCenter + new Vector2(point.X - center.X, center.Y - point.Z) * scale;

        Vector2 previous = Project(_ribbon.Points[0]);
        for (int i = 1; i < _ribbon.Points.Length; i++)
        {
            Vector2 next = Project(_ribbon.Points[i]);
            DrawLine(previous, next, new Color(0.35f, 0.8f, 1f, 1f), 2f, true);
            previous = next;
        }

        DrawCircle(Project(_ribbon.Points[0]), 3f, new Color(0.45f, 1f, 0.5f, 1f));
        DrawCircle(previous, 3f, new Color(1f, 0.55f, 0.35f, 1f));
    }

    internal void ShowRibbon(CampaignDangerRibbon? ribbon)
    {
        _ribbon = ribbon;
        QueueRedraw();
    }
}

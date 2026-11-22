using Godot;

namespace CSVM.Tooling;

/// <summary>Draws the editor's numeric cloud-band and fog-range weather preview.</summary>
[Tool]
internal sealed partial class CampaignWeatherPreview : Control
{
    private static readonly Color FrameColor =
        new(0.55f, 0.58f, 0.62f, 1f);
    private static readonly Color MarkerColor =
        new(0.2f, 0.23f, 0.27f, 1f);
    private static readonly Color ProbeColor =
        new(1f, 0.72f, 0.12f, 1f);
    private static readonly Color FogColor =
        new(0.55f, 0.72f, 0.9f, 1f);
    private static readonly Color ClipColor =
        new(0.95f, 0.55f, 0.25f, 1f);

    private CampaignWeatherSummary? _summary;

    public CampaignWeatherPreview()
    {
        CustomMinimumSize =
            new Vector2(0f, 170f);
        MouseFilter =
            MouseFilterEnum.Ignore;
    }

    /// <summary>Replaces the plotted summary and schedules one redraw.</summary>
    public void ShowSummary(
        CampaignWeatherSummary? summary)
    {
        _summary = summary;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_summary == null)
        {
            return;
        }

        float height =
            Mathf.Max(80f, Size.Y - 24f);
        var bandRect =
            new Rect2(
                18f,
                12f,
                Mathf.Max(48f, Size.X * 0.34f),
                height);
        DrawBand(
            bandRect,
            _summary.BandPlot);

        var fogRect =
            new Rect2(
                bandRect.End.X + 30f,
                (height * 0.5f) + 8f,
                Mathf.Max(
                    80f,
                    Size.X
                    - bandRect.End.X
                    - 48f),
                28f);
        DrawFog(
            fogRect,
            _summary.FogPlot);
    }

    private void DrawBand(
        Rect2 rect,
        CampaignWeatherBandPlot plot)
    {
        DrawRect(
            rect,
            new Color(0.08f, 0.09f, 0.11f, 1f));
        DrawRect(
            rect,
            FrameColor,
            false,
            1f);
        if (!plot.HasBand)
        {
            DrawLine(
                rect.Position,
                rect.End,
                MarkerColor,
                2f);
            return;
        }

        float stripHeight =
            rect.Size.Y
            / plot.Samples.Count;
        foreach (CampaignWeatherBandSample sample
            in plot.Samples)
        {
            float y =
                rect.End.Y
                - (sample.Position * rect.Size.Y)
                - (stripHeight * 0.5f);
            Color color =
                sample.Color
                ?? new Color(
                    0.75f,
                    0.75f,
                    0.75f);
            color.A =
                0.15f
                + (sample.Amount * 0.85f);
            DrawRect(
                new Rect2(
                    rect.Position.X + 1f,
                    y,
                    rect.Size.X - 2f,
                    stripHeight + 1f),
                color);
        }

        foreach (CampaignWeatherBandMarker marker
            in plot.Markers)
        {
            float y =
                rect.End.Y
                - (marker.Position * rect.Size.Y);
            DrawLine(
                new Vector2(rect.Position.X, y),
                new Vector2(rect.End.X, y),
                marker.Name == "midpoint"
                    ? FrameColor
                    : MarkerColor,
                marker.Name == "midpoint"
                    ? 2f
                    : 1f);
        }

        float probeY =
            rect.End.Y
            - (plot.ProbePosition * rect.Size.Y);
        DrawLine(
            new Vector2(rect.Position.X - 6f, probeY),
            new Vector2(rect.End.X + 6f, probeY),
            ProbeColor,
            2f);
    }

    private void DrawFog(
        Rect2 rect,
        CampaignWeatherFogPlot? plot)
    {
        DrawRect(
            rect,
            new Color(0.08f, 0.09f, 0.11f, 1f));
        DrawRect(
            rect,
            FrameColor,
            false,
            1f);
        if (!plot.HasValue)
        {
            return;
        }

        CampaignWeatherFogPlot value =
            plot.Value;
        float y =
            rect.GetCenter().Y;
        float near =
            rect.Position.X
            + (value.NearPosition
                * rect.Size.X);
        float far =
            rect.Position.X
            + (value.FarPosition
                * rect.Size.X);
        float clip =
            rect.Position.X
            + (value.ClipPosition
                * rect.Size.X);
        DrawLine(
            new Vector2(near, y),
            new Vector2(far, y),
            FogColor,
            8f);
        DrawLine(
            new Vector2(clip, rect.Position.Y),
            new Vector2(clip, rect.End.Y),
            ClipColor,
            2f);
    }
}

using System;
using System.Collections.Generic;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Read-only campaign weather labels and plot values over invented readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignWeatherDiagnosticsTests
{
    private static readonly CampaignMission Mission =
        new(
            0,
            "Invented mission",
            6,
            1,
            "Invented",
            false);

    [Fact]
    public void SummaryMatchesRuntimeThresholdsAndSelectedZone()
    {
        CampaignMissionDocument document =
            Document(
                WithBand(
                    "ZONE1",
                    Zone(
                        100f,
                        400f,
                        500f,
                        0.8f,
                        0.2f,
                        -25f,
                        90f),
                    "ZONE2",
                    Zone(
                        50f,
                        500f,
                        600f,
                        1f,
                        0.2f,
                        -65f,
                        90f)));

        Assert.True(
            CampaignWeatherDiagnostics.TryCreate(
                document,
                out CampaignWeatherDiagnostics? diagnostics,
                out string problem),
            problem);
        Assert.Equal(
            new[] { "zone1", "zone2" },
            diagnostics.ZoneNames);

        CampaignWeatherSummary summary =
            diagnostics.Summary(
                "ZONE2",
                1200f);
        Assert.Equal("zone2", summary.SelectedZone);
        Assert.Equal(1200f, summary.Altitude);
        Assert.Equal(0.5f, summary.WhiteoutAmount);
        Assert.Equal(
            new Color(0.2f, 0f, 0.8f),
            summary.WhiteoutColor);
        Assert.Equal(1, summary.BandCameraState);
        Assert.Contains(
            "1000 to 2000 m",
            summary.CloudLabel);
        Assert.Contains(
            "opaque core 1400 to 1600 m",
            summary.CloudLabel);
        Assert.Contains(
            "base whiteout 50%",
            summary.ProbeLabel);
        Assert.Contains(
            "Selected zone: zone2",
            summary.ZoneLabel);
        Assert.Contains(
            "distance 50 to 500 m",
            summary.ZoneLabel);
        Assert.Contains(
            "clip far 600 m (informational)",
            summary.ZoneLabel);
        Assert.Contains(
            "Precipitation: rain",
            summary.PrecipitationLabel);
        Assert.Contains(
            "world 0.66",
            summary.LightingLabel);
        Assert.Contains(
            "(-65, 90, 0) deg",
            summary.LightingLabel);

        CampaignWeatherBandPlot band =
            summary.BandPlot;
        Assert.True(band.HasBand);
        Assert.Collection(
            band.Markers,
            marker =>
                AssertMarker(
                    marker,
                    "bottom",
                    1000f,
                    0f),
            marker =>
                AssertMarker(
                    marker,
                    "core bottom / state 2",
                    1400f,
                    0.4f),
            marker =>
                AssertMarker(
                    marker,
                    "midpoint",
                    1500f,
                    0.5f),
            marker =>
                AssertMarker(
                    marker,
                    "core top",
                    1600f,
                    0.6f),
            marker =>
                AssertMarker(
                    marker,
                    "top",
                    2000f,
                    1f));
        Assert.Equal(0.2f, band.ProbePosition);
        Assert.Equal(32, band.Samples.Count);

        CampaignWeatherFogPlot fog =
            Assert.IsType<CampaignWeatherFogPlot>(
                summary.FogPlot);
        Assert.Equal(50f, fog.Near);
        Assert.Equal(500f, fog.Far);
        Assert.Equal(600f, fog.ClipFar);
        Assert.Equal(50f / 600f, fog.NearPosition);
        Assert.Equal(500f / 600f, fog.FarPosition);
        Assert.Equal(1f, fog.ClipPosition);
    }

    [Fact]
    public void ZoneThreeAndAbsentAtmosphereStayExplicit()
    {
        CampaignMissionDocument document =
            Document(
                new List<object?>
                {
                    "ZONE1",
                    Zone(
                        100f,
                        300f,
                        350f,
                        1f,
                        0.2f,
                        -25f,
                        -135f),
                    "ZONE3",
                    Zone(
                        10f,
                        200f,
                        250f,
                        0.6f,
                        0.15f,
                        -25f,
                        -135f),
                });

        Assert.True(
            CampaignWeatherDiagnostics.TryCreate(
                document,
                out CampaignWeatherDiagnostics? diagnostics,
                out string problem),
            problem);
        CampaignWeatherSummary summary =
            diagnostics.Summary(
                "zone3",
                5000f);

        Assert.Equal(
            new[] { "zone1", "zone3" },
            diagnostics.ZoneNames);
        Assert.Equal("Cloud band: absent.", summary.CloudLabel);
        Assert.Equal(
            "Precipitation: absent.",
            summary.PrecipitationLabel);
        Assert.Null(summary.WhiteoutColor);
        Assert.Equal(0f, summary.WhiteoutAmount);
        Assert.Equal(1, summary.BandCameraState);
        Assert.False(summary.BandPlot.HasBand);
        Assert.Empty(summary.BandPlot.Markers);
        Assert.Empty(summary.BandPlot.Samples);
        Assert.Contains(
            "Selected zone: zone3",
            summary.ZoneLabel);
    }

    [Fact]
    public void RebindingAfterAnEditRefreshesDerivedValues()
    {
        CampaignMissionDocument document =
            Document(
                WithBand(
                    "ZONE1",
                    Zone(
                        100f,
                        400f,
                        500f,
                        1f,
                        0.2f,
                        -25f,
                        90f)));
        Assert.True(
            CampaignWeatherDiagnostics.TryCreate(
                document,
                out CampaignWeatherDiagnostics? before,
                out string beforeProblem),
            beforeProblem);
        Assert.Equal(1500f, before.DefaultAltitude);

        Assert.True(
            document.SetLeaf(
                0,
                new[] { 0, 1, 1 },
                2400f));
        Assert.True(
            CampaignWeatherDiagnostics.TryCreate(
                document,
                out CampaignWeatherDiagnostics? after,
                out string afterProblem),
            afterProblem);

        Assert.Equal(1700f, after.DefaultAltitude);
        Assert.Contains(
            "1000 to 2400 m",
            after.Summary("zone1", 1700f).CloudLabel);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => after.Summary(
                "zone1",
                float.NaN));
    }

    private static void AssertMarker(
        CampaignWeatherBandMarker marker,
        string name,
        float altitude,
        float position)
    {
        Assert.Equal(name, marker.Name);
        Assert.Equal(altitude, marker.Altitude);
        Assert.Equal(position, marker.Position);
    }

    private static CampaignMissionDocument Document(
        List<object?> inner) =>
        CampaignMissionDocument.FromScope(
            Mission,
            new ZrdrSet(
                "invented mission zrdr.zbd",
                new[]
                {
                    new ZrdrFile(
                        "weather.zrd",
                        null,
                        new List<object?> { inner }),
                }));

    private static List<object?> WithBand(
        params object?[] zones)
    {
        var inner =
            new List<object?>
            {
                "CLOUD_COVER",
                new List<object?>
                {
                    "TOP",
                    2000f,
                    "BOTTOM",
                    1000f,
                    "THICKNESS",
                    200f,
                    "TOP_COLOR",
                    new List<object?> { 1f, 0f, 0f },
                    "BOTTOM_COLOR",
                    new List<object?> { 0f, 0f, 1f },
                },
                "WIND",
                new List<object?>
                {
                    "STATIC_VELOCITY",
                    new List<object?> { 1f, 2f, 3f },
                    "RANDOM_MAX_SPEED",
                    4f,
                    "RANDOM_ACCEL",
                    5f,
                    "RANDOM_ANG_VEL",
                    6f,
                },
            };
        inner.AddRange(zones);
        inner.AddRange(
            new object?[]
            {
                "TYPE",
                "RAIN",
                "COLOR",
                new List<object?> { 0.2f, 0.4f, 0.6f },
                "WIND_DIR",
                45f,
                "WIND_VEL",
                8f,
                "GRAVITY",
                1.5f,
                "PARTICLES",
                100f,
                "ALPHA_GRADIENT",
                new List<object?> { 0.1f, 0.9f },
            });
        return inner;
    }

    private static List<object?> Zone(
        float fogNear,
        float fogFar,
        float clipFar,
        float diffuse,
        float ambient,
        float pitch,
        float yaw) =>
        new()
        {
            "FOG_COLOR",
            new List<object?> { 0.2f, 0.3f, 0.4f },
            "FOG_RANGES",
            new List<object?> { fogNear, fogFar },
            "FOG_ALTITUDE",
            new List<object?> { 0f, 5000f },
            "CLIP_RANGES",
            new List<object?> { 1f, clipFar },
            "SUNLIGHT_DIFFUSE",
            new List<object?> { diffuse },
            "SUNLIGHT_AMBIENT",
            new List<object?> { ambient },
            "SUNLIGHT_COLOR_DIFFUSE",
            new List<object?> { 1f, 1f, 1f },
            "SUNLIGHT_COLOR_AMBIENT",
            new List<object?> { 1f, 1f, 1f },
            "SUNLIGHT_BICOLORED",
            new List<object?> { 0f },
            "SUNLIGHT_ORIENTATION",
            new List<object?> { pitch, yaw, 0f },
        };
}

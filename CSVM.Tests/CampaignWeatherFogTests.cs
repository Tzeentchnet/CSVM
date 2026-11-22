using System;
using System.Collections.Generic;
using CSVM.Effects;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Typed campaign fog-zone editing over invented weather readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignWeatherFogTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void DynamicZonesEditExactLeavesAndPreserveSoftwareTwins()
    {
        List<object?> zone1 = Zone(
            0.2f,
            0.4f,
            1f,
            100f,
            200f,
            -50f,
            500f);
        List<object?> zone3 = Zone(
            32f,
            64f,
            128f,
            50f,
            250f,
            10f,
            1000f);
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "SW_ZONE1",
                Zone(9f, 9f, 9f, 1f, 2f, 3f, 4f),
                "ZONE1",
                zone1,
                "SW_ZONE3",
                Zone(8f, 8f, 8f, 5f, 6f, 7f, 8f),
                "ZONE3",
                zone3,
                "OTHER",
                new List<object?> { "root preserved" },
            });

        Assert.True(
            CampaignWeatherFogZones.TryBind(
                document,
                out CampaignWeatherFogZones? binding,
                out string problem),
            problem);
        Assert.Collection(
            binding.Zones,
            zone =>
            {
                Assert.Equal("ZONE1", zone.Name);
                Assert.False(zone.UsesByteColor);
            },
            zone =>
            {
                Assert.Equal("ZONE3", zone.Name);
                Assert.True(zone.UsesByteColor);
            });

        CampaignWeatherFogZone editedZone =
            binding.Zones[1];
        var edited = new CampaignWeatherFogZoneState(
            64f,
            96f,
            192f,
            75f,
            300f,
            20f,
            1200f);
        Assert.True(editedZone.Apply(edited));
        Assert.False(editedZone.Apply(edited));

        Assert.Equal(
            0.2f,
            document.ValueAt(1, new[] { 0, 3, 1, 0 }));
        Assert.Equal(
            8f,
            document.ValueAt(1, new[] { 0, 5, 1, 0 }));
        Assert.Equal(
            64f,
            document.ValueAt(1, new[] { 0, 7, 1, 0 }));
        Assert.Equal(
            "color tail",
            document.ValueAt(1, new[] { 0, 7, 1, 3 }));
        Assert.Equal(
            75f,
            document.ValueAt(1, new[] { 0, 7, 3, 0 }));
        Assert.Equal(
            1200f,
            document.ValueAt(1, new[] { 0, 7, 5, 1 }));
        Assert.Equal(
            "zone preserved",
            document.ValueAt(1, new[] { 0, 7, 7, 0 }));
        Assert.Equal(
            "root preserved",
            document.ValueAt(1, new[] { 0, 9, 0 }));

        WeatherState runtime = Assert.IsType<WeatherState>(
            WeatherState.Load(
                new ZrdrSet("edited mission", document.Files)));
        Assert.Equal(
            new[] { "zone1", "zone3" },
            runtime.ZoneNames);
        WeatherState.ZoneWeather runtimeZone =
            runtime.Zone("zone3");
        const float byteScale = 1f / 255f;
        Assert.Equal(
            new Color(
                64f * byteScale,
                96f * byteScale,
                192f * byteScale),
            runtimeZone.FogColor);
        Assert.Equal(75f, runtimeZone.FogNear);
        Assert.Equal(300f, runtimeZone.FogFar);
        Assert.Equal(20f, runtimeZone.FogLow);
        Assert.Equal(1200f, runtimeZone.FogHigh);
    }

    [Fact]
    public void LastDuplicateZoneAndFieldListsMatchRuntimeSelection()
    {
        List<object?> selected = Zone(
            0.1f,
            0.2f,
            0.3f,
            10f,
            20f,
            30f,
            40f);
        selected.Add("FOG_COLOR");
        selected.Add(
            new List<object?>
            {
                32f,
                56f,
                72f,
                "duplicate tail",
            });
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "ZONE1",
                Zone(1f, 0f, 0f, 1f, 2f, 3f, 4f),
                "zone1",
                selected,
            });

        Assert.True(
            CampaignWeatherFogZones.TryBind(
                document,
                out CampaignWeatherFogZones? binding,
                out string problem),
            problem);
        CampaignWeatherFogZone zone =
            Assert.Single(binding.Zones);
        Assert.Equal("ZONE1", zone.Name);
        Assert.True(zone.UsesByteColor);
        Assert.Equal(32f, zone.State.ColorR);

        CampaignWeatherFogZoneState edited =
            zone.State with { ColorR = 64f };
        Assert.True(zone.Apply(edited));
        Assert.Equal(
            1f,
            document.ValueAt(1, new[] { 0, 1, 1, 0 }));
        Assert.Equal(
            0.1f,
            document.ValueAt(1, new[] { 0, 3, 1, 0 }));
        Assert.Equal(
            64f,
            document.ValueAt(1, new[] { 0, 3, 9, 0 }));
        Assert.Equal(
            "duplicate tail",
            document.ValueAt(1, new[] { 0, 3, 9, 3 }));

        WeatherState runtime = Assert.IsType<WeatherState>(
            WeatherState.Load(
                new ZrdrSet("edited mission", document.Files)));
        const float byteScale = 1f / 255f;
        Assert.Equal(
            new Color(
                64f * byteScale,
                56f * byteScale,
                72f * byteScale),
            runtime.Zone("zone1").FogColor);
    }

    [Fact]
    public void MissingBareAndMalformedZonesStayUnbound()
    {
        AssertUnbound(
            CampaignMissionDocument.FromScope(
                Mission,
                Scope(new ZrdrFile(
                    "other.zrd",
                    null,
                    new List<object?>()))),
            "weather");
        AssertUnbound(
            Document(new List<object?>
            {
                "SW_ZONE1",
                Zone(1f, 1f, 1f, 1f, 2f, 3f, 4f),
            }),
            "no runtime ZONE");
        AssertUnbound(
            Document(new List<object?>
            {
                "ZONE1",
                Zone(1f, 1f, 1f, 1f, 2f, 3f, 4f),
                "ZONE1",
            }),
            "bare key");
        AssertUnbound(
            Document(new List<object?>
            {
                "ZONE1",
                new List<object?>
                {
                    "FOG_COLOR",
                    new List<object?> { 1f, 1f, 1f },
                    "FOG_RANGES",
                    new List<object?> { 1f, 2f },
                },
            }),
            "complete finite");
    }

    [Fact]
    public void InvalidValuesFailBeforeChangingAnyLeafOrEncoding()
    {
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "ZONE1",
                Zone(32f, 56f, 72f, 100f, 200f, 0f, 500f),
            });
        Assert.True(
            CampaignWeatherFogZones.TryBind(
                document,
                out CampaignWeatherFogZones? binding,
                out string problem),
            problem);
        CampaignWeatherFogZone zone =
            Assert.Single(binding.Zones);
        CampaignWeatherFogZoneState original =
            zone.State;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => zone.Apply(original with
            {
                ColorR = 0.5f,
                ColorG = 0.5f,
                ColorB = 0.5f,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => zone.Apply(original with
            {
                Near = 250f,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => zone.Apply(original with
            {
                Low = float.NaN,
            }));

        Assert.Equal(original, zone.State);
        Assert.False(document.IsDirty);
    }

    private static CampaignMissionDocument Document(
        List<object?> inner) =>
        CampaignMissionDocument.FromScope(
            Mission,
            Scope(
                new ZrdrFile(
                    "before.zrd",
                    null,
                    new List<object?> { "unchanged" }),
                new ZrdrFile(
                    "WEATHER.ZRD",
                    null,
                    new List<object?> { inner })));

    private static List<object?> Zone(
        float red,
        float green,
        float blue,
        float near,
        float far,
        float low,
        float high) =>
        new()
        {
            "FOG_COLOR",
            new List<object?> { red, green, blue, "color tail" },
            "FOG_RANGES",
            new List<object?> { near, far, "range tail" },
            "FOG_ALTITUDE",
            new List<object?> { low, high, "altitude tail" },
            "UNKNOWN",
            new List<object?> { "zone preserved" },
        };

    private static void AssertUnbound(
        CampaignMissionDocument document,
        string expectedProblem)
    {
        Assert.False(
            CampaignWeatherFogZones.TryBind(
                document,
                out CampaignWeatherFogZones? binding,
                out string problem));
        Assert.Null(binding);
        Assert.Contains(
            expectedProblem,
            problem,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ZrdrSet Scope(params ZrdrFile[] files) =>
        new("invented mission zrdr.zbd", files);
}

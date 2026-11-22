using System;
using System.Collections.Generic;
using CSVM.Effects;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Typed campaign zone-light editing over invented weather readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignWeatherLightingTests
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
    public void DynamicZonesEditExactLeavesAndMatchRuntime()
    {
        List<object?> zone1 =
            Zone(
                200f,
                0.8f,
                0.2f,
                0.3f,
                0.4f,
                0.5f,
                0.6f,
                0.7f,
                0f,
                0f,
                -25f,
                90f,
                0f);
        List<object?> zone3 =
            Zone(
                300f,
                0.9f,
                0.25f,
                32f,
                56f,
                72f,
                0.1f,
                0.2f,
                0.3f,
                1f,
                -25f,
                -135f,
                0f);
        CampaignMissionDocument document =
            Document(new List<object?>
            {
                "SW_ZONE1",
                Zone(
                    2f,
                    3f,
                    4f,
                    5f,
                    6f,
                    7f,
                    8f,
                    9f,
                    1f,
                    0f,
                    10f,
                    11f,
                    12f),
                "ZONE1",
                zone1,
                "SW_ZONE3",
                Zone(
                    13f,
                    14f,
                    15f,
                    16f,
                    17f,
                    18f,
                    19f,
                    20f,
                    0f,
                    0f,
                    21f,
                    22f,
                    23f),
                "ZONE3",
                zone3,
                "OTHER",
                new List<object?> { "root preserved" },
            });

        Assert.True(
            CampaignWeatherLightingZones.TryBind(
                document,
                out CampaignWeatherLightingZones? binding,
                out string problem),
            problem);
        Assert.Collection(
            binding.Zones,
            zone =>
            {
                Assert.Equal("ZONE1", zone.Name);
                Assert.False(
                    zone.DiffuseUsesByteColor);
                Assert.False(
                    zone.AmbientUsesByteColor);
            },
            zone =>
            {
                Assert.Equal("ZONE3", zone.Name);
                Assert.True(
                    zone.DiffuseUsesByteColor);
                Assert.False(
                    zone.AmbientUsesByteColor);
            });

        CampaignWeatherLightingZone editedZone =
            binding.Zones[1];
        var edited =
            new CampaignWeatherLightingState(
                450f,
                1.1f,
                0.3f,
                new CampaignWeatherColorState(
                    64f,
                    96f,
                    192f),
                new CampaignWeatherColorState(
                    0.25f,
                    0.5f,
                    0.75f),
                false,
                -35f,
                -120f,
                5f);
        Assert.True(editedZone.Apply(edited));
        Assert.False(editedZone.Apply(edited));

        Assert.Equal(
            0.8f,
            document.ValueAt(
                1,
                new[] { 0, 3, 9, 0 }));
        Assert.Equal(
            14f,
            document.ValueAt(
                1,
                new[] { 0, 5, 9, 0 }));
        Assert.Equal(
            450f,
            document.ValueAt(
                1,
                new[] { 0, 7, 7, 1 }));
        Assert.Equal(
            "clip tail",
            document.ValueAt(
                1,
                new[] { 0, 7, 7, 2 }));
        Assert.Equal(
            1.1f,
            document.ValueAt(
                1,
                new[] { 0, 7, 9, 0 }));
        Assert.Equal(
            64f,
            document.ValueAt(
                1,
                new[] { 0, 7, 13, 0 }));
        Assert.Equal(
            "color tail",
            document.ValueAt(
                1,
                new[] { 0, 7, 13, 3 }));
        Assert.Equal(
            0f,
            document.ValueAt(
                1,
                new[] { 0, 7, 17, 0 }));
        Assert.Equal(
            5f,
            document.ValueAt(
                1,
                new[] { 0, 7, 19, 2 }));
        Assert.Equal(
            "orientation tail",
            document.ValueAt(
                1,
                new[] { 0, 7, 19, 3 }));
        Assert.Equal(
            "zone preserved",
            document.ValueAt(
                1,
                new[] { 0, 7, 21, 0 }));
        Assert.Equal(
            "root preserved",
            document.ValueAt(
                1,
                new[] { 0, 9, 0 }));

        WeatherState runtime =
            Assert.IsType<WeatherState>(
                WeatherState.Load(
                    new ZrdrSet(
                        "edited mission",
                        document.Files)));
        Assert.Equal(
            new[] { "zone1", "zone3" },
            runtime.ZoneNames);
        WeatherState.ZoneWeather runtimeZone =
            runtime.Zone("zone3");
        const float byteScale = 1f / 255f;
        Assert.Equal(450f, runtimeZone.ClipFar);
        Assert.Equal(1.1f, runtimeZone.SunDiffuse);
        Assert.Equal(0.3f, runtimeZone.SunAmbient);
        Assert.Equal(0.806f, runtimeZone.WorldLight, 3);
        Assert.Equal(
            new Color(
                64f * byteScale,
                96f * byteScale,
                192f * byteScale),
            runtimeZone.SunColorDiffuse);
        Assert.Equal(
            new Color(0.25f, 0.5f, 0.75f),
            runtimeZone.SunColorAmbient);
        Assert.False(runtimeZone.SunBicolored);
        Assert.Equal(
            new Vector3(
                Mathf.DegToRad(-35f),
                Mathf.DegToRad(-120f),
                Mathf.DegToRad(5f)),
            runtimeZone.SunOrientation);
    }

    [Fact]
    public void LastZoneAndFieldListsMatchRuntimeSelection()
    {
        List<object?> selected =
            Zone(
                250f,
                0.7f,
                0.2f,
                1f,
                1f,
                1f,
                1f,
                1f,
                1f,
                0f,
                -65f,
                90f,
                0f);
        selected.Add("SUNLIGHT_DIFFUSE");
        selected.Add(
            new List<object?>
            {
                1.5f,
                "duplicate tail",
            });
        CampaignMissionDocument document =
            Document(new List<object?>
            {
                "ZONE1",
                Zone(
                    100f,
                    0.5f,
                    0.1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    0f,
                    -25f,
                    90f,
                    0f),
                "zone1",
                selected,
                "ZONE2",
                Zone(
                    400f,
                    0.6f,
                    0.15f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    0f,
                    -65f,
                    90f,
                    0f),
            });

        Assert.True(
            CampaignWeatherLightingZones.TryBind(
                document,
                out CampaignWeatherLightingZones? binding,
                out string problem),
            problem);
        Assert.Collection(
            binding.Zones,
            zone =>
            {
                Assert.Equal("ZONE1", zone.Name);
                Assert.Equal(
                    1.5f,
                    zone.State.SunDiffuse);
            },
            zone =>
                Assert.Equal("ZONE2", zone.Name));

        CampaignWeatherLightingZone zone1 =
            binding.Zones[0];
        CampaignWeatherLightingState edited =
            zone1.State with { SunDiffuse = 1.75f };
        Assert.True(zone1.Apply(edited));
        Assert.Equal(
            0.5f,
            document.ValueAt(
                1,
                new[] { 0, 1, 9, 0 }));
        Assert.Equal(
            0.7f,
            document.ValueAt(
                1,
                new[] { 0, 3, 9, 0 }));
        Assert.Equal(
            1.75f,
            document.ValueAt(
                1,
                new[] { 0, 3, 23, 0 }));
        Assert.Equal(
            "duplicate tail",
            document.ValueAt(
                1,
                new[] { 0, 3, 23, 1 }));

        WeatherState runtime =
            Assert.IsType<WeatherState>(
                WeatherState.Load(
                    new ZrdrSet(
                        "edited mission",
                        document.Files)));
        Assert.Equal(
            1.75f,
            runtime.Zone("zone1").SunDiffuse);
    }

    [Fact]
    public void OptionalFieldsStayIndependentAndAbsent()
    {
        List<object?> zone =
            BaseFog();
        zone.Add("SUNLIGHT_DIFFUSE");
        zone.Add(
            new List<object?>
            {
                1.2f,
                "diffuse tail",
            });
        zone.Add("SUNLIGHT_COLOR_AMBIENT");
        zone.Add(
            new List<object?>
            {
                0.1f,
                0.2f,
                0.3f,
                "color tail",
            });
        zone.Add("SUNLIGHT_ORIENTATION");
        zone.Add(
            new List<object?>
            {
                -25f,
                90f,
            });
        zone.Add("SUNLIGHT_BICOLORED");
        CampaignMissionDocument document =
            Document(new List<object?>
            {
                "ZONE1",
                zone,
            });

        Assert.True(
            CampaignWeatherLightingZones.TryBind(
                document,
                out CampaignWeatherLightingZones? binding,
                out string problem),
            problem);
        CampaignWeatherLightingZone lighting =
            Assert.Single(binding.Zones);
        CampaignWeatherLightingState original =
            lighting.State;
        Assert.Null(original.ClipFar);
        Assert.Equal(1.2f, original.SunDiffuse);
        Assert.Null(original.SunAmbient);
        Assert.Null(original.SunColorDiffuse);
        Assert.NotNull(original.SunColorAmbient);
        Assert.Null(original.SunBicolored);
        Assert.Equal(-25f, original.SunPitch);
        Assert.Equal(90f, original.SunYaw);
        Assert.Null(original.SunRoll);

        CampaignWeatherLightingState edited =
            original with
            {
                SunDiffuse = 1.4f,
                SunColorAmbient =
                    new CampaignWeatherColorState(
                        0.25f,
                        0.5f,
                        0.75f),
            };
        int count = zone.Count;
        Assert.True(lighting.Apply(edited));
        Assert.Equal(count, zone.Count);
        Assert.Equal(
            2,
            Assert.IsType<List<object?>>(zone[11]).Count);
        Assert.Equal(
            "SUNLIGHT_BICOLORED",
            document.ValueAt(
                1,
                new[] { 0, 1, 12 }));

        WeatherState runtime =
            Assert.IsType<WeatherState>(
                WeatherState.Load(
                    new ZrdrSet(
                        "edited mission",
                        document.Files)));
        WeatherState.ZoneWeather runtimeZone =
            runtime.Zone("zone1");
        Assert.Equal(1e9f, runtimeZone.ClipFar);
        Assert.Equal(1.4f, runtimeZone.SunDiffuse);
        Assert.Equal(
            WeatherState.DefaultAmbient,
            runtimeZone.SunAmbient);
        Assert.Equal(0.644f, runtimeZone.WorldLight, 3);
        Assert.Equal(
            Colors.White,
            runtimeZone.SunColorDiffuse);
        Assert.Equal(
            new Color(0.25f, 0.5f, 0.75f),
            runtimeZone.SunColorAmbient);
        Assert.False(runtimeZone.SunBicolored);
        Assert.Equal(
            new Vector3(
                Mathf.DegToRad(-25f),
                Mathf.DegToRad(90f),
                0f),
            runtimeZone.SunOrientation);
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
                Zone(
                    100f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    0f,
                    0f,
                    0f,
                    0f),
            }),
            "no runtime ZONE");
        AssertUnbound(
            Document(new List<object?>
            {
                "ZONE1",
                Zone(
                    100f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    1f,
                    0f,
                    0f,
                    0f,
                    0f),
                "ZONE1",
            }),
            "bare key");

        List<object?> malformed =
            Zone(
                100f,
                1f,
                1f,
                1f,
                1f,
                1f,
                1f,
                1f,
                1f,
                0f,
                0f,
                0f,
                0f);
        malformed.Add("SUNLIGHT_COLOR_DIFFUSE");
        malformed.Add(
            new List<object?> { 1f, 1f });
        AssertUnbound(
            Document(new List<object?>
            {
                "ZONE1",
                malformed,
            }),
            "SUNLIGHT_COLOR_DIFFUSE");
    }

    [Fact]
    public void InvalidValuesFailBeforeChangingAnyLeaf()
    {
        CampaignMissionDocument document =
            Document(new List<object?>
            {
                "ZONE1",
                Zone(
                    300f,
                    0.9f,
                    0.25f,
                    32f,
                    56f,
                    72f,
                    64f,
                    80f,
                    96f,
                    1f,
                    -25f,
                    90f,
                    0f),
            });
        Assert.True(
            CampaignWeatherLightingZones.TryBind(
                document,
                out CampaignWeatherLightingZones? binding,
                out string problem),
            problem);
        CampaignWeatherLightingZone zone =
            Assert.Single(binding.Zones);
        CampaignWeatherLightingState original =
            zone.State;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => zone.Apply(original with
            {
                ClipFar = -1f,
                SunDiffuse = 1.5f,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => zone.Apply(original with
            {
                SunColorDiffuse =
                    new CampaignWeatherColorState(
                        0.5f,
                        0.5f,
                        0.5f),
                SunAmbient = 0.5f,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => zone.Apply(original with
            {
                SunPitch = float.NaN,
                SunBicolored = false,
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
        float clipFar,
        float diffuse,
        float ambient,
        float diffuseRed,
        float diffuseGreen,
        float diffuseBlue,
        float ambientRed,
        float ambientGreen,
        float ambientBlue,
        float bicolored,
        float pitch,
        float yaw,
        float roll)
    {
        List<object?> zone =
            BaseFog();
        zone.Add("CLIP_RANGES");
        zone.Add(
            new List<object?>
            {
                1f,
                clipFar,
                "clip tail",
            });
        zone.Add("SUNLIGHT_DIFFUSE");
        zone.Add(
            new List<object?>
            {
                diffuse,
                "diffuse tail",
            });
        zone.Add("SUNLIGHT_AMBIENT");
        zone.Add(
            new List<object?>
            {
                ambient,
                "ambient tail",
            });
        zone.Add("SUNLIGHT_COLOR_DIFFUSE");
        zone.Add(
            new List<object?>
            {
                diffuseRed,
                diffuseGreen,
                diffuseBlue,
                "color tail",
            });
        zone.Add("SUNLIGHT_COLOR_AMBIENT");
        zone.Add(
            new List<object?>
            {
                ambientRed,
                ambientGreen,
                ambientBlue,
                "ambient color tail",
            });
        zone.Add("SUNLIGHT_BICOLORED");
        zone.Add(
            new List<object?>
            {
                bicolored,
                "flag tail",
            });
        zone.Add("SUNLIGHT_ORIENTATION");
        zone.Add(
            new List<object?>
            {
                pitch,
                yaw,
                roll,
                "orientation tail",
            });
        zone.Add("UNKNOWN");
        zone.Add(
            new List<object?> { "zone preserved" });
        return zone;
    }

    private static List<object?> BaseFog() =>
        new()
        {
            "FOG_COLOR",
            new List<object?>
            {
                0.2f,
                0.3f,
                0.4f,
            },
            "FOG_RANGES",
            new List<object?> { 100f, 200f },
            "FOG_ALTITUDE",
            new List<object?> { 0f, 500f },
        };

    private static void AssertUnbound(
        CampaignMissionDocument document,
        string expectedProblem)
    {
        Assert.False(
            CampaignWeatherLightingZones.TryBind(
                document,
                out CampaignWeatherLightingZones? binding,
                out string problem));
        Assert.Null(binding);
        Assert.Contains(
            expectedProblem,
            problem,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ZrdrSet Scope(
        params ZrdrFile[] files) =>
        new("invented mission zrdr.zbd", files);
}

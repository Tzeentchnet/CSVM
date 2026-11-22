using System;
using System.Collections.Generic;
using CSVM.Effects;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Typed campaign wind editing over invented weather readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignWeatherWindTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void LastWindBlockBindsFirstValidFieldsAndEditsTheirExactLeaves()
    {
        List<object?> first = Wind(
            1f,
            2f,
            3f,
            4f,
            5f,
            6f);
        var second = new List<object?>
        {
            "STATIC_VELOCITY",
            new List<object?> { 1f, 2f },
            "STATIC_VELOCITY",
            new List<object?> { 10f, 20f, 30f, "vector tail" },
            "STATIC_VELOCITY",
            new List<object?> { 100f, 200f, 300f },
            "RANDOM_MAX_SPEED",
            "not a float",
            "RANDOM_MAX_SPEED",
            40f,
            "RANDOM_MAX_SPEED",
            400f,
            "RANDOM_ACCEL",
            0.5f,
            "RANDOM_ANG_VEL",
            45f,
            "UNKNOWN",
            new List<object?> { "preserved" },
        };
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "WIND",
                first,
                "OTHER",
                new List<object?> { "unchanged" },
                "WIND",
                second,
            });

        Assert.True(
            CampaignWeatherWind.TryBind(
                document,
                out CampaignWeatherWind? binding,
                out string problem),
            problem);
        Assert.Equal(
            new CampaignWeatherWindState(
                10f,
                20f,
                30f,
                40f,
                0.5f,
                45f),
            binding.State);

        var edited = new CampaignWeatherWindState(
            -10f,
            -20f,
            -30f,
            12f,
            0.75f,
            30f);
        Assert.True(binding.Apply(edited));
        Assert.False(binding.Apply(edited));

        Assert.Equal(
            1f,
            document.ValueAt(1, new[] { 0, 1, 1, 0 }));
        Assert.Equal(
            -10f,
            document.ValueAt(1, new[] { 0, 5, 3, 0 }));
        Assert.Equal(
            "vector tail",
            document.ValueAt(1, new[] { 0, 5, 3, 3 }));
        Assert.Equal(
            100f,
            document.ValueAt(1, new[] { 0, 5, 5, 0 }));
        Assert.Equal(
            12f,
            document.ValueAt(1, new[] { 0, 5, 9 }));
        Assert.Equal(
            400f,
            document.ValueAt(1, new[] { 0, 5, 11 }));
        Assert.Equal(
            "preserved",
            document.ValueAt(1, new[] { 0, 5, 17, 0 }));

        WeatherState runtime = Assert.IsType<WeatherState>(
            WeatherState.Load(
                new ZrdrSet("edited mission", document.Files)));
        Assert.Equal(
            new Vector3(-10f, -20f, -30f),
            runtime.WindStatic);
        Assert.Equal(12f, runtime.WindRandomMaxSpeed);
        Assert.Equal(0.75f, runtime.WindRandomAccel);
        Assert.Equal(30f, runtime.WindRandomAngVel);
    }

    [Fact]
    public void MissingMalformedAndBareWindBlocksStayUnbound()
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
            Document(new List<object?> { "OTHER", 1f }),
            "WIND");
        AssertUnbound(
            Document(new List<object?>
            {
                "WIND",
                Wind(1f, 2f, 3f, 4f, 5f, 6f),
                "WIND",
            }),
            "bare key");
        AssertUnbound(
            Document(new List<object?>
            {
                "WIND",
                new List<object?>
                {
                    "STATIC_VELOCITY",
                    new List<object?> { 1f, 2f, 3f },
                    "RANDOM_MAX_SPEED",
                    4f,
                    "RANDOM_ACCEL",
                    5f,
                },
            }),
            "complete finite");
    }

    [Fact]
    public void InvalidTypedValuesFailBeforeChangingAnyLeaf()
    {
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "WIND",
                Wind(1f, 2f, 3f, 4f, 5f, 6f),
            });
        Assert.True(
            CampaignWeatherWind.TryBind(
                document,
                out CampaignWeatherWind? binding,
                out string problem),
            problem);
        CampaignWeatherWindState original = binding.State;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                StaticX = 99f,
                RandomAccel = -1f,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                StaticZ = float.NaN,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                RandomAngVelDeg = float.PositiveInfinity,
            }));

        Assert.Equal(original, binding.State);
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

    private static List<object?> Wind(
        float x,
        float y,
        float z,
        float maxSpeed,
        float accel,
        float angVel) =>
        new()
        {
            "STATIC_VELOCITY",
            new List<object?> { x, y, z },
            "RANDOM_MAX_SPEED",
            maxSpeed,
            "RANDOM_ACCEL",
            accel,
            "RANDOM_ANG_VEL",
            angVel,
        };

    private static void AssertUnbound(
        CampaignMissionDocument document,
        string expectedProblem)
    {
        Assert.False(
            CampaignWeatherWind.TryBind(
                document,
                out CampaignWeatherWind? binding,
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

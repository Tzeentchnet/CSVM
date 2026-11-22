using System;
using System.Collections.Generic;
using CSVM.Effects;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Typed campaign precipitation editing over invented weather readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignWeatherPrecipitationTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void FirstRuntimeValuesEditExactLeavesAndPreserveDuplicatesAndTails()
    {
        var color =
            new List<object?>
            {
                128f,
                96f,
                64f,
                "color tail",
            };
        var alpha =
            new List<object?>
            {
                0.5f,
                0f,
                "alpha tail",
            };
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "TYPE",
                7f,
                "TYPE",
                "rain",
                "TYPE",
                "LATER",
                "COLOR",
                color,
                "COLOR",
                new List<object?> { 1f, 1f, 1f },
                "WIND_DIR",
                "not a float",
                "WIND_DIR",
                15f,
                "WIND_DIR",
                90f,
                "WIND_VEL",
                0.8f,
                "WIND_VEL",
                8f,
                "GRAVITY",
                3f,
                "GRAVITY",
                30f,
                "PARTICLES",
                "not a float",
                "PARTICLES",
                100f,
                "PARTICLES",
                200f,
                "ALPHA_GRADIENT",
                new List<object?> { 0.25f },
                "ALPHA_GRADIENT",
                alpha,
                "ALPHA_GRADIENT",
                new List<object?> { 1f, 1f },
                "UNKNOWN",
                new List<object?> { "preserved" },
            });

        Assert.True(
            CampaignWeatherPrecipitation.TryBind(
                document,
                out CampaignWeatherPrecipitation? binding,
                out string problem),
            problem);
        Assert.True(binding.UsesByteColor);
        Assert.Equal(
            new CampaignWeatherPrecipitationState(
                CampaignWeatherPrecipitationKind.Rain,
                new CampaignWeatherColorState(
                    128f,
                    96f,
                    64f),
                15f,
                0.8f,
                3f,
                100,
                0.5f,
                0f),
            binding.State);

        var edited = new CampaignWeatherPrecipitationState(
            CampaignWeatherPrecipitationKind.Snow,
            new CampaignWeatherColorState(
                160f,
                128f,
                96f),
            30f,
            1.25f,
            2f,
            125,
            0.75f,
            0.1f);
        Assert.True(binding.Apply(edited));
        Assert.False(binding.Apply(edited));

        Assert.Equal(
            7f,
            document.ValueAt(1, new[] { 0, 1 }));
        Assert.Equal(
            "SNOW",
            document.ValueAt(1, new[] { 0, 3 }));
        Assert.Equal(
            "LATER",
            document.ValueAt(1, new[] { 0, 5 }));
        Assert.Equal(
            160f,
            document.ValueAt(1, new[] { 0, 7, 0 }));
        Assert.Equal(
            "color tail",
            document.ValueAt(1, new[] { 0, 7, 3 }));
        Assert.Equal(
            1f,
            document.ValueAt(1, new[] { 0, 9, 0 }));
        Assert.Equal(
            30f,
            document.ValueAt(1, new[] { 0, 13 }));
        Assert.Equal(
            90f,
            document.ValueAt(1, new[] { 0, 15 }));
        Assert.Equal(
            1.25f,
            document.ValueAt(1, new[] { 0, 17 }));
        Assert.Equal(
            8f,
            document.ValueAt(1, new[] { 0, 19 }));
        Assert.Equal(
            2f,
            document.ValueAt(1, new[] { 0, 21 }));
        Assert.Equal(
            30f,
            document.ValueAt(1, new[] { 0, 23 }));
        Assert.Equal(
            125f,
            document.ValueAt(1, new[] { 0, 27 }));
        Assert.Equal(
            200f,
            document.ValueAt(1, new[] { 0, 29 }));
        Assert.Equal(
            0.25f,
            document.ValueAt(1, new[] { 0, 31, 0 }));
        Assert.Equal(
            0.75f,
            document.ValueAt(1, new[] { 0, 33, 0 }));
        Assert.Equal(
            0.1f,
            document.ValueAt(1, new[] { 0, 33, 1 }));
        Assert.Equal(
            "alpha tail",
            document.ValueAt(1, new[] { 0, 33, 2 }));
        Assert.Equal(
            1f,
            document.ValueAt(1, new[] { 0, 35, 0 }));
        Assert.Equal(
            "preserved",
            document.ValueAt(1, new[] { 0, 37, 0 }));
        Assert.Equal(
            "unchanged",
            document.ValueAt(0, new[] { 0 }));

        WeatherState.PrecipData runtime =
            RuntimePrecip(document);
        Assert.Equal(
            WeatherState.PrecipKind.Snow,
            runtime.Kind);
        const float byteScale = 1f / 255f;
        Assert.Equal(
            new Color(
                160f * byteScale,
                128f * byteScale,
                96f * byteScale),
            runtime.Color);
        Assert.Equal(30f, runtime.WindDir);
        Assert.Equal(1.25f, runtime.WindVel);
        Assert.Equal(2f, runtime.Gravity);
        Assert.Equal(125, runtime.Particles);
        Assert.Equal(
            new Vector2(0.75f, 0.1f),
            runtime.AlphaGradient);
    }

    [Fact]
    public void AbsentUnknownAndMalformedPrecipitationStayUnbound()
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
            "TYPE");

        List<object?> unknownType =
            Precipitation(
                "MIST",
                particles: true);
        unknownType.Add("TYPE");
        unknownType.Add("RAIN");
        CampaignMissionDocument unknown =
            Document(unknownType);
        AssertUnbound(
            unknown,
            "RAIN or SNOW");
        Assert.Null(
            WeatherState.Load(
                new ZrdrSet(
                    "unknown type",
                    unknown.Files))!.Precip);

        List<object?> missingGravity =
            Precipitation(
                "RAIN",
                particles: true);
        int gravity =
            missingGravity.FindIndex(
                value => Equals(value, "GRAVITY"));
        missingGravity.RemoveRange(gravity, 2);
        AssertUnbound(
            Document(missingGravity),
            "complete runtime field shape");

        var malformedColor =
            new List<object?>
            {
                "TYPE",
                "RAIN",
                "COLOR",
                new List<object?> { 128f, "bad", 128f },
                "COLOR",
                new List<object?> { 64f, 64f, 64f },
                "WIND_DIR",
                0f,
                "WIND_VEL",
                0.8f,
                "GRAVITY",
                3f,
                "PARTICLES",
                100f,
                "ALPHA_GRADIENT",
                new List<object?> { 0.5f, 0f },
            };
        CampaignMissionDocument malformed =
            Document(malformedColor);
        AssertUnbound(
            malformed,
            "finite color triple");
        Assert.Equal(
            new Color(0.5f, 0.5f, 0.5f),
            RuntimePrecip(malformed).Color);

        List<object?> nonFinite =
            Precipitation(
                "RAIN",
                particles: true);
        int windVelocity =
            nonFinite.FindIndex(
                value => Equals(value, "WIND_VEL"));
        nonFinite[windVelocity + 1] = float.NaN;
        nonFinite.Insert(windVelocity + 2, "WIND_VEL");
        nonFinite.Insert(windVelocity + 3, 0.8f);
        CampaignMissionDocument nonFiniteDocument =
            Document(nonFinite);
        AssertUnbound(
            nonFiniteDocument,
            "must be finite");
        Assert.True(
            float.IsNaN(
                RuntimePrecip(
                    nonFiniteDocument).WindVel));
    }

    [Fact]
    public void OptionalParticlesCannotBeCreatedOrRemoved()
    {
        CampaignMissionDocument snow =
            Document(
                Precipitation(
                    "SNOW",
                    particles: false));
        Assert.True(
            CampaignWeatherPrecipitation.TryBind(
                snow,
                out CampaignWeatherPrecipitation? snowBinding,
                out string snowProblem),
            snowProblem);
        CampaignWeatherPrecipitationState snowState =
            snowBinding.State;
        Assert.Null(snowState.Particles);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => snowBinding.Apply(
                snowState with
                {
                    WindVel = 9f,
                    Particles = 100,
                }));
        Assert.Equal(snowState, snowBinding.State);
        Assert.False(snow.IsDirty);

        Assert.True(
            snowBinding.Apply(
                snowState with { WindVel = 1.2f }));
        Assert.DoesNotContain(
            "PARTICLES",
            (List<object?>)snow.Files[1].Root[0]!);
        Assert.Equal(
            0,
            RuntimePrecip(snow).Particles);

        CampaignMissionDocument rain =
            Document(
                Precipitation(
                    "RAIN",
                    particles: true));
        Assert.True(
            CampaignWeatherPrecipitation.TryBind(
                rain,
                out CampaignWeatherPrecipitation? rainBinding,
                out string rainProblem),
            rainProblem);
        CampaignWeatherPrecipitationState rainState =
            rainBinding.State;
        Assert.Equal(100, rainState.Particles);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => rainBinding.Apply(
                rainState with
                {
                    Gravity = 9f,
                    Particles = null,
                }));
        Assert.Equal(rainState, rainBinding.State);
        Assert.False(rain.IsDirty);
    }

    [Fact]
    public void ColorEncodingAndAllValuesValidateBeforeMutation()
    {
        CampaignMissionDocument bytes =
            Document(
                Precipitation(
                    "RAIN",
                    particles: true));
        Assert.True(
            CampaignWeatherPrecipitation.TryBind(
                bytes,
                out CampaignWeatherPrecipitation? byteBinding,
                out string byteProblem),
            byteProblem);
        Assert.True(byteBinding.UsesByteColor);
        CampaignWeatherPrecipitationState byteState =
            byteBinding.State;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => byteBinding.Apply(
                byteState with
                {
                    Color =
                        new CampaignWeatherColorState(
                            0.5f,
                            0.5f,
                            0.5f),
                    WindDirDeg = 45f,
                }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => byteBinding.Apply(
                byteState with
                {
                    Gravity = 4f,
                    AlphaGradientY = float.PositiveInfinity,
                }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => byteBinding.Apply(
                byteState with
                {
                    Kind =
                        (CampaignWeatherPrecipitationKind)99,
                    WindVel = 2f,
                }));
        Assert.Equal(byteState, byteBinding.State);
        Assert.False(bytes.IsDirty);

        List<object?> normalizedValues =
            Precipitation(
                "SNOW",
                particles: false);
        int colorIndex =
            normalizedValues.FindIndex(
                value => Equals(value, "COLOR"));
        normalizedValues[colorIndex + 1] =
            new List<object?>
            {
                0.2f,
                0.4f,
                1f,
                "normalized tail",
            };
        CampaignMissionDocument normalized =
            Document(normalizedValues);
        Assert.True(
            CampaignWeatherPrecipitation.TryBind(
                normalized,
                out CampaignWeatherPrecipitation? normalizedBinding,
                out string normalizedProblem),
            normalizedProblem);
        Assert.False(normalizedBinding.UsesByteColor);
        CampaignWeatherPrecipitationState normalizedState =
            normalizedBinding.State with
            {
                Color =
                    new CampaignWeatherColorState(
                        0.3f,
                        0.5f,
                        0.7f),
            };
        Assert.True(
            normalizedBinding.Apply(
                normalizedState));
        Assert.Equal(
            "normalized tail",
            normalized.ValueAt(
                1,
                new[] { 0, colorIndex + 1, 3 }));
        Assert.Equal(
            new Color(0.3f, 0.5f, 0.7f),
            RuntimePrecip(normalized).Color);
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

    private static List<object?> Precipitation(
        string kind,
        bool particles)
    {
        var values = new List<object?>
        {
            "TYPE",
            kind,
            "COLOR",
            new List<object?> { 128f, 128f, 128f },
            "WIND_DIR",
            0f,
            "WIND_VEL",
            0.8f,
            "GRAVITY",
            kind.Equals(
                "RAIN",
                StringComparison.OrdinalIgnoreCase)
                    ? 3f
                    : 1f,
        };
        if (particles)
        {
            values.Add("PARTICLES");
            values.Add(100f);
        }

        values.Add("ALPHA_GRADIENT");
        values.Add(
            new List<object?> { 0.5f, 0f });
        return values;
    }

    private static WeatherState.PrecipData RuntimePrecip(
        CampaignMissionDocument document) =>
        Assert.IsType<WeatherState.PrecipData>(
            WeatherState.Load(
                new ZrdrSet(
                    "edited mission",
                    document.Files))!.Precip);

    private static void AssertUnbound(
        CampaignMissionDocument document,
        string expectedProblem)
    {
        Assert.False(
            CampaignWeatherPrecipitation.TryBind(
                document,
                out CampaignWeatherPrecipitation? binding,
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

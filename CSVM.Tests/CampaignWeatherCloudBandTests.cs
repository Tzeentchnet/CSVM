using System;
using System.Collections.Generic;
using CSVM.Effects;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Typed campaign cloud-band editing over invented weather readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignWeatherCloudBandTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void LastCloudBlockBindsFirstValidScalarsAndEditsExactLeaves()
    {
        List<object?> first = Cloud(
            1000f,
            800f,
            50f);
        var second = new List<object?>
        {
            "TOP",
            "not a float",
            "TOP",
            1200f,
            "TOP",
            9999f,
            "BOTTOM",
            800f,
            "THICKNESS",
            100f,
            "TOP_COLOR",
            new List<object?> { 192f, 192f, 192f },
            "UNKNOWN",
            new List<object?> { "preserved" },
        };
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "CLOUD_COVER",
                first,
                "OTHER",
                new List<object?> { "unchanged" },
                "CLOUD_COVER",
                second,
            });

        Assert.True(
            CampaignWeatherCloudBand.TryBind(
                document,
                out CampaignWeatherCloudBand? binding,
                out string problem),
            problem);
        Assert.Equal(
            new CampaignWeatherCloudBandState(
                1200f,
                800f,
                100f,
                new CampaignWeatherColorState(
                    192f,
                    192f,
                    192f)),
            binding.State);

        CampaignWeatherCloudBandState edited =
            binding.State with
            {
                Top = 1400f,
                Bottom = 900f,
                Thickness = 200f,
            };
        Assert.True(binding.Apply(edited));
        Assert.False(binding.Apply(edited));

        Assert.Equal(
            1000f,
            document.ValueAt(1, new[] { 0, 1, 1 }));
        Assert.Equal(
            1400f,
            document.ValueAt(1, new[] { 0, 5, 3 }));
        Assert.Equal(
            9999f,
            document.ValueAt(1, new[] { 0, 5, 5 }));
        Assert.Equal(
            900f,
            document.ValueAt(1, new[] { 0, 5, 7 }));
        Assert.Equal(
            200f,
            document.ValueAt(1, new[] { 0, 5, 9 }));
        Assert.Equal(
            192f,
            document.ValueAt(1, new[] { 0, 5, 11, 0 }));
        Assert.Equal(
            "preserved",
            document.ValueAt(1, new[] { 0, 5, 13, 0 }));

        WeatherState runtime = Assert.IsType<WeatherState>(
            WeatherState.Load(
                new ZrdrSet("edited mission", document.Files)));
        Assert.Equal(1400f, runtime.CloudTop);
        Assert.Equal(900f, runtime.CloudBottom);
        Assert.Equal(200f, runtime.CloudThickness);
        Assert.Equal(1150f, runtime.CloudBandCentre);
        Assert.Equal(1050f, runtime.CloudCoreBottom);
        Assert.Equal(0f, runtime.WhiteoutAmount(900f));
        Assert.Equal(0.5f, runtime.WhiteoutAmount(975f));
        Assert.Equal(1f, runtime.WhiteoutAmount(1050f));
        Assert.Equal(1f, runtime.WhiteoutAmount(1150f));
        Assert.Equal(0.5f, runtime.WhiteoutAmount(1325f));
        Assert.Equal(0f, runtime.WhiteoutAmount(1400f));
    }

    [Fact]
    public void ExistingColorsKeepEncodingAndEditRuntimeSelectedLists()
    {
        var cloud = new List<object?>
        {
            "TOP",
            1200f,
            "BOTTOM",
            800f,
            "THICKNESS",
            100f,
            "TOP_COLOR",
            new List<object?> { 220f, 200f, 180f, "top tail" },
            "TOP_COLOR",
            new List<object?> { 1f, 2f, 3f },
            "BOTTOM_COLOR",
            "not a list",
            "BOTTOM_COLOR",
            new List<object?> { 0.2f, 0.4f, 0.6f, "bottom tail" },
            "BOTTOM_COLOR",
            new List<object?> { 4f, 5f, 6f },
            "UNKNOWN",
            new List<object?> { "preserved" },
        };
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "CLOUD_COVER",
                cloud,
            });

        Assert.True(
            CampaignWeatherCloudBand.TryBind(
                document,
                out CampaignWeatherCloudBand? binding,
                out string problem),
            problem);
        Assert.True(binding.TopColorUsesByteEncoding);
        Assert.False(binding.BottomColorUsesByteEncoding);
        Assert.Equal(
            new CampaignWeatherColorState(
                220f,
                200f,
                180f),
            binding.State.TopColor);
        Assert.Equal(
            new CampaignWeatherColorState(
                0.2f,
                0.4f,
                0.6f),
            binding.State.BottomColor);

        CampaignWeatherCloudBandState edited =
            binding.State with
            {
                TopColor = new CampaignWeatherColorState(
                    200f,
                    180f,
                    160f),
                BottomColor = new CampaignWeatherColorState(
                    0.3f,
                    0.5f,
                    0.7f),
            };
        Assert.True(binding.Apply(edited));
        Assert.False(binding.Apply(edited));

        Assert.Equal(
            200f,
            document.ValueAt(1, new[] { 0, 1, 7, 0 }));
        Assert.Equal(
            "top tail",
            document.ValueAt(1, new[] { 0, 1, 7, 3 }));
        Assert.Equal(
            1f,
            document.ValueAt(1, new[] { 0, 1, 9, 0 }));
        Assert.Equal(
            "not a list",
            document.ValueAt(1, new[] { 0, 1, 11 }));
        Assert.Equal(
            0.3f,
            document.ValueAt(1, new[] { 0, 1, 13, 0 }));
        Assert.Equal(
            "bottom tail",
            document.ValueAt(1, new[] { 0, 1, 13, 3 }));
        Assert.Equal(
            4f,
            document.ValueAt(1, new[] { 0, 1, 15, 0 }));
        Assert.Equal(
            "preserved",
            document.ValueAt(1, new[] { 0, 1, 17, 0 }));

        WeatherState runtime = Assert.IsType<WeatherState>(
            WeatherState.Load(
                new ZrdrSet("edited mission", document.Files)));
        const float byteScale = 1f / 255f;
        var expectedTop = new Color(
            200f * byteScale,
            180f * byteScale,
            160f * byteScale);
        var expectedBottom = new Color(
            0.3f,
            0.5f,
            0.7f);
        Assert.Equal(
            expectedTop,
            runtime.CloudTopColor);
        Assert.Equal(
            expectedBottom,
            runtime.CloudBottomColor);
        Assert.Equal(
            expectedBottom,
            runtime.WhiteoutColor(800f));
        Assert.Equal(
            expectedBottom.Lerp(
                expectedTop,
                0.5f),
            runtime.WhiteoutColor(1000f));
        Assert.Equal(
            expectedTop,
            runtime.WhiteoutColor(1200f));
    }

    [Fact]
    public void MissingBareAndMalformedCloudBlocksStayUnbound()
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
            "CLOUD_COVER");
        AssertUnbound(
            Document(new List<object?>
            {
                "CLOUD_COVER",
                Cloud(1000f, 800f, 50f),
                "CLOUD_COVER",
            }),
            "bare key");
        AssertUnbound(
            Document(new List<object?>
            {
                "CLOUD_COVER",
                new List<object?>
                {
                    "TOP",
                    1000f,
                    "BOTTOM",
                    800f,
                },
            }),
            "complete finite");
        AssertUnbound(
            Document(new List<object?>
            {
                "CLOUD_COVER",
                Cloud(800f, 1000f, 50f),
            }),
            "TOP must be above");
        AssertUnbound(
            Document(new List<object?>
            {
                "CLOUD_COVER",
                new List<object?>
                {
                    "TOP",
                    1000f,
                    "BOTTOM",
                    800f,
                    "THICKNESS",
                    50f,
                    "TOP_COLOR",
                    new List<object?> { 192f, 192f },
                    "TOP_COLOR",
                    new List<object?> { 32f, 56f, 72f },
                },
            }),
            "TOP_COLOR");
        AssertUnbound(
            Document(new List<object?>
            {
                "CLOUD_COVER",
                new List<object?>
                {
                    "TOP",
                    1000f,
                    "BOTTOM",
                    800f,
                    "THICKNESS",
                    50f,
                    "BOTTOM_COLOR",
                    new List<object?> { 0.2f, float.NaN, 0.6f },
                },
            }),
            "BOTTOM_COLOR");
    }

    [Fact]
    public void InvalidTypedValuesFailBeforeChangingAnyLeaf()
    {
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "CLOUD_COVER",
                Cloud(1000f, 800f, 50f),
            });
        Assert.True(
            CampaignWeatherCloudBand.TryBind(
                document,
                out CampaignWeatherCloudBand? binding,
                out string problem),
            problem);
        CampaignWeatherCloudBandState original =
            binding.State;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                Bottom = 1100f,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                Top = 1200f,
                Thickness = 500f,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                Thickness = float.NaN,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                TopColor = new CampaignWeatherColorState(
                    32f,
                    56f,
                    72f),
            }));

        Assert.Equal(original, binding.State);
        Assert.False(document.IsDirty);

        Assert.True(binding.Apply(original with
        {
            Thickness = 0f,
        }));
        Assert.Equal(0f, binding.State.Thickness);
        Assert.True(binding.Apply(original with
        {
            Thickness = original.Top - original.Bottom,
        }));
        Assert.Equal(
            original.Top - original.Bottom,
            binding.State.Thickness);
    }

    [Fact]
    public void InvalidColorValuesFailBeforeChangingAnyLeafOrPresence()
    {
        var cloud = new List<object?>
        {
            "TOP",
            1000f,
            "BOTTOM",
            800f,
            "THICKNESS",
            50f,
            "TOP_COLOR",
            new List<object?> { 32f, 56f, 72f },
            "BOTTOM_COLOR",
            new List<object?> { 0.2f, 0.4f, 0.6f },
        };
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "CLOUD_COVER",
                cloud,
            });
        Assert.True(
            CampaignWeatherCloudBand.TryBind(
                document,
                out CampaignWeatherCloudBand? binding,
                out string problem),
            problem);
        CampaignWeatherCloudBandState original =
            binding.State;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                Top = 1100f,
                TopColor = new CampaignWeatherColorState(
                    0.5f,
                    0.5f,
                    0.5f),
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                BottomColor = new CampaignWeatherColorState(
                    0.2f,
                    1.1f,
                    0.6f),
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                TopColor = null,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                BottomColor = new CampaignWeatherColorState(
                    0.2f,
                    float.PositiveInfinity,
                    0.6f),
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

    private static List<object?> Cloud(
        float top,
        float bottom,
        float thickness) =>
        new()
        {
            "TOP",
            top,
            "BOTTOM",
            bottom,
            "THICKNESS",
            thickness,
        };

    private static void AssertUnbound(
        CampaignMissionDocument document,
        string expectedProblem)
    {
        Assert.False(
            CampaignWeatherCloudBand.TryBind(
                document,
                out CampaignWeatherCloudBand? binding,
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

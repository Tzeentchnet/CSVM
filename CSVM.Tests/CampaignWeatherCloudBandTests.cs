using System;
using System.Collections.Generic;
using CSVM.Effects;
using CSVM.Mech3;
using CSVM.Tooling;
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
                100f),
            binding.State);

        var edited = new CampaignWeatherCloudBandState(
            1400f,
            900f,
            200f);
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

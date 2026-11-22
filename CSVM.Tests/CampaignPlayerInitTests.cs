using System;
using System.Collections.Generic;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Typed campaign PLAYER_INIT editing over invented raw reader trees.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignPlayerInitTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void LastDuplicateBindsAndEditsOnlyItsExactLeaves()
    {
        List<object?> first = PlayerInit(
            1f,
            2f,
            3f,
            4f,
            5f,
            6f,
            0.5f,
            100f);
        List<object?> second = PlayerInit(
            10f,
            20f,
            30f,
            40f,
            50f,
            60f,
            0.8f,
            180f);
        second.Add("unknown tail");
        var inner = new List<object?>
        {
            "PLAYER_INIT",
            first,
            "OTHER",
            new List<object?> { "unchanged" },
            "PLAYER_INIT",
            second,
        };
        CampaignMissionDocument document = Document(inner);

        Assert.True(
            CampaignPlayerInit.TryBind(
                document,
                out CampaignPlayerInit? binding,
                out string problem),
            problem);
        Assert.Equal(1f, binding.Pending);
        Assert.Equal(
            new CampaignPlayerInitState(
                10f,
                20f,
                30f,
                40f,
                50f,
                60f,
                0.8f,
                18f),
            binding.State);

        var edited = new CampaignPlayerInitState(
            -10f,
            -20f,
            -30f,
            -40f,
            -50f,
            -60f,
            0.75f,
            21f);
        Assert.True(binding.Apply(edited));
        Assert.False(binding.Apply(edited));

        Assert.Equal(
            1f,
            document.ValueAt(1, new[] { 0, 1, 1, 0 }));
        Assert.Equal(
            -10f,
            document.ValueAt(1, new[] { 0, 5, 1, 0 }));
        Assert.Equal(
            -40f,
            document.ValueAt(1, new[] { 0, 5, 2, 0 }));
        Assert.Equal(
            -60f,
            document.ValueAt(1, new[] { 0, 5, 2, 2 }));
        Assert.Equal(
            210f,
            document.ValueAt(1, new[] { 0, 5, 4 }));
        Assert.Equal(
            "unknown tail",
            document.ValueAt(1, new[] { 0, 5, 5 }));

        PlayerStart? runtime = SpawnPoints.LoadPlayerInit(
            new ZrdrSet("edited mission", document.Files));
        Assert.True(runtime.HasValue);
        Assert.Equal(
            new Vector3(-10f, -20f, -30f),
            runtime.Value.Spawn.Position);
        Assert.Equal(-50f, runtime.Value.Spawn.HeadingDeg);
        Assert.Equal(0.75f, runtime.Value.ThrottleFrac);
        Assert.Equal(21f, runtime.Value.SpeedMps);
    }

    [Fact]
    public void MissingAndIncompleteRecordsStayUnboundWithDiagnostics()
    {
        AssertUnbound(
            CampaignMissionDocument.FromScope(
                Mission,
                Scope(new ZrdrFile(
                    "other.zrd",
                    null,
                    new List<object?>()))),
            "objectives");
        AssertUnbound(
            Document(new List<object?>
            {
                "PLAYER_INIT",
                new List<object?>
                {
                    1f,
                    new List<object?> { 1f, 2f, 3f },
                    new List<object?> { 4f, 5f },
                    0.8f,
                    180f,
                },
            }),
            "complete five-element");
        AssertUnbound(
            Document(new List<object?>
            {
                "PLAYER_INIT",
                PlayerInit(1f, 2f, 3f, 4f, 5f, 6f, 0.8f, 180f),
                "PLAYER_INIT",
            }),
            "bare key");
    }

    [Fact]
    public void InvalidTypedValuesFailBeforeChangingAnyLeaf()
    {
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "PLAYER_INIT",
                PlayerInit(1f, 2f, 3f, 4f, 5f, 6f, 0.8f, 180f),
            });
        Assert.True(
            CampaignPlayerInit.TryBind(
                document,
                out CampaignPlayerInit? binding,
                out string problem),
            problem);
        CampaignPlayerInitState original = binding.State;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                X = 99f,
                ThrottleFrac = 2f,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                YawDeg = float.NaN,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => binding.Apply(original with
            {
                SpeedMps = float.MaxValue,
            }));

        Assert.Equal(original, binding.State);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void NoOpApplyPreservesAuthoredSpeedBitsLostByDisplayConversion()
    {
        float authoredSpeed = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                "PLAYER_INIT",
                PlayerInit(1f, 2f, 3f, 4f, 5f, 6f, 0.8f, authoredSpeed),
            });
        Assert.True(
            CampaignPlayerInit.TryBind(
                document,
                out CampaignPlayerInit? binding,
                out string problem),
            problem);
        Assert.Equal(0f, binding.State.SpeedMps);

        Assert.False(binding.Apply(binding.State));

        Assert.Equal(
            1u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(
                    document.ValueAt(1, new[] { 0, 1, 4 }))));
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
                    "objectives.zrd",
                    null,
                    new List<object?> { inner })));

    private static List<object?> PlayerInit(
        float x,
        float y,
        float z,
        float pitch,
        float yaw,
        float roll,
        float throttle,
        float speed) =>
        new()
        {
            1f,
            new List<object?> { x, y, z },
            new List<object?> { pitch, yaw, roll },
            throttle,
            speed,
        };

    private static void AssertUnbound(
        CampaignMissionDocument document,
        string expectedProblem)
    {
        Assert.False(
            CampaignPlayerInit.TryBind(
                document,
                out CampaignPlayerInit? binding,
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

using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Typed campaign roster-start editing over invented variable-width blocks.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignRosterStartsTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void EntriesBindInOrderAndEditOnlyTheirExactSpawnLeaves()
    {
        List<object?> player = Fields(
            1f,
            2f,
            3f,
            4f,
            1f,
            0f,
            1f);
        List<object?> template = Fields(
            10f,
            20f,
            30f,
            40f,
            2f,
            3f,
            0f);
        template.Add("unknown tail");
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                Header("Player", "EnemyParams"),
                new List<object?> { "player", player },
                new List<object?> { "secfury_1", template },
            });

        Assert.True(
            CampaignRosterStarts.TryBind(
                document,
                out CampaignRosterStarts? roster,
                out string problem),
            problem);
        Assert.Equal(2, roster.Entries.Count);
        Assert.True(roster.Entries[0].IsPlayer);
        Assert.True(roster.Entries[0].Enabled);
        CampaignRosterStart enemy = roster.Entries[1];
        Assert.Equal("secfury_1", enemy.Name);
        Assert.Equal(2, enemy.RawIndex);
        Assert.False(enemy.IsPlayer);
        Assert.False(enemy.Enabled);
        Assert.Equal(
            new CampaignRosterStartState(
                10f,
                20f,
                30f,
                40f,
                2,
                3),
            enemy.State);

        var edited = new CampaignRosterStartState(
            -10f,
            -20f,
            -30f,
            -40f,
            4,
            5);
        Assert.True(enemy.Apply(edited));
        Assert.False(enemy.Apply(edited));

        Assert.Equal(
            1f,
            document.ValueAt(1, new[] { 1, 1, 1, 0 }));
        Assert.Equal(
            -10f,
            document.ValueAt(1, new[] { 2, 1, 1, 0 }));
        Assert.Equal(
            -40f,
            document.ValueAt(1, new[] { 2, 1, 2 }));
        Assert.Equal(
            4f,
            document.ValueAt(1, new[] { 2, 1, 3 }));
        Assert.Equal(
            5f,
            document.ValueAt(1, new[] { 2, 1, 4 }));
        Assert.Equal(
            0f,
            document.ValueAt(1, new[] { 2, 1, 5 }));
        Assert.Equal(
            "unknown tail",
            document.ValueAt(1, new[] { 2, 1, 7 }));

        List<object?> runtimeFields = AiSkills.LoadRoster(
                new ZrdrSet("edited mission", document.Files))
            .Single(entry => entry.Name == "secfury_1")
            .Fields;
        (Vector3 Position, float YawDeg)? pose =
            AiSkills.RosterSpawnPose(runtimeFields);
        Assert.True(pose.HasValue);
        Assert.Equal(
            new Vector3(-10f, -20f, -30f),
            pose.Value.Position);
        Assert.Equal(-40f, pose.Value.YawDeg);
        Assert.Equal(4, AiSkills.RosterTeam(runtimeFields));
        Assert.Equal(5, AiSkills.RosterGroup(runtimeFields));
        Assert.False(AiSkills.RosterEnabled(runtimeFields));
    }

    [Fact]
    public void MissingOrMalformedSpawnPrefixesStayUnbound()
    {
        AssertUnbound(
            CampaignMissionDocument.FromScope(
                Mission,
                Scope(new ZrdrFile(
                    "other.zrd",
                    null,
                    new List<object?>()))),
            "aiv");
        AssertUnbound(
            Document(new List<object?> { "not a header" }),
            "header");
        AssertUnbound(
            Document(
                new List<object?>
                {
                    Header("short"),
                    new List<object?>
                    {
                        "short_1",
                        new List<object?> { 1f, new List<object?> { 1f, 2f, 3f } },
                    },
                }),
            "six-slot");
        AssertUnbound(
            Document(
                new List<object?>
                {
                    Header("fractional"),
                    new List<object?>
                    {
                        "fractional_1",
                        Fields(1f, 2f, 3f, 4f, 1.5f, 0f, 1f),
                    },
                }),
            "team");
        AssertUnbound(
            Document(
                new List<object?>
                {
                    Header("enabled"),
                    new List<object?>
                    {
                        "enabled_1",
                        Fields(1f, 2f, 3f, 4f, 1f, 0f, 2f),
                    },
                }),
            "enabled");
    }

    [Fact]
    public void InvalidTypedValuesFailBeforeChangingAnyLeaf()
    {
        CampaignMissionDocument document = Document(
            new List<object?>
            {
                Header("Enemy"),
                new List<object?>
                {
                    "secfury_1",
                    Fields(1f, 2f, 3f, 4f, 1f, 0f, 1f),
                },
            });
        Assert.True(
            CampaignRosterStarts.TryBind(
                document,
                out CampaignRosterStarts? roster,
                out string problem),
            problem);
        CampaignRosterStart entry =
            Assert.Single(roster.Entries);
        CampaignRosterStartState original =
            entry.State;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => entry.Apply(original with
            {
                X = 99f,
                YawDeg = float.NaN,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => entry.Apply(original with
            {
                Team = -2,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => entry.Apply(original with
            {
                Group = -1,
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => entry.Apply(original with
            {
                Team = 16_777_217,
            }));

        Assert.Equal(original, entry.State);
        Assert.False(document.IsDirty);
    }

    private static CampaignMissionDocument Document(
        List<object?> root) =>
        CampaignMissionDocument.FromScope(
            Mission,
            Scope(
                new ZrdrFile(
                    "before.zrd",
                    null,
                    new List<object?> { "unchanged" }),
                new ZrdrFile(
                    "AIV.ZRD",
                    null,
                    root)));

    private static List<object?> Fields(
        float x,
        float y,
        float z,
        float yaw,
        float team,
        float group,
        float enabled) =>
        new()
        {
            -1f,
            new List<object?> { x, y, z },
            yaw,
            team,
            group,
            enabled,
            "",
        };

    private static List<object?> Header(
        params string[] labels)
    {
        var header = new List<object?>();
        for (int i = 0; i < labels.Length; i++)
        {
            header.Add((float)i);
            header.Add(labels[i]);
        }

        return header;
    }

    private static void AssertUnbound(
        CampaignMissionDocument document,
        string expectedProblem)
    {
        Assert.False(
            CampaignRosterStarts.TryBind(
                document,
                out CampaignRosterStarts? roster,
                out string problem));
        Assert.Null(roster);
        Assert.Contains(
            expectedProblem,
            problem,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ZrdrSet Scope(params ZrdrFile[] files) =>
        new("invented mission zrdr.zbd", files);
}

using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Mech3;
using CSVM.Tooling;
using Xunit;
using static CSVM.Tests.CampaignMissionZeppelinsTests;

namespace CSVM.Tests;

/// <summary>Zeppelin references use runtime-selected prefixes and exact source paths.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignZeppelinReferenceTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void ExtractsOnlyConsumedNamesAndSuppressesShadowedAndBareKeys()
    {
        var record = Row(
            "node", Row("old_hull", "not-a-node"),
            "NODE", Row("hull", "node-tail"),
            "position", Row(1f, 2f, 3f),
            "net", Row("old_net"),
            "NET", Row("route", "not-a-net"),
            "targets", Row("old_target"),
            "TARGETS", Row("world_target", Row("not-a-target"), null, "player"),
            "engines", Row("old_engine"),
            "ENGINES",
            "healthy", Row(Row("bag", "panels", "healthy-tail"), Row("bad", 1f)),
            "gasbags", Row(Row("bag", 100f, Row("destroy", "animation-tail"), "gasbag-tail"), Row("invalid", "hp")),
            "left_cannons", Row(Row("left", "deploy", "retract", "cannon-tail"), Row("invalid", "deploy")),
            "cannon_health", Row(Row("left", "not-gunback", "not-frame", "bag", 10f,
                Row("dead", "destroy-tail"), Row(Row(0.6f, "stage", "stage-tail"), Row(1f, 2f)), "health-tail")),
            "unknown", Row("net", Row("tail-net"), "targets", Row("tail-target"),
                "ANIM_NAME", Row("false_definition")));
        CampaignMissionDocument document = Document(
            Reader(Row(record)),
            Reader(Row(Row("node", Row("ignored")))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());

        ReferenceSnapshot snapshot = diagnostics.Current;
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_hull").State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "hull").State);
        Assert.Equal(new[] { 0, 0, 3, 0 }, Find(snapshot, "hull").Reference.Source.Path);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_net").State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "route").State);
        Assert.Equal(new[] { 0, 0, 9, 0 }, Find(snapshot, "route").Reference.Source.Path);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_target").State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "world_target").State);
        Assert.Equal(ReferenceState.Unverified, Find(snapshot, "player").State);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_engine").State);
        Assert.Equal(ReferenceState.Unverified, Find(snapshot, "panels").State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "destroy").State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "deploy").State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "retract").State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "dead").State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "stage").State);
        Assert.DoesNotContain(snapshot.References, item => item.Reference.Source.FileIndex == 1);
        string[] ignored =
        {
            "not-a-node", "node-tail", "not-a-net", "not-a-target", "healthy-tail", "invalid",
            "animation-tail", "gasbag-tail", "cannon-tail", "not-gunback", "not-frame",
            "destroy-tail", "stage-tail", "health-tail", "tail-net", "tail-target", "false_definition",
        };
        foreach (string name in ignored)
        {
            Assert.DoesNotContain(snapshot.References, item => item.Reference.Value.Name == name);
        }
    }

    [Fact]
    public void NewMissingReferencesBlockAndRepairWhilePartsAndActorsStayUnverified()
    {
        var record = Row(
            "node", Row("hull"),
            "position", Row(0f, 1f, 2f),
            "net", Row("route"),
            "targets", Row("world_target"),
            "engines", Row("engine"),
            "left_cannons", Row(Row("left", "deploy", "retract")),
            "unknown", Row("ANIM_NAME", Row("false_definition")));
        CampaignMissionDocument document = Document(Reader(Row(record)));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceSnapshot original = diagnostics.Current;
        Assert.False(original.HasErrors);
        Assert.Equal(ReferenceState.Resolved, Find(original, "hull").State);
        Assert.Equal(ReferenceState.Resolved, Find(original, "route").State);
        Assert.Equal(ReferenceState.Unverified, Find(original, "engine").State);

        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1, 0 }, "missing_hull"));
        ReferenceSnapshot broken = diagnostics.Current;
        Assert.Equal(ReferenceState.Missing, Find(broken, "missing_hull").State);
        Assert.True(broken.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1, 0 }, "hull"));
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 5, 0 }, "missing_route"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "missing_route").State);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 5, 0 }, "route"));
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 7, 0 }, "staged_target"));
        Assert.Equal(ReferenceState.Unverified, Find(diagnostics.Current, "staged_target").State);
        Assert.False(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 11, 0, 1 }, "missing_animation"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "missing_animation").State);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 11, 0, 1 }, "false_definition"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "false_definition").State);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 11, 0, 1 }, "deploy"));
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void InheritedMissingStaysNonblockingAndUnavailableInputsAreUnverified()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("absent_hull"),
            "position", Row(0f, 1f, 2f),
            "net", Row("absent_net")))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.InheritedMissing, Find(diagnostics.Current, "absent_hull").State);
        Assert.Equal(ReferenceState.InheritedMissing, Find(diagnostics.Current, "absent_net").State);
        Assert.False(diagnostics.Current.HasErrors);

        using var unavailable = new CampaignReferenceDiagnostics(document, Bundle(worldAvailable: false));
        Assert.Equal(ReferenceState.Unverified, Find(unavailable.Current, "absent_hull").State);
    }

    [Fact]
    public void UnavailableAnimationArchiveCannotProveANewNameMissing()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("hull"),
            "position", Row(0f, 1f, 2f),
            "net", Row("route"),
            "left_cannons", Row(Row("left", "deploy", "retract"))))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(animationsAvailable: false));
        Assert.Equal(ReferenceState.Unverified, Find(diagnostics.Current, "deploy").State);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 7, 0, 1 }, "unknown_animation"));
        Assert.Equal(ReferenceState.Unverified, Find(diagnostics.Current, "unknown_animation").State);
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void DuplicateGasbagsUseLastAndDuplicateHealthyZonesUseFirstStateChild()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("hull"),
            "position", Row(0f, 1f, 2f),
            "net", Row("route"),
            "healthy", Row(Row("bag", "first_child"), Row("BAG", "second_child")),
            "gasbags", Row(
                Row("BAG", 10f, Row("old_destroy")),
                Row("bag", 20f, Row("destroy")),
                Row("orphan", 30f, Row("unused_destroy")))))));
        CampaignMissionZeppelins binding = CampaignMissionZeppelins.Bind(document);
        CampaignZeppelinEntry entry = Assert.Single(binding.Entries);
        Assert.Contains("first entry", Section(entry, "Healthy zones").Rows[1].Note);
        Assert.Contains("later", Section(entry, "Gasbags").Rows[0].Note);
        Assert.Contains("No healthy zone", Section(entry, "Gasbags").Rows[2].Note);
        Assert.Equal(2, Assert.Single(Load(document)).Healthy.Count);
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.Unverified, Find(diagnostics.Current, "first_child").State);
        Assert.Equal(ReferenceState.Suppressed, Find(diagnostics.Current, "second_child").State);
        Assert.Equal(ReferenceState.Suppressed, Find(diagnostics.Current, "old_destroy").State);
        Assert.Equal(ReferenceState.Resolved, Find(diagnostics.Current, "destroy").State);
        Assert.Equal(ReferenceState.Unverified, Find(diagnostics.Current, "unused_destroy").State);
        Assert.False(diagnostics.Current.HasErrors);

        Assert.True(document.SetLeaf(0, new[] { 0, 0, 9, 0, 2, 0 }, "shadow_missing"));
        Assert.Equal(ReferenceState.Suppressed, Find(diagnostics.Current, "shadow_missing").State);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 9, 2, 2, 0 }, "orphan_missing"));
        Assert.Equal(ReferenceState.Unverified, Find(diagnostics.Current, "orphan_missing").State);
        Assert.False(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 9, 1, 2, 0 }, "active_missing"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "active_missing").State);
        Assert.True(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void MalformedRequiredRecordsAndEmptyReadersReportBlockingIssues()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            null,
            Row("node", Row("hull"), "position", Row(0f, float.NaN, 2f), "net", Row("route")))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceIssue issue = Assert.Single(diagnostics.Current.Issues.Where(item => item.Code == "malformed-zeppelin-record"));
        Assert.True(issue.BlocksReady);
        Assert.Equal(new[] { 0, 1 }, issue.Source!.Path);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 1, 3, 1 }, 1f));
        Assert.DoesNotContain(diagnostics.Current.Issues, item => item.Code == "malformed-zeppelin-record");
        Assert.False(diagnostics.Current.HasErrors);

        CampaignMissionDocument empty = Document(new ZrdrFile("zeppelins.zrd", null, Row()));
        using var emptyDiagnostics = new CampaignReferenceDiagnostics(empty, Bundle());
        Assert.Contains(emptyDiagnostics.Current.Issues, item => item.Code == "malformed-zeppelin-reader" && item.BlocksReady);
        Assert.True(emptyDiagnostics.Current.HasErrors);
    }

    private static CheckedReference Find(ReferenceSnapshot snapshot, string value) =>
        Assert.Single(snapshot.References.Where(reference => reference.Reference.Value.Name == value));

    private static CampaignMissionDependencyBundle Bundle(bool worldAvailable = true, bool animationsAvailable = true)
    {
        var world = new GameZ();
        world.Nodes.Add(new GameZNode { Index = 0, Name = "hull", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 1, Name = "world_target", Kind = "Group" });
        var net = Enumerable.Repeat<object?>(0f, 11).ToList();
        net.Add(Row(Row(0f, 0f, 0f)));
        net.Add(Row());
        var chapter = new ZrdrSet("chapter", new[]
        {
            new ZrdrFile("neindex.zrd", null, Row(Row(1f, 10f, "route"))),
            new ZrdrFile("ne000010.zrd", null, Row(net)),
        });
        AnimArchive animations = AnimArchive.FromDecoded(
            "mission",
            new[] { "destroy", "deploy", "retract", "dead", "stage" }
                .Select(name => new AnimDefinition { Name = "hull", AnimName = name }).ToList(),
            new List<(string Name, SiScript Script)>());
        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            Mission,
            Dependency(new ZrdrSet("shared", Array.Empty<ZrdrFile>()), CampaignDependencyState.Ready),
            Dependency<GameZ?>(worldAvailable ? world : null,
                worldAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable),
            Dependency(new TextureSet("textures", Array.Empty<TextureImage>()), CampaignDependencyState.Ready),
            Dependency(chapter, CampaignDependencyState.Ready),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, optional: true),
            Dependency<AnimArchive?>(animationsAvailable ? animations : null,
                animationsAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable, optional: true),
            Dependency(new Messages(), CampaignDependencyState.Ready, optional: true),
            Dependency<InterpScripts?>(null, CampaignDependencyState.Missing, optional: true),
            Dependency<MissionSetup?>(null, CampaignDependencyState.NotChecked, optional: true),
            null);
    }

    private static CampaignDependency<T> Dependency<T>(T value, CampaignDependencyState state, bool optional = false) =>
        new(value, new CampaignDependencyAvailability("invented", state, optional));
}

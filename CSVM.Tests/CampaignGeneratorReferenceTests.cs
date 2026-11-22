using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Mech3;
using CSVM.Mech3.Anim;
using CSVM.Session.Objectives;
using CSVM.Tooling;
using Xunit;
using static CSVM.Tests.CampaignMissionGeneratorsTests;

namespace CSVM.Tests;

/// <summary>Generator references use the runtime's selected readers and binding scopes.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignGeneratorReferenceTests
{
    [Fact]
    public void SelectedHostTemplateNetsAndOriginHaveExactPathsAndSuppressDuplicates()
    {
        var record = Row(
            "node", Row("old_host", "host-tail"),
            "NODE", Row("hostA"),
            "vehicle", Row("params", Row("old_vehicle")),
            "VEHICLE", Row(
                "params", Row("old_template"),
                "PARAMS", Row("template", "template-tail"),
                "nets", Row("old_net"),
                "NETS", Row("good_net", 42f, Row("nested_net"), "missing_net")),
            "origin", Row("old_origin"),
            "ORIGIN", Row("bay", "origin-tail"),
            "zeppelin",
            "open_anim", Row("door", "door-tail"),
            "unknown", Row("node", Row("hidden_host")));
        CampaignMissionDocument document = Document(
            Reader(Row(record)),
            Reader(Row(Row("node", Row("ignored_reader"), "vehicle", Row()))),
            Roster());
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceSnapshot snapshot = diagnostics.Current;

        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_host", ReferenceRule.GeneratorHost).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "hostA", ReferenceRule.GeneratorHost).State);
        Assert.Equal(new[] { 0, 0, 3, 0 }, Find(snapshot, "hostA", ReferenceRule.GeneratorHost).Reference.Source.Path);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_vehicle", ReferenceRule.TemplateName).State);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_template", ReferenceRule.TemplateName).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "template", ReferenceRule.TemplateName).State);
        Assert.Equal(new[] { 0, 0, 7, 3, 0 }, Find(snapshot, "template", ReferenceRule.TemplateName).Reference.Source.Path);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_net", ReferenceRule.GeneratorNet).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "good_net", ReferenceRule.GeneratorNet).State);
        Assert.Equal(new[] { 0, 0, 7, 7, 0 }, Find(snapshot, "good_net", ReferenceRule.GeneratorNet).Reference.Source.Path);
        Assert.Equal(ReferenceState.InheritedMissing, Find(snapshot, "missing_net", ReferenceRule.GeneratorNet).State);
        Assert.Equal(new[] { 0, 0, 7, 7, 3 }, Find(snapshot, "missing_net", ReferenceRule.GeneratorNet).Reference.Source.Path);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, "old_origin", ReferenceRule.GeneratorChild).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, "bay", ReferenceRule.GeneratorChild).State);
        Assert.Equal(new[] { 0, 0, 11, 0 }, Find(snapshot, "bay", ReferenceRule.GeneratorChild).Reference.Source.Path);
        Assert.Equal(ReferenceState.Unverified, Find(snapshot, "door", ReferenceRule.EffectiveAnimation).State);
        Assert.DoesNotContain(snapshot.References, item => item.Reference.Value.Name is
            "host-tail" or "template-tail" or "nested_net" or "origin-tail" or "door-tail" or "hidden_host"
            or "ignored_reader");
        Assert.DoesNotContain(snapshot.References, item => item.Reference.Source.FileIndex == 1);
        Assert.False(snapshot.HasErrors);
    }

    [Fact]
    public void ChildAndDerivedSurfaceNodesStayInsideTheSelectedHost()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("hostA"),
            "vehicle", Row("nets", Row("good_net")),
            "zeppelin",
            "origin", Row("wrong_origin"),
            "open_anim", Row("door")))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceSnapshot original = diagnostics.Current;
        Assert.Equal(ReferenceState.InheritedMissing, Find(original, "wrong_origin", ReferenceRule.GeneratorChild).State);
        Assert.False(original.HasErrors);

        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1, 0 }, "other"));
        Assert.Equal(ReferenceState.Resolved, Find(diagnostics.Current, "wrong_origin", ReferenceRule.GeneratorChild).State);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 6, 0 }, "missing_origin"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "missing_origin", ReferenceRule.GeneratorChild).State);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 6, 0 }, "wrong_origin"));
        Assert.False(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1, 0 }, "roster_only"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "roster_only", ReferenceRule.GeneratorHost).State);

        CampaignMissionDocument surface = Document(Reader(Row(Row(
            "node", Row("hostA"),
            "vehicle", Row("nets", Row("good_net"))))));
        using var surfaceDiagnostics = new CampaignReferenceDiagnostics(surface, Bundle());
        ReferenceSnapshot paths = surfaceDiagnostics.Current;
        CheckedReference point0 = Find(paths, EnemyGenerators.LaunchPathNode("hostA", 0), ReferenceRule.GeneratorChild);
        CheckedReference point1 = Find(paths, EnemyGenerators.LaunchPathNode("hostA", 1), ReferenceRule.GeneratorChild);
        Assert.Equal(ReferenceState.Resolved, point0.State);
        Assert.Equal(ReferenceState.Resolved, point1.State);
        Assert.True(point0.Reference.Source.Derived);
        Assert.True(point1.Reference.Source.Derived);
        Assert.Equal(new[] { 0, 0, 1, 0 }, point0.Reference.Source.Path);
        Assert.Equal(ReferenceState.Unverified,
            Find(paths, EnemyGenerators.DefaultDoorAnim("hostA"), ReferenceRule.EffectiveAnimation).State);
        Assert.False(paths.HasErrors);
    }

    [Fact]
    public void ANewHostDoesNotInheritAnUnresolvedChildFromAnotherHost()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("hostA"),
            "vehicle", Row("nets", Row("good_net")),
            "zeppelin",
            "origin", Row("missing_origin")))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.InheritedMissing, Find(diagnostics.Current, "missing_origin", ReferenceRule.GeneratorChild).State);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1, 0 }, "HOSTA"));
        Assert.Equal(ReferenceState.InheritedMissing, Find(diagnostics.Current, "missing_origin", ReferenceRule.GeneratorChild).State);
        Assert.False(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1, 0 }, "other"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "missing_origin", ReferenceRule.GeneratorChild).State);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1, 0 }, "hostA"));
        Assert.Equal(ReferenceState.InheritedMissing, Find(diagnostics.Current, "missing_origin", ReferenceRule.GeneratorChild).State);
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void OnlyMissionDisabledPlannableTemplatesAndChapterNetNamesResolve()
    {
        CampaignMissionDocument document = Document(
            Reader(Row(Row(
                "node", Row("hostA"),
                "vehicle", Row("params", Row("template"), "nets", Row("good_net")),
                "zeppelin"))),
            Roster(),
            new ZrdrFile("neindex.zrd", null, Row(Row(1f, 20f, "mission_only_net"))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.Resolved, Find(diagnostics.Current, "template", ReferenceRule.TemplateName).State);
        Assert.Equal(ReferenceState.Resolved, Find(diagnostics.Current, "good_net", ReferenceRule.GeneratorNet).State);

        int[] template = { 0, 0, 3, 1, 0 };
        int[] net = { 0, 0, 3, 3, 0 };
        foreach (string invalid in new[] { "enabled_template", "bad_template", "chapter_template" })
        {
            Assert.True(document.SetLeaf(0, template, invalid));
            Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, invalid, ReferenceRule.TemplateName).State);
            Assert.True(diagnostics.Current.HasErrors);
        }

        Assert.True(document.SetLeaf(0, template, "template"));
        Assert.True(document.SetLeaf(0, net, "mission_only_net"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "mission_only_net", ReferenceRule.GeneratorNet).State);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, net, "good_net"));
        Assert.False(diagnostics.Current.HasErrors);

        using var unavailable = new CampaignReferenceDiagnostics(document, Bundle(sharedAvailable: false));
        Assert.Equal(ReferenceState.Unverified, Find(unavailable.Current, "template", ReferenceRule.TemplateName).State);
        using var noChapter = new CampaignReferenceDiagnostics(document, Bundle(chapterAvailable: false));
        Assert.Equal(ReferenceState.Unverified, Find(noChapter.Current, "good_net", ReferenceRule.GeneratorNet).State);
        using var noWorld = new CampaignReferenceDiagnostics(document, Bundle(worldAvailable: false));
        Assert.Equal(ReferenceState.Unverified, Find(noWorld.Current, "hostA", ReferenceRule.GeneratorHost).State);
        using var emptyNet = new CampaignReferenceDiagnostics(document, Bundle(emptyNet: true));
        Assert.Equal(ReferenceState.Resolved, Find(emptyNet.Current, "good_net", ReferenceRule.GeneratorNet).State);
    }

    [Fact]
    public void TemplateIndexRefreshesAfterRosterEdits()
    {
        CampaignMissionDocument document = Document(
            Reader(Row(Row(
                "node", Row("hostA"),
                "vehicle", Row("params", Row("template"), "nets", Row("good_net")),
                "zeppelin"))),
            Roster());
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.Resolved, Find(diagnostics.Current, "template", ReferenceRule.TemplateName).State);
        Assert.True(document.SetLeaf(1, new[] { 0, 2 }, "renamed"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "template", ReferenceRule.TemplateName).State);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 3, 1, 0 }, "renamed"));
        Assert.Equal(ReferenceState.Resolved, Find(diagnostics.Current, "renamed", ReferenceRule.TemplateName).State);
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void MissingMissionRosterProducesATemplateVerdictWithoutCrashing()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("hostA"),
            "vehicle", Row("params", Row("missing_template"), "nets", Row("good_net")),
            "zeppelin"))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.InheritedMissing,
            Find(diagnostics.Current, "missing_template", ReferenceRule.TemplateName).State);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 3, 1, 0 }, "another_template"));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, "another_template", ReferenceRule.TemplateName).State);
        Assert.True(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void ObjectiveUsesFollowTheFirstDirectiveAndContiguousSelectedObjectives()
    {
        var objectives = new ZrdrFile("objectives.zrd", null, Row(Row(
            "OBJECTIVE1", Row(
                "WAKEUP_GENERATOR", Row("hostA", 2f, "ignored_tail"),
                "WAKEUP_GENERATOR", Row("shadowed"),
                "DEDG", Row(3f, 4f, "hostA", "dedg_tail"),
                "unknown", Row("WAKEUP_GENERATOR", Row("nested"))),
            "OBJECTIVE2", Row("DEDG", Row(5f, 6f, "lost")),
            "OBJECTIVE4", Row("WAKEUP_GENERATOR", Row("after_gap")))));
        CampaignMissionDocument document = Document(
            Reader(Row(Row("node", Row("hostA"), "vehicle", Row("nets", Row("good_net")), "zeppelin"))),
            objectives,
            new ZrdrFile("objectives.zrd", null, Row(Row("OBJECTIVE1", Row("WAKEUP_GENERATOR", Row("second_reader"))))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceSnapshot snapshot = diagnostics.Current;
        CheckedReference wake = FindContext(snapshot, "OBJECTIVE1:WAKEUP_GENERATOR", "hostA");
        CheckedReference dedg = FindContext(snapshot, "OBJECTIVE1:DEDG", "hostA");
        Assert.Equal(ReferenceState.Resolved, wake.State);
        Assert.Equal(ReferenceState.Resolved, dedg.State);
        Assert.Equal(new[] { 0, 1, 1, 0 }, wake.Reference.Source.Path);
        Assert.Equal(new[] { 0, 1, 5, 2 }, dedg.Reference.Source.Path);
        Assert.Equal(ReferenceState.Suppressed, FindContext(snapshot, "OBJECTIVE1:WAKEUP_GENERATOR", "shadowed").State);
        Assert.Equal(ReferenceState.InheritedMissing, FindContext(snapshot, "OBJECTIVE2:DEDG", "lost").State);
        Assert.DoesNotContain(snapshot.References, item => item.Reference.Kind == ReferenceKind.Generator
            && item.Reference.Value.Name is "ignored_tail" or "dedg_tail" or "nested" or "after_gap" or "second_reader");
        Assert.True(document.SetLeaf(1, new[] { 0, 1, 1, 0 }, "lost"));
        Assert.Equal(ReferenceState.Missing, FindContext(diagnostics.Current, "OBJECTIVE1:WAKEUP_GENERATOR", "lost").State);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(1, new[] { 0, 1, 1, 0 }, "hostA"));
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void BareFirstObjectiveDirectivesSuppressLaterGeneratorNames()
    {
        var objectives = new ZrdrFile("objectives.zrd", null, Row(Row(
            "OBJECTIVE1", Row(
                "WAKEUP_GENERATOR",
                "WAKEUP_GENERATOR", Row("hostA"),
                "DEDG",
                "DEDG", Row(1f, 2f, "hostA")))));
        CampaignMissionDocument document = Document(
            Reader(Row(Row("node", Row("hostA"), "vehicle", Row("nets", Row("good_net")), "zeppelin"))),
            objectives);
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.Suppressed,
            FindContext(diagnostics.Current, "OBJECTIVE1:WAKEUP_GENERATOR", "hostA").State);
        Assert.Equal(ReferenceState.Suppressed,
            FindContext(diagnostics.Current, "OBJECTIVE1:DEDG", "hostA").State);
        ObjectiveDef runtime = Assert.Single(ObjectiveScript.Load(new ZrdrSet("mission", document.Files)).Objectives);
        Assert.Null(runtime.WakeupGenerator);
        Assert.Null(runtime.Dedg);
    }

    [Fact]
    public void AFinalBareOverrideSuppressesPriorGeneratorNamesAndMalformedRecordsBlock()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("hostA"),
            "vehicle", Row("nets", Row("unused")),
            "VEHICLE",
            "origin", Row("unused_origin"),
            "ORIGIN",
            "zeppelin"))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.Suppressed, Find(diagnostics.Current, "unused", ReferenceRule.GeneratorNet).State);
        Assert.Equal(ReferenceState.Suppressed, Find(diagnostics.Current, "unused_origin", ReferenceRule.GeneratorChild).State);
        Assert.DoesNotContain(diagnostics.Current.Issues, issue => issue.Code == "malformed-generator-record");
        Assert.Contains(diagnostics.Current.Issues,
            issue => issue.Code == "generator-no-net-names" && !issue.BlocksReady);
        Assert.Single(Load(document));

        CampaignMissionDocument malformed = Document(Reader(Row(
            Row("node", Row("hostA"), "vehicle", Row()),
            Row("node", Row("broken")))));
        using var malformedDiagnostics = new CampaignReferenceDiagnostics(malformed, Bundle());
        ReferenceIssue issue = Assert.Single(malformedDiagnostics.Current.Issues.Where(item => item.Code == "malformed-generator-record"));
        Assert.True(issue.BlocksReady);
        Assert.Equal(new[] { 0, 1 }, issue.Source!.Path);
        Assert.True(malformedDiagnostics.Current.HasErrors);

        CampaignMissionDocument empty = Document(new ZrdrFile("egen.zrd", null, Row()));
        using var emptyDiagnostics = new CampaignReferenceDiagnostics(empty, Bundle());
        Assert.Contains(emptyDiagnostics.Current.Issues,
            item => item.Code == "malformed-generator-reader" && item.BlocksReady);
    }

    private static CheckedReference Find(ReferenceSnapshot snapshot, string value, ReferenceRule rule) =>
        Assert.Single(snapshot.References.Where(item =>
            item.Reference.Value.Name == value && item.Reference.Rule == rule));

    private static CheckedReference FindContext(ReferenceSnapshot snapshot, string context, string value) =>
        Assert.Single(snapshot.References.Where(item =>
            item.Reference.Value.Name == value && item.Reference.Source.Context == context));

    private static ZrdrFile Roster()
    {
        var valid = Row(-1f, Row(0f, 0f, 0f), 0f, 1f, 1f, 0f);
        var enabled = Row(-1f, Row(0f, 0f, 0f), 0f, 1f, 1f, 1f);
        var bad = Row(-1f, null, 0f, 1f, 1f, 0f);
        return new ZrdrFile("aiv.zrd", null, Row(
            Row(3f, 1f, "template", 2f, "enabled_template", 3f, "bad_template"),
            Row("patrolboat_1", valid),
            Row("patrolboat_2", enabled),
            Row("patrolboat_3", bad)));
    }

    private static CampaignMissionDependencyBundle Bundle(
        bool worldAvailable = true,
        bool sharedAvailable = true,
        bool chapterAvailable = true,
        bool emptyNet = false)
    {
        var world = new GameZ();
        var root = new GameZNode { Index = 0, Name = "world", Kind = "World" };
        root.Children.AddRange(new[] { 1, 2 });
        var host = new GameZNode { Index = 1, Name = "hostA", Kind = "Group" };
        host.Children.AddRange(new[] { 3, 4, 5 });
        var other = new GameZNode { Index = 2, Name = "other", Kind = "Group" };
        other.Children.Add(6);
        world.Nodes.AddRange(new[]
        {
            root,
            host,
            other,
            new GameZNode { Index = 3, Name = "bay", Kind = "Group" },
            new GameZNode { Index = 4, Name = EnemyGenerators.LaunchPathNode("hostA", 0), Kind = "Group" },
            new GameZNode { Index = 5, Name = EnemyGenerators.LaunchPathNode("hostA", 1), Kind = "Group" },
            new GameZNode { Index = 6, Name = "wrong_origin", Kind = "Group" },
        });

        var net = Enumerable.Repeat<object?>(0f, 11).ToList();
        net.Add(emptyNet ? Row() : Row(Row(0f, 0f, 0f)));
        net.Add(Row());
        var chapter = new ZrdrSet("chapter", new[]
        {
            new ZrdrFile("neindex.zrd", null, Row(Row(1f, 10f, "good_net"))),
            new ZrdrFile("ne000010.zrd", null, Row(net)),
            new ZrdrFile("aiv.zrd", null, Row(Row(1f, 1f, "chapter_template"), Row("patrolboat_1", Row()))),
        });
        var shared = new ZrdrSet("shared", new[]
        {
            new ZrdrFile("vehicle.zrd", null, Row(Row("patrolboat", Row("mode", Row("ship"))))),
        });
        AnimArchive animation = AnimArchive.FromDecoded(
            "mission",
            new List<AnimDefinition> { new() { Name = "hostA", AnimName = "door" } },
            new List<(string Name, SiScript Script)>());
        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            Document().Mission,
            Dependency(shared, sharedAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable),
            Dependency<GameZ?>(worldAvailable ? world : null,
                worldAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable),
            Dependency(new TextureSet("textures", Array.Empty<TextureImage>()), CampaignDependencyState.Ready),
            Dependency(chapter, chapterAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, optional: true),
            Dependency<AnimArchive?>(animation, CampaignDependencyState.Ready, optional: true),
            Dependency(new Messages(), CampaignDependencyState.Ready, optional: true),
            Dependency<InterpScripts?>(null, CampaignDependencyState.Missing, optional: true),
            Dependency<MissionSetup?>(null, CampaignDependencyState.NotChecked, optional: true),
            null);
    }

    private static CampaignDependency<T> Dependency<T>(T value, CampaignDependencyState state, bool optional = false) =>
        new(value, new CampaignDependencyAvailability("invented", state, optional));
}

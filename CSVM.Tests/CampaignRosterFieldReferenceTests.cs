using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Mech3;
using CSVM.Mech3.Anim;
using CSVM.Tooling;
using Xunit;
using static CSVM.Tests.CampaignMissionGeneratorsTests;

namespace CSVM.Tests;

/// <summary>Roster reference verdicts use the selected reader and the runtime's chapter scopes.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignRosterFieldReferenceTests
{
    [Fact]
    public void NetChoicesResolveByRuntimeIdAndRefreshWithoutReadingTheShadowReader()
    {
        List<object?> fields = Fields();
        fields[0] = Row(5f, 5.75f, -1f, 9f, "opaque");
        CampaignMissionDocument document = Document(
            Roster(Row(1f, 1f, "initial"), Row("patrolboat_1", fields)),
            Roster(Row(1f, 1f, "shadow"), Row("patrolboat_9", Fields(9f))),
            new ZrdrFile("neindex.zrd", null, Row(Row(1f, 9f, "mission_only"))),
            NetFile(9));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceSnapshot snapshot = diagnostics.Current;
        CheckedReference[] references = NetReferences(snapshot);
        Assert.Equal(3, references.Length);
        Assert.Equal(new long?[] { 5, 5, 9 }, references.Select(item => item.Reference.Value.Number));
        Assert.Equal(new[] { 1, 1, 0, 0 }, references[0].Reference.Source.Path);
        Assert.Equal(new[] { 1, 1, 0, 1 }, references[1].Reference.Source.Path);
        Assert.Equal(new[] { 1, 1, 0, 3 }, references[2].Reference.Source.Path);
        Assert.Equal(ReferenceState.Resolved, references[0].State);
        Assert.Contains("chapter_five", references[0].Detail);
        Assert.Equal(ReferenceState.Resolved, references[1].State);
        Assert.Equal(ReferenceState.InheritedMissing, references[2].State);
        Assert.DoesNotContain(snapshot.References,
            item => item.Reference.Rule == ReferenceRule.NetId && item.Reference.Source.FileIndex == 1);
        Assert.False(snapshot.HasErrors);

        CampaignRosterStart entry = Entry(document);
        CampaignRosterFieldGroup net = Assert.Single(entry.Groups.Where(group => group.Label == "AI net IDs"));
        Assert.Equal(new[] { "net_0", "net_2", "net_3" }, net.Fields.Select(field => field.Key));
        Assert.True(entry.Apply(net, Values(entry, net, "net_0", 9f)));
        references = NetReferences(diagnostics.Current);
        Assert.Equal(ReferenceState.Missing, references[0].State);
        Assert.True(diagnostics.Current.HasErrors);
        entry = Entry(document);
        net = Assert.Single(entry.Groups.Where(group => group.Label == "AI net IDs"));
        Assert.True(entry.Apply(net, Values(entry, net, "net_0", 5f)));
        Assert.False(diagnostics.Current.HasErrors);
        Assert.Equal(5.75f, document.ValueAt(0, new[] { 1, 1, 0, 1 }));
    }

    [Fact]
    public void MissingEmptyAndUnavailableChapterNetsHaveDistinctVerdicts()
    {
        CampaignMissionDocument document = Document(Roster(
            Row(3f, 1f, "one", 2f, "two", 3f, "three"),
            Row("patrolboat_1", Fields(5f)),
            Row("patrolboat_2", Fields(6f)),
            Row("patrolboat_3", Fields(7f))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        CheckedReference[] references = NetReferences(diagnostics.Current);
        Assert.Equal(new[] { ReferenceState.Resolved, ReferenceState.InheritedMissing, ReferenceState.InheritedMissing },
            references.Select(item => item.State));
        Assert.Equal(new[] { 5, 6, 7 }, AiSkills.LoadRoster(new ZrdrSet("mission", document.Files))
            .Select(block => Assert.Single(AiSkills.RosterNetIds(block.Fields))));

        using var unavailable = new CampaignReferenceDiagnostics(document, Bundle(chapterAvailable: false));
        Assert.All(NetReferences(unavailable.Current),
            reference => Assert.Equal(ReferenceState.Unverified, reference.State));
        Assert.True(unavailable.Current.HasErrors);
        Assert.Contains(
            unavailable.Current.Issues,
            issue => issue.Code == "dependency-unavailable" && issue.BlocksReady);
    }

    [Fact]
    public void TargetStagingIndexesOnlyWellFormedBlocksInTheSelectedRoster()
    {
        CampaignMissionDocument document = Document(
            Roster(Row(2f, 1f, "good", 2f, "bad"),
                Row("patrolboat_1", Fields()), Row("bad_actor")),
            Roster(Row(1f, 1f, "shadow"), Row("shadow_actor", Fields())),
            new ZrdrFile("targets.zrd", null, Row(Row(
                Row("nodes", Row("patrolboat_1", "bad_actor", "shadow_actor"))))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        CheckedReference[] targets = diagnostics.Current.References
            .Where(item => item.Reference.Rule == ReferenceRule.TargetNode).ToArray();
        Assert.Equal(3, targets.Length);
        Assert.Equal(ReferenceState.Unverified, targets[0].State);
        Assert.Equal(ReferenceState.InheritedMissing, targets[1].State);
        Assert.Equal(ReferenceState.InheritedMissing, targets[2].State);
        Assert.Contains(diagnostics.Current.Issues, issue => issue.Code == "malformed-roster-row");
    }

    [Fact]
    public void MarkerReferencesUseTheRuntimeThresholdEvenWhenTheRawFlagIsNotCanonical()
    {
        List<object?> fields = Fields();
        fields[37] = 0.5f;
        fields[38] = "MSG_TYPE";
        fields[39] = "designer text";
        CampaignMissionDocument document = Document(Roster(
            Row(1f, 1f, "initial"), Row("patrolboat_1", fields)));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.False(AiSkills.RosterObjectiveTarget(fields));
        Assert.Empty(MarkerReferences(diagnostics.Current));

        Assert.True(document.SetLeaf(0, new[] { 1, 1, 37 }, 1.5f));
        Assert.True(AiSkills.RosterObjectiveTarget(
            Assert.Single(AiSkills.LoadRoster(new ZrdrSet("mission", document.Files))).Fields));
        Assert.Equal(new[] { "MSG_TYPE", "designer text" },
            MarkerReferences(diagnostics.Current).Select(item => item.Reference.Value.Name));
        Assert.All(MarkerReferences(diagnostics.Current), item => Assert.Equal(ReferenceState.Missing, item.State));
        Assert.DoesNotContain(Entry(document).Groups, group => group.Label == "Objective marker");

        Assert.True(document.SetLeaf(0, new[] { 1, 1, 37 }, 0f));
        Assert.Empty(MarkerReferences(diagnostics.Current));
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void UnsupportedFiniteValuesRemainRawWithoutOverflowingReferenceExtraction()
    {
        List<object?> fields = Fields();
        fields[32] = float.MaxValue;
        fields[65] = float.MaxValue;
        CampaignMissionDocument document = Document(Roster(
            Row(1f, 1f, "initial"), Row("patrolboat_1", fields)));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.DoesNotContain(diagnostics.Current.References, item =>
            item.Reference.Rule is ReferenceRule.ManeuverName or ReferenceRule.AccentId);
        Assert.DoesNotContain(Entry(document).Groups, group =>
            group.Label is "Signature maneuvers" or "Voice");
        Assert.Equal(float.MaxValue, document.ValueAt(0, new[] { 1, 1, 32 }));
        Assert.Equal(float.MaxValue, document.ValueAt(0, new[] { 1, 1, 65 }));
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void TemplateVerdictsFollowSelectedMissionReaderAndLastPlannableDuplicate()
    {
        List<object?> first = Fields(-1f, enabled: 0f);
        List<object?> bad = Fields(-1f, enabled: 0f);
        bad[1] = null;
        List<object?> last = Fields(-1f, enabled: 0f);
        CampaignMissionDocument document = Document(
            Reader(Row(Row("node", Row("host"),
                "vehicle", Row("params", Row("template"), "nets", Row("chapter_five")), "zeppelin"))),
            Roster(Row(3f, 1f, "template", 2f, "TEMPLATE", 3f, "template"),
                Row("patrolboat_1", first), Row("patrolboat_2", bad), Row("patrolboat_3", last)),
            Roster(Row(1f, 1f, "shadow_template"), Row("patrolboat_4", Fields(-1f, enabled: 0f))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.Resolved, Template(diagnostics.Current, "template").State);

        Assert.True(document.SetLeaf(1, new[] { 3, 0 }, "unknown_3"));
        Assert.Equal(ReferenceState.Resolved, Template(diagnostics.Current, "template").State);
        Assert.True(document.SetLeaf(1, new[] { 1, 0 }, "unknown_1"));
        Assert.Equal(ReferenceState.Missing, Template(diagnostics.Current, "template").State);
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(document.SetLeaf(1, new[] { 3, 0 }, "patrolboat_3"));
        Assert.Equal(ReferenceState.Resolved, Template(diagnostics.Current, "template").State);
        Assert.True(document.SetLeaf(1, new[] { 1, 0 }, "patrolboat_1"));
        Assert.False(diagnostics.Current.HasErrors);

        Assert.True(document.SetLeaf(0, new[] { 0, 0, 3, 1, 0 }, "shadow_template"));
        Assert.Equal(ReferenceState.Missing, Template(diagnostics.Current, "shadow_template").State);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 3, 1, 0 }, "chapter_template"));
        Assert.Equal(ReferenceState.Missing, Template(diagnostics.Current, "chapter_template").State);
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 3, 1, 0 }, "template"));
        using var noShared = new CampaignReferenceDiagnostics(document, Bundle(sharedAvailable: false));
        using var noChapter = new CampaignReferenceDiagnostics(document, Bundle(chapterAvailable: false));
        Assert.Equal(ReferenceState.Unverified, Template(noShared.Current, "template").State);
        Assert.Equal(ReferenceState.Unverified, Template(noChapter.Current, "template").State);
    }

    private static CheckedReference[] NetReferences(ReferenceSnapshot snapshot) =>
        snapshot.References.Where(item => item.Reference.Rule == ReferenceRule.NetId).ToArray();

    private static CheckedReference[] MarkerReferences(ReferenceSnapshot snapshot) =>
        snapshot.References.Where(item => item.Reference.Rule == ReferenceRule.MessageKey
            && item.Reference.Source.Path.Length == 3 && item.Reference.Source.Path[2] is 38 or 39).ToArray();

    private static CheckedReference Template(ReferenceSnapshot snapshot, string name) =>
        Assert.Single(snapshot.References.Where(item => item.Reference.Rule == ReferenceRule.TemplateName
            && item.Reference.Value.Name == name));

    private static CampaignRosterStart Entry(CampaignMissionDocument document)
    {
        Assert.True(CampaignRosterStarts.TryBind(document, out CampaignRosterStarts? roster, out string problem), problem);
        return Assert.Single(roster.Entries);
    }

    private static object[] Values(CampaignRosterStart entry, CampaignRosterFieldGroup group, string key, object value) =>
        group.Fields.Select(field => field.Key == key ? value : entry.Value(field)).ToArray();

    private static ZrdrFile Roster(List<object?> header, params List<object?>[] blocks) =>
        new("aiv.zrd", null, new List<object?> { header }.Concat(blocks).ToList());

    private static List<object?> Fields(float net = 5f, float enabled = 1f)
    {
        var fields = Enumerable.Repeat<object?>(-1f, 68).ToList();
        fields[0] = net;
        fields[1] = Row(0f, 0f, 0f);
        fields[2] = 0f;
        fields[3] = 1f;
        fields[4] = 0f;
        fields[5] = enabled;
        fields[6] = "";
        fields[20] = "";
        fields[32] = 0f;
        fields[33] = null;
        fields[37] = 0f;
        fields[38] = "";
        fields[39] = "";
        fields[40] = 0f;
        return fields;
    }

    private static ZrdrFile NetFile(int id, bool empty = false)
    {
        var record = Enumerable.Repeat<object?>(0f, 11).ToList();
        record.Add(empty ? Row() : Row(Row(0f, 0f, 0f)));
        record.Add(Row());
        return new ZrdrFile($"ne{id:D6}.zrd", null, Row(record));
    }

    private static CampaignMissionDependencyBundle Bundle(bool sharedAvailable = true, bool chapterAvailable = true)
    {
        var world = new GameZ();
        var root = new GameZNode { Index = 0, Name = "world", Kind = "World" };
        root.Children.Add(1);
        world.Nodes.Add(root);
        world.Nodes.Add(new GameZNode { Index = 1, Name = "host", Kind = "Group" });
        var shared = new ZrdrSet("shared", new[]
        {
            new ZrdrFile("vehicle.zrd", null, Row(Row("patrolboat", Row("mode", Row("ship"))))),
        });
        var chapter = new ZrdrSet("chapter", new[]
        {
            new ZrdrFile("neindex.zrd", null, Row(Row(2f, 5f, "chapter_five", 6f, "chapter_empty"))),
            NetFile(5),
            NetFile(6, empty: true),
            Roster(Row(1f, 1f, "chapter_template"), Row("patrolboat_1", Fields(-1f, enabled: 0f))),
        });
        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            Document().Mission,
            Dependency(shared, sharedAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable),
            Dependency<GameZ?>(world, CampaignDependencyState.Ready),
            Dependency(new TextureSet("textures", Array.Empty<TextureImage>()), CampaignDependencyState.Ready),
            Dependency(chapter, chapterAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, optional: true),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, optional: true),
            Dependency(new Messages(), CampaignDependencyState.Ready, optional: true),
            Dependency<InterpScripts?>(null, CampaignDependencyState.Missing, optional: true),
            Dependency<MissionSetup?>(null, CampaignDependencyState.NotChecked, optional: true),
            null);
    }

    private static CampaignDependency<T> Dependency<T>(T value, CampaignDependencyState state, bool optional = false) =>
        new(value, new CampaignDependencyAvailability("invented", state, optional));
}

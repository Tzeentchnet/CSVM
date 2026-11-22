using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Mech3;
using CSVM.Tooling;
using Xunit;

namespace CSVM.Tests;

/// <summary>Target references preserve raw identity and follow runtime lookup rules.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignTargetReferenceTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void TargetDefinitionsResolveAgainstWorldAndRefreshAfterRawRepair()
    {
        CampaignMissionDocument document = Document(
            Targets(Row(Row(
                Row("description", "GOOD"),
                Row("nodes", Row(Row("parent/branch", "leaf"), "ghost"))))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        int published = 0;
        diagnostics.Changed += _ => published++;

        ReferenceSnapshot initial = diagnostics.Current;
        CheckedReference scoped = Find(initial, ReferenceRule.TargetNode, 0, 1, 1, 0);
        Assert.Equal(ReferenceState.Resolved, scoped.State);
        Assert.Equal(new[] { "parent", "branch", "leaf" }, scoped.Reference.Value.Segments);
        Assert.Equal(new[] { 0, 1, 1, 0, 0 }, scoped.Reference.Source.LeafPaths[0]);
        Assert.Equal(new[] { 0, 1, 1, 0, 1 }, scoped.Reference.Source.LeafPaths[1]);
        Assert.Equal(
            ReferenceState.InheritedMissing,
            Find(initial, ReferenceRule.TargetNode, 0, 1, 1, 1).State);
        Assert.Equal(
            ReferenceState.Resolved,
            Find(initial, ReferenceRule.MessageKey, 0, 0, 1).State);
        Assert.Equal(1, published);

        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1 }, "UNKNOWN_MESSAGE"));
        Assert.True(document.SetLeaf(0, new[] { 0, 1, 1, 1 }, "new_ghost"));
        ReferenceSnapshot broken = diagnostics.Current;
        Assert.Equal(2, broken.DocumentRevision);
        Assert.Equal(
            ReferenceState.Missing,
            Find(broken, ReferenceRule.TargetNode, 0, 1, 1, 1).State);
        Assert.Equal(
            ReferenceState.Missing,
            Find(broken, ReferenceRule.MessageKey, 0, 0, 1).State);
        Assert.True(broken.HasErrors);
        Assert.Equal(2, published);

        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1 }, "GOOD"));
        Assert.True(document.SetLeaf(0, new[] { 0, 1, 1, 1 }, "leaf"));
        ReferenceSnapshot repaired = diagnostics.Current;
        Assert.Equal(
            ReferenceState.Resolved,
            Find(repaired, ReferenceRule.TargetNode, 0, 1, 1, 1).State);
        Assert.Equal(
            ReferenceState.Resolved,
            Find(repaired, ReferenceRule.MessageKey, 0, 0, 1).State);
        Assert.False(repaired.HasErrors);
        Assert.Equal(3, published);
    }

    [Fact]
    public void DuplicateLabelsNodesAndFilesFollowRuntimeSelection()
    {
        var first = Row(
            Row("description", "EARLY"),
            Row("description", "GOOD"),
            Row("nodes", Row("old")),
            Row("nodes", Row("same")));
        var second = Row(
            Row("description", "GOOD"),
            Row("nodes", Row("SAME")));
        var ignoredFile = Row(Row(
            Row("description", "IGNORED"),
            Row("nodes", Row("ignored"))));
        CampaignMissionDocument document = Document(
            Targets(Row(first, second)),
            Targets(ignoredFile));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());

        ReferenceSnapshot snapshot = diagnostics.Current;

        Assert.Equal(
            ReferenceState.Suppressed,
            Find(snapshot, ReferenceRule.MessageKey, 0, 0, 1).State);
        Assert.Equal(
            ReferenceState.Suppressed,
            Find(snapshot, ReferenceRule.MessageKey, 0, 1, 1).State);
        Assert.Equal(
            ReferenceState.Suppressed,
            Find(snapshot, ReferenceRule.TargetNode, 0, 2, 1, 0).State);
        Assert.Equal(
            ReferenceState.Suppressed,
            Find(snapshot, ReferenceRule.TargetNode, 0, 3, 1, 0).State);
        Assert.Equal(
            ReferenceState.Resolved,
            Find(snapshot, ReferenceRule.MessageKey, 1, 0, 1).State);
        Assert.Equal(
            ReferenceState.Resolved,
            Find(snapshot, ReferenceRule.TargetNode, 1, 1, 1, 0).State);
        Assert.DoesNotContain(
            snapshot.References,
            reference => reference.Reference.Source.FileIndex == 1
                && reference.Reference.Source.Container == "targets.zrd");
    }

    [Fact]
    public void WorldPathUsesTheFirstParentRatherThanSearchingLaterDuplicates()
    {
        CampaignMissionDocument document = Document(
            Targets(Row(Row(
                Row("nodes", Row(Row("parent", "second_only"), Row("parent", "branch", "leaf")))))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());

        ReferenceSnapshot snapshot = diagnostics.Current;
        Assert.Equal(
            ReferenceState.InheritedMissing,
            Find(snapshot, ReferenceRule.TargetNode, 0, 0, 1, 0).State);
        Assert.Equal(
            ReferenceState.Resolved,
            Find(snapshot, ReferenceRule.TargetNode, 0, 0, 1, 1).State);
    }

    [Fact]
    public void InheritedMissingReferencesRemainInheritedAfterCopyAndFlagIndexShift()
    {
        ZrdrSet chapter = Scope(Targets(Row(Row(
            Row("objective"),
            Row("description", "MISSING_MESSAGE"),
            Row("nodes", Row("missing_node"))))));
        CampaignMissionDocument document = Document(
            new ZrdrFile("other.zrd", null, Row("kept")));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(chapter: chapter));
        ReferenceSnapshot initial = diagnostics.Current;
        CheckedReference inheritedLabel = Find(initial, ReferenceRule.MessageKey, 0, 1, 1);
        CheckedReference inheritedNode = Find(initial, ReferenceRule.TargetNode, 0, 2, 1, 0);
        Assert.Equal(ReferenceScope.Chapter, inheritedLabel.Reference.Source.Scope);
        Assert.Null(inheritedLabel.Reference.Source.FileIndex);
        Assert.Equal(ReferenceState.InheritedMissing, inheritedLabel.State);
        Assert.Equal(ReferenceState.InheritedMissing, inheritedNode.State);

        CampaignMissionTargets inherited = Bind(document, chapter);
        Assert.Equal(1, inherited.CopyToMission());
        CampaignTargetEntry local = Assert.Single(Bind(document, chapter).Entries);
        Assert.True(local.Apply(local.State with { Objective = false }));
        ReferenceSnapshot copied = diagnostics.Current;
        CheckedReference copiedLabel = Find(copied, ReferenceRule.MessageKey, 0, 0, 1);
        CheckedReference copiedNode = Find(copied, ReferenceRule.TargetNode, 0, 1, 1, 0);
        Assert.Equal(ReferenceScope.Mission, copiedLabel.Reference.Source.Scope);
        Assert.Equal(1, copiedLabel.Reference.Source.FileIndex);
        Assert.Equal(ReferenceState.InheritedMissing, copiedLabel.State);
        Assert.Equal(ReferenceState.InheritedMissing, copiedNode.State);
        Assert.False(copied.HasErrors);

        CampaignTargetEntry rebound = Assert.Single(Bind(document, chapter).Entries);
        Assert.True(rebound.Apply(rebound.State with { Description = "NEW_MISSING" }));
        ReferenceSnapshot changed = diagnostics.Current;
        Assert.Equal(
            ReferenceState.Missing,
            Find(changed, ReferenceRule.MessageKey, 0, 0, 1).State);
        Assert.True(changed.HasErrors);
        rebound = Assert.Single(Bind(document, chapter).Entries);
        Assert.True(rebound.Apply(rebound.State with { Description = "GOOD" }));
        Assert.Equal(
            ReferenceState.Resolved,
            Find(diagnostics.Current, ReferenceRule.MessageKey, 0, 0, 1).State);
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void MissingWorldAndMessageInputsRemainUnverified()
    {
        CampaignMissionDocument document = Document(
            Targets(Row(Row(
                Row("description", "UNKNOWN"),
                Row("nodes", Row("unknown"))))));
        using var diagnostics = new CampaignReferenceDiagnostics(
            document,
            Bundle(worldAvailable: false, messagesAvailable: false));

        ReferenceSnapshot snapshot = diagnostics.Current;
        Assert.Equal(
            ReferenceState.Unverified,
            Find(snapshot, ReferenceRule.MessageKey, 0, 0, 1).State);
        Assert.Equal(
            ReferenceState.Unverified,
            Find(snapshot, ReferenceRule.TargetNode, 0, 1, 1, 0).State);
    }

    [Fact]
    public void EmptyPresentTargetsProduceAnIndexIssueWithoutChapterFallback()
    {
        ZrdrSet chapter = Scope(Targets(Row(Row(
            Row("nodes", Row("same"))))));
        CampaignMissionDocument document = Document(Targets(Row()));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(chapter: chapter));

        ReferenceSnapshot snapshot = diagnostics.Current;

        Assert.Contains(snapshot.Issues, issue => issue.Code == "target-index" && issue.BlocksReady);
        Assert.DoesNotContain(snapshot.References, reference => reference.Reference.Rule == ReferenceRule.TargetNode);
        Assert.True(snapshot.HasErrors);
    }

    private static CheckedReference Find(
        ReferenceSnapshot snapshot,
        ReferenceRule rule,
        params int[] path) =>
        Assert.Single(snapshot.References.Where(reference =>
            reference.Reference.Rule == rule
            && reference.Reference.Source.Path.SequenceEqual(path)));

    private static CampaignMissionTargets Bind(CampaignMissionDocument document, ZrdrSet chapter) =>
        CampaignMissionTargets.Bind(document, chapter, chapterAvailable: true);

    private static List<object?> Row(params object?[] values) => new(values);

    private static ZrdrFile Targets(List<object?> root) =>
        new("targets.zrd", null, root);

    private static ZrdrSet Scope(params ZrdrFile[] files) =>
        new("scope", files);

    private static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, Scope(files));

    private static CampaignMissionDependencyBundle Bundle(
        ZrdrSet? chapter = null,
        bool worldAvailable = true,
        bool messagesAvailable = true)
    {
        GameZ world = World();
        Messages messages = Messages.Of(new[] { (Key: "GOOD", Value: "Resolved text") });
        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            Mission,
            Dependency(Scope(), "shared", CampaignDependencyState.Ready),
            Dependency<GameZ?>(
                worldAvailable ? world : null,
                "world",
                worldAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable),
            Dependency(new TextureSet("textures", Array.Empty<TextureImage>()), "textures", CampaignDependencyState.Ready),
            Dependency(chapter ?? Scope(), "chapter", CampaignDependencyState.Ready),
            Dependency<AnimArchive?>(null, "chapter animations", CampaignDependencyState.Missing, optional: true),
            Dependency<AnimArchive?>(null, "mission animations", CampaignDependencyState.Missing, optional: true),
            Dependency(
                messages,
                "messages",
                messagesAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable,
                optional: true),
            Dependency<InterpScripts?>(null, "interp", CampaignDependencyState.Missing, optional: true),
            Dependency<MissionSetup?>(null, "setup", CampaignDependencyState.NotChecked, optional: true),
            null);
    }

    private static CampaignDependency<T> Dependency<T>(
        T value,
        string path,
        CampaignDependencyState state,
        bool optional = false) =>
        new(value, new CampaignDependencyAvailability(path, state, optional));

    private static GameZ World()
    {
        var world = new GameZ();
        world.Nodes.Add(new GameZNode { Index = 0, Name = "world", Kind = "World" });
        world.Nodes.Add(new GameZNode { Index = 1, Name = "parent", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 2, Name = "leaf", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 3, Name = "branch", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 4, Name = "leaf", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 5, Name = "same", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 6, Name = "parent", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 7, Name = "second_only", Kind = "Group" });
        world.Nodes[0].Children.AddRange(new[] { 1, 2, 5, 6 });
        world.Nodes[1].Children.Add(3);
        world.Nodes[3].Children.Add(4);
        world.Nodes[6].Children.Add(7);
        return world;
    }
}

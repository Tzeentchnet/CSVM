using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Mech3;
using CSVM.Tooling;
using Xunit;
using static CSVM.Tests.CampaignMissionObjectivesTests;

namespace CSVM.Tests;

/// <summary>Objective, path and message diagnostics follow the readers' exact selection rules.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignObjectiveReferenceTests
{
    [Fact]
    public void PathsAndIdentityReferencesKeepEachTargetAndConsumerSeparate()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row(
                "IDENTITY", Row("PRIMARY", 1f, "GOOD"),
                "IDENTITY", Row("SECONDARY", 2f, "GOOD2"),
                "INACTIVE1", Row("parent", Row("branch", "leaf")),
                "ADD_OBJECTIVE_TARGET", Row("bare", Row("parent", "leaf"), "loose"),
                "SET_HELP_LABEL", Row(Row("parent", "branch", "leaf"), "NOT_MESSAGE", "HELP"),
                "WAKE_OBJECTIVE_WHEN_I_COMPLETE", Row(2f, 2f, Row(999f)),
                "WAKE_OBJECTIVE", Row(99f),
                "TRAVELERS", Row("bare", "APPROACHING", "loose", 50f),
                "inactive2", Row("not_runtime"),
                "UNKNOWN", Row("IDENTITY", Row("PRIMARY", 9f, "not_a_message"))),
            "OBJECTIVE2", Row(),
            "OBJECTIVE4", Row(
                "IDENTITY", Row("PRIMARY", 4f, "AFTER"),
                "WAKE_OBJECTIVE", Row(99f)))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceSnapshot snapshot = diagnostics.Current;
        CheckedReference inactive = Find(snapshot, ReferenceRule.TargetNode, 0, 1, 5, 0);
        Assert.Equal(ReferenceState.Resolved, inactive.State);
        Assert.Equal(new[] { "parent", "branch", "leaf" }, inactive.Reference.Value.Segments);
        Assert.Equal(new[] { 0, 1, 5, 1, 0 }, inactive.Reference.Source.LeafPaths[1]);
        Assert.Equal(new[] { 0, 1, 5, 1, 1 }, inactive.Reference.Source.LeafPaths[2]);

        CheckedReference first = Find(snapshot, ReferenceRule.TargetSite, 0, 1, 7, 0);
        CheckedReference path = Find(snapshot, ReferenceRule.TargetSite, 0, 1, 7, 1, 0);
        CheckedReference third = Find(snapshot, ReferenceRule.TargetSite, 0, 1, 7, 2);
        Assert.Equal(ReferenceState.Resolved, first.State);
        Assert.Equal(ReferenceState.Resolved, path.State);
        Assert.Equal(ReferenceState.Resolved, third.State);
        Assert.Equal(new[] { "bare" }, first.Reference.Value.Segments);
        Assert.Equal(new[] { "parent", "leaf" }, path.Reference.Value.Segments);
        Assert.Equal(new[] { "loose" }, third.Reference.Value.Segments);
        Assert.Equal(new[] { 0, 1, 7, 1, 1 }, path.Reference.Source.LeafPaths[1]);
        CheckedReference helpTarget = Find(snapshot, ReferenceRule.TargetSite, 0, 1, 9, 0, 0);
        Assert.Equal(new[] { "parent", "branch", "leaf" }, helpTarget.Reference.Value.Segments);
        Assert.Equal(ReferenceState.Resolved, helpTarget.State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, ReferenceRule.MessageKey, 0, 1, 9, 2).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, ReferenceRule.MessageKey, 0, 1, 1, 2).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, ReferenceRule.MessageKey, 0, 1, 3, 2).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, ReferenceRule.MessageKey, 0, 5, 1, 2).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, ReferenceRule.ObjectiveNumber, 0, 1, 11, 0).State);
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, ReferenceRule.ObjectiveNumber, 0, 1, 11, 1).State);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, ReferenceRule.ObjectiveNumber, 0, 1, 13, 0).State);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, ReferenceRule.ObjectiveNumber, 0, 5, 3, 0).State);
        Assert.Contains(snapshot.Issues, issue => issue.Code == "objective-after-gap" && !issue.BlocksReady);
        Assert.DoesNotContain(snapshot.References, item => item.Reference.Value.Name is
            "PRIMARY" or "SECONDARY" or "NOT_MESSAGE" or "not_runtime" or "not_a_message");
        Assert.DoesNotContain(snapshot.References, item => item.Reference.Value.Number == 999);
        Assert.False(snapshot.HasErrors);
    }

    [Fact]
    public void BareFirstDirectiveSuppressesLaterLinksButBriefingStillReadsItsIdentity()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row(
                "IDENTITY",
                "IDENTITY", Row("PRIMARY", 1f, "GOOD"),
                "WAKE_OBJECTIVE_WHEN_I_COMPLETE",
                "WAKE_OBJECTIVE", Row(2f),
                "ADD_OTHER_TARGET",
                "ADD_OTHER_TARGET", Row("missing")),
            "OBJECTIVE2", null)));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceSnapshot snapshot = diagnostics.Current;
        Assert.Equal(ReferenceState.Resolved, Find(snapshot, ReferenceRule.MessageKey, 0, 1, 2, 2).State);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, ReferenceRule.ObjectiveNumber, 0, 1, 5, 0).State);
        Assert.Equal(ReferenceState.Suppressed, Find(snapshot, ReferenceRule.TargetSite, 0, 1, 8, 0).State);
        Assert.False(snapshot.HasErrors);
    }

    [Fact]
    public void EditedMessageAndScopedTargetProduceNewErrorsAndCanBeRepaired()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row(
                "IDENTITY", Row("PRIMARY", 1f, "GOOD"),
                "ADD_OBJECTIVE_TARGET", Row(Row("parent", "leaf"))),
            "OBJECTIVE2", null)));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.False(diagnostics.Current.HasErrors);
        CampaignMissionObjectives binding = CampaignMissionObjectives.Bind(document);
        CampaignObjectiveRow identity = RowOf(binding, 1, "IDENTITY");
        Assert.True(binding.Apply(identity, new object[] { "PRIMARY", 1f, "MISSING_MESSAGE" }));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, ReferenceRule.MessageKey, 0, 1, 1, 2).State);
        Assert.True(diagnostics.Current.HasErrors);

        binding = CampaignMissionObjectives.Bind(document);
        identity = RowOf(binding, 1, "IDENTITY");
        Assert.True(binding.Apply(identity, new object[] { "PRIMARY", 1f, "GOOD" }));
        binding = CampaignMissionObjectives.Bind(document);
        CampaignObjectiveRow target = RowOf(binding, 1, "ADD_OBJECTIVE_TARGET");
        Assert.True(binding.Apply(target, new object[] { "parent", "missing_leaf" }));
        Assert.Equal(ReferenceState.Missing, Find(diagnostics.Current, ReferenceRule.TargetSite, 0, 1, 3, 0, 0).State);
        Assert.True(diagnostics.Current.HasErrors);
        binding = CampaignMissionObjectives.Bind(document);
        target = RowOf(binding, 1, "ADD_OBJECTIVE_TARGET");
        Assert.True(binding.Apply(target, new object[] { "parent", "leaf" }));
        Assert.Equal(ReferenceState.Resolved, Find(diagnostics.Current, ReferenceRule.TargetSite, 0, 1, 3, 0, 0).State);
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void UnavailableWorldAndMessagesDoNotPretendToResolve()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row(
                "IDENTITY", Row("PRIMARY", 1f, "GOOD"),
                "INACTIVE1", Row("parent", "leaf"),
                "ADD_OTHER_TARGET", Row("bare")))));
        using var diagnostics = new CampaignReferenceDiagnostics(
            document, Bundle(worldAvailable: false, messagesAvailable: false));
        ReferenceSnapshot snapshot = diagnostics.Current;
        Assert.Equal(ReferenceState.Unverified, Find(snapshot, ReferenceRule.MessageKey, 0, 1, 1, 2).State);
        Assert.Equal(ReferenceState.Unverified, Find(snapshot, ReferenceRule.TargetNode, 0, 1, 3, 0).State);
        Assert.Equal(ReferenceState.Unverified, Find(snapshot, ReferenceRule.TargetSite, 0, 1, 5, 0).State);
    }

    [Fact]
    public void SlashesSplitRuntimeTargetKeysButNotInactivePathSegments()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row(
                "ADD_OBJECTIVE_TARGET", Row("parent/leaf", Row("parent/branch", "leaf")),
                "INACTIVE1", Row("parent/leaf")))));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        ReferenceSnapshot snapshot = diagnostics.Current;
        CheckedReference bare = Find(snapshot, ReferenceRule.TargetSite, 0, 1, 1, 0);
        CheckedReference nested = Find(snapshot, ReferenceRule.TargetSite, 0, 1, 1, 1, 0);
        CheckedReference inactive = Find(snapshot, ReferenceRule.TargetNode, 0, 1, 3, 0);
        Assert.Equal(new[] { "parent", "leaf" }, bare.Reference.Value.Segments);
        Assert.Equal(new[] { "parent", "branch", "leaf" }, nested.Reference.Value.Segments);
        Assert.Equal(2, nested.Reference.Source.LeafPaths.Length);
        Assert.Equal(ReferenceState.Resolved, bare.State);
        Assert.Equal(ReferenceState.Resolved, nested.State);
        Assert.Equal(new[] { "parent/leaf" }, inactive.Reference.Value.Segments);
        Assert.Equal(ReferenceState.InheritedMissing, inactive.State);
        Assert.False(snapshot.HasErrors);
    }

    [Fact]
    public void SwappingNumbersKeepsInheritedDangerReferencesAtTheirRawIdentity()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("missing_zone")),
            "OBJECTIVE2", Row())));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Equal(ReferenceState.InheritedMissing,
            Find(diagnostics.Current, ReferenceRule.DangerGeometry, 0, 1, 1, 0).State);
        Assert.True(CampaignMissionObjectives.Bind(document).SwapNumbers(1, 2));
        Assert.Equal(ReferenceState.InheritedMissing,
            Find(diagnostics.Current, ReferenceRule.DangerGeometry, 0, 1, 1, 0).State);
        Assert.False(diagnostics.Current.HasErrors);
    }

    private static CheckedReference Find(ReferenceSnapshot snapshot, ReferenceRule rule, params int[] path) =>
        Assert.Single(snapshot.References.Where(item =>
            item.Reference.Rule == rule && item.Reference.Source.Path.SequenceEqual(path)));

    private static CampaignMissionDependencyBundle Bundle(bool worldAvailable = true, bool messagesAvailable = true)
    {
        var world = new GameZ();
        world.Nodes.Add(new GameZNode { Index = 0, Name = "world", Kind = "World" });
        world.Nodes.Add(new GameZNode { Index = 1, Name = "parent", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 2, Name = "branch", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 3, Name = "leaf", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 4, Name = "bare", Kind = "Group" });
        world.Nodes.Add(new GameZNode { Index = 5, Name = "loose", Kind = "Group" });
        world.Nodes[0].Children.AddRange(new[] { 1, 4, 5 });
        world.Nodes[1].Children.Add(2);
        world.Nodes[2].Children.Add(3);
        var scope = new ZrdrSet("empty", Array.Empty<ZrdrFile>());
        Messages messages = Messages.Of(new[]
        {
            (Key: "GOOD", Value: "One"),
            (Key: "GOOD2", Value: "Two"),
            (Key: "HELP", Value: "Help"),
            (Key: "AFTER", Value: "After"),
        });
        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            Mission,
            Dependency(scope, "shared", CampaignDependencyState.Ready),
            Dependency<GameZ?>(worldAvailable ? world : null, "world",
                worldAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable),
            Dependency(new TextureSet("textures", Array.Empty<TextureImage>()), "textures", CampaignDependencyState.Ready),
            Dependency(scope, "chapter", CampaignDependencyState.Ready),
            Dependency<AnimArchive?>(null, "chapter animations", CampaignDependencyState.Missing, optional: true),
            Dependency<AnimArchive?>(null, "mission animations", CampaignDependencyState.Missing, optional: true),
            Dependency(messages, "messages",
                messagesAvailable ? CampaignDependencyState.Ready : CampaignDependencyState.Unavailable, optional: true),
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
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSVM.Mech3;
using CSVM.Session.Objectives;
using CSVM.Tooling;
using CSVM.UI.Menu;
using Xunit;

namespace CSVM.Tests;

/// <summary>Lossless objective bindings and the consumers of their edited reader.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionObjectivesTests
{
    internal static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void NumberingAndIdentityFollowBothRuntimeAndBriefingReaders()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE2", Row("IDENTITY", Row("SECONDARY", 3f, "B")),
            "OBJECTIVE1", Row(
                "IDENTITY", Row("PRIMARY", 1f, "A", "tail"),
                "identity", Row("SECONDARY", 1f, "LOWER"),
                "IDENTITY", Row("TERTIARY", 1f, "DUP"),
                "WAKE_OBJECTIVE", Row(2f),
                "WAKE_OBJECTIVE_WHEN_I_COMPLETE", Row(2f, 2f),
                "WAKE_OBJECTIVE_WHEN_I_COMPLETE", Row(99f),
                "UNKNOWN", Row("kept", -0f)),
            "OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", 2f, "BLOCK_DUP")),
            "OBJECTIVE4", Row("IDENTITY", Row("PRIMARY", 4f, "AFTER_GAP")))));
        CampaignMissionObjectives binding = CampaignMissionObjectives.Bind(document);
        Assert.Equal(new[] { 1, 2 }, binding.Entries.Select(entry => entry.Number));
        Assert.Equal(1, binding.Entries[0].DuplicateBlocks);
        Assert.Equal(new[] { "IDENTITY", "identity", "IDENTITY", "WAKE_OBJECTIVE",
                "WAKE_OBJECTIVE_WHEN_I_COMPLETE", "WAKE_OBJECTIVE_WHEN_I_COMPLETE", "UNKNOWN" },
            binding.Entries[0].Directives.Select(item => item.Key));
        Assert.True(binding.Entries[0].Directives[0].Selected);
        Assert.False(binding.Entries[0].Directives[2].Selected);
        Assert.False(binding.Entries[0].Directives[3].Selected);
        Assert.True(binding.Entries[0].Directives[4].Selected);
        Assert.False(binding.Entries[0].Directives[5].Selected);
        Assert.Empty(binding.Entries[0].Directives[6].Rows);

        ObjectiveScript script = Script(document);
        Assert.Equal(new[] { 2, 2 }, script.Objectives[0].WakeWhenComplete);
        Assert.Equal("A", script.Objectives[0].Identity!.Value.MessageKey);
        Assert.Equal(new[] { "A", "B" }, script.DisplayIdentities().Select(identity => identity.MessageKey));
        IReadOnlyList<BriefingObjective> briefing = BriefingObjectives.Load(document.Files[0].Root, new Messages());
        Assert.Equal(new[] { "A", "LOWER", "DUP", "BLOCK_DUP", "B", "AFTER_GAP" },
            briefing.Select(row => row.Key));
        Assert.Equal(new[] { 1, 1, 1, 1, 2, 4 }, briefing.Select(row => row.Number));
        Assert.False(document.IsDirty);
        Assert.Equal(0, document.Revision);
    }

    [Fact]
    public void BareFirstBlocksAndDirectivesSuppressLaterDuplicates()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", null,
            "OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", 1f, "NOTE")),
            "OBJECTIVE2", Row(
                "IDENTITY",
                "IDENTITY", Row("PRIMARY", 2f, "BRIEFING_ONLY"),
                "WAKE_OBJECTIVE_WHEN_I_COMPLETE",
                "WAKE_OBJECTIVE", Row(1f),
                "ADJUST_TIMER_WHEN_I_COMPLETE", null,
                "TIMER_ADJUST", Row("ADD", 5f)))));
        CampaignMissionObjectives binding = CampaignMissionObjectives.Bind(document);
        Assert.Equal(2, binding.Entries.Count);
        Assert.Empty(binding.Entries[0].Directives);
        Assert.Equal(1, binding.Entries[0].DuplicateBlocks);
        Assert.False(binding.Entries[1].Directives[1].Selected);
        Assert.False(binding.Entries[1].Directives[3].Selected);
        Assert.False(binding.Entries[1].Directives[5].Selected);
        ObjectiveScript script = Script(document);
        Assert.Null(script.Objectives[0].Identity);
        Assert.Null(script.Objectives[1].Identity);
        Assert.Empty(script.Objectives[1].WakeWhenComplete);
        Assert.Null(script.Objectives[1].AdjustTimer);
        Assert.Equal(new[] { "NOTE", "BRIEFING_ONLY" },
            BriefingObjectives.Load(document.Files[0].Root, new Messages()).Select(row => row.Key));
    }

    [Fact]
    public void TypedRowsEditExactLeavesAndValidateBeforeAnyMutation()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row(
                "IDENTITY", Row("PRIMARY", 1f, "OLD", "identity-tail"),
                "BEGIN_DORMANT", Row(-1f, 0f, -0f, subnormal, "timing-tail"),
                "INACTIVE1", Row("parent", Row("branch", "leaf"), null),
                "ADD_OBJECTIVE_TARGET", Row("bare", Row("parent", "branch", "leaf"), "other"),
                "SET_HELP_LABEL", Row(Row("parent", "branch", "leaf"), "unused-middle", "HELP"),
                "WAKE_OBJECTIVE_WHEN_I_COMPLETE", Row(2f, 2f, "ignored-tail"),
                "UNKNOWN", Row(-0f, subnormal, null, Row("nested", "keep"))),
            "OBJECTIVE2", null)));
        CampaignMissionObjectives binding = CampaignMissionObjectives.Bind(document);
        CampaignObjectiveRow identity = RowOf(binding, 1, "IDENTITY");
        Assert.Equal(new[] { 0, 1, 1, 0 }, identity.Fields[0].Path);
        int notifications = 0;
        document.Changed += () => notifications++;
        Assert.False(binding.Apply(identity, Values(binding, identity)));
        Assert.Throws<ArgumentException>(() => binding.Apply(identity,
            new object[] { "SECONDARY", 1.5f, "NEW" }));
        Assert.Equal("PRIMARY", binding.Value(identity.Fields[0]));
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.True(binding.Apply(identity, new object[] { "SECONDARY", 3f, "NEW" }));
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Throws<InvalidOperationException>(() => binding.Apply(identity, new object[] { "PRIMARY", 1f, "OLD" }));

        binding = CampaignMissionObjectives.Bind(document);
        CampaignObjectiveRow target = RowOf(binding, 1, "ADD_OBJECTIVE_TARGET", 1);
        Assert.Equal(3, target.Fields.Count);
        Assert.Throws<ArgumentException>(() => binding.Apply(target, new object[] { "new_parent", " ", "new_leaf" }));
        Assert.Equal("parent", binding.Value(target.Fields[0]));
        Assert.Equal(1, document.Revision);
        Assert.True(binding.Apply(target, new object[] { "new_parent", "new_branch", "new_leaf" }));

        binding = CampaignMissionObjectives.Bind(document);
        CampaignObjectiveRow help = RowOf(binding, 1, "SET_HELP_LABEL");
        Assert.Equal(4, help.Fields.Count);
        Assert.True(binding.Apply(help, new object[] { "parent", "branch", "leaf", "NEW_HELP" }));
        binding = CampaignMissionObjectives.Bind(document);
        CampaignObjectiveRow timing = RowOf(binding, 1, "BEGIN_DORMANT");
        Assert.True(binding.Apply(timing, new object[] { 0f, 0f, -0f, subnormal }));
        ObjectiveDef runtime = Script(document).Objectives[0];
        Assert.Equal(ObjectiveClass.Secondary, runtime.Identity!.Value.Class);
        Assert.Equal(3, runtime.Identity.Value.Priority);
        Assert.Equal("NEW", runtime.Identity.Value.MessageKey);
        Assert.True(runtime.BeginDormant);
        Assert.Equal(0f, runtime.DormantUntil);
        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(runtime.NapDuration!.Value));
        Assert.Equal(new[] { "bare", "new_parent/new_branch/new_leaf", "other" },
            runtime.AddObjectiveTarget.Select(item => item.Key));
        Assert.Equal(new[] { "parent", "branch", "leaf" }, runtime.Inactive[0]);
        Assert.Equal("parent/branch/leaf", Assert.Single(runtime.HelpLabel!.Value.Names).Key);
        Assert.Equal("NEW_HELP", runtime.HelpLabel.Value.MessageKey);
        Assert.Equal(new[] { 2, 2 }, runtime.WakeWhenComplete);
        Assert.Equal("NEW", Assert.Single(BriefingObjectives.Load(document.Files[0].Root, new Messages())).Key);
        Assert.Equal("identity-tail", document.ValueAt(0, new[] { 0, 1, 1, 3 }));
        Assert.Equal("timing-tail", document.ValueAt(0, new[] { 0, 1, 3, 4 }));
        Assert.Equal(0x80000000u, Bits(document, 0, 1, 3, 2));
        Assert.Equal(1u, Bits(document, 0, 1, 3, 3));
        Assert.Equal("ignored-tail", document.ValueAt(0, new[] { 0, 1, 11, 2 }));
        Assert.Equal(0x80000000u, Bits(document, 0, 1, 13, 0));
        Assert.Equal(1u, Bits(document, 0, 1, 13, 1));
        Assert.Null(document.ValueAt(0, new[] { 0, 1, 13, 2 }));
    }

    [Fact]
    public void AWholeRowMustValidateEvenWhenItsMalformedLeafIsUnchanged()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", 1.5f, "OLD")))));
        CampaignMissionObjectives binding = CampaignMissionObjectives.Bind(document);
        CampaignObjectiveRow row = RowOf(binding, 1, "IDENTITY");
        Assert.False(binding.Apply(row, Values(binding, row)));
        Assert.Throws<ArgumentException>(() => binding.Apply(row, new object[] { "SECONDARY", 1.5f, "NEW" }));
        Assert.Equal("PRIMARY", binding.Value(row.Fields[0]));
        Assert.Equal(1.5f, binding.Value(row.Fields[1]));
        Assert.Equal("OLD", binding.Value(row.Fields[2]));
        Assert.Equal(0, document.Revision);
        Assert.True(binding.Apply(row, new object[] { "SECONDARY", 1f, "NEW" }));
        Assert.Equal(1, document.Revision);
    }

    [Fact]
    public void SwapRemapsEveryKnownLinkAndDuplicateBlockInOneRevision()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "PLAYER_INIT", Row(1f, Row(0f, 0f, 0f), Row(0f, 0f, 0f), 1f, 10f),
            "RESTORE_ANIMS", null,
            "OBJECTIVE1", Row(
                "IDENTITY", Row("PRIMARY", 3f, "A"),
                "WAKE_OBJECTIVE_WHEN_I_COMPLETE", Row(2f, 2f, 3f),
                "WAKE_OBJECTIVE", Row(2f),
                "KILL_OBJECTIVE_WHEN_I_COMPLETE", Row(3f),
                "TICK_DEPENDS_ON_OBJ", Row(2f),
                "HIDE_OBJ", Row(3f),
                "NAP_OBJECTIVE_WHEN_I_COMPLETE", Row(2f, 0.3f)),
            "OBJECTIVE2", Row(
                "IDENTITY", Row("SECONDARY", 3f, "B"),
                "SLEEP_OBJECTIVE_WHEN_I_COMPLETE", Row(1f),
                "WAKE_OBJECTIVE_WHEN_I_SLEEP", Row(1f)),
            "OBJECTIVE1", Row("WAKE_OBJECTIVE", Row(2f)),
            "OBJECTIVE3", null)));
        CampaignMissionObjectives binding = CampaignMissionObjectives.Bind(document);
        int notifications = 0;
        document.Changed += () => notifications++;
        Assert.True(binding.SwapNumbers(1, 2));
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Throws<InvalidOperationException>(() => binding.SwapNumbers(1, 2));
        Assert.Equal("OBJECTIVE2", document.ValueAt(0, new[] { 0, 4 }));
        Assert.Equal("OBJECTIVE1", document.ValueAt(0, new[] { 0, 6 }));
        Assert.Equal("OBJECTIVE2", document.ValueAt(0, new[] { 0, 8 }));
        Assert.Equal(1f, document.ValueAt(0, new[] { 0, 5, 5, 0 }));
        Assert.Equal(1f, document.ValueAt(0, new[] { 0, 9, 1, 0 }));

        ObjectiveScript script = Script(document);
        Assert.Equal("B", script.Objectives[0].Identity!.Value.MessageKey);
        Assert.Equal("A", script.Objectives[1].Identity!.Value.MessageKey);
        Assert.Equal(new[] { 1, 1, 3 }, script.Objectives[1].WakeWhenComplete);
        Assert.Equal(1, script.Objectives[1].TickDependsOn);
        Assert.Equal(3, script.Objectives[1].HideObj);
        Assert.Equal(1, script.Objectives[1].NapWhenComplete!.Value.Target);
        Assert.Equal(new[] { 2 }, script.Objectives[0].SleepWhenComplete);
        Assert.Equal(new[] { 2 }, script.Objectives[0].WakeWhenSleep);
        Assert.Equal("B", Assert.Single(script.DisplayIdentities()).MessageKey);
        IReadOnlyList<BriefingObjective> briefing = BriefingObjectives.Load(document.Files[0].Root, new Messages());
        Assert.Equal(new[] { "A", "B" }, briefing.Select(row => row.Key));
        Assert.Equal(new[] { 2, 1 }, briefing.Select(row => row.Number));
    }

    [Fact]
    public void SwapPreservesDisabledOptionalObjectiveLinks()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row("TICK_DEPENDS_ON_OBJ", Row(0f), "HIDE_OBJ", Row(0f)),
            "OBJECTIVE2", Row())));
        Assert.True(CampaignMissionObjectives.Bind(document).SwapNumbers(1, 2));
        ObjectiveDef moved = Script(document).ByNumber(2)!;
        Assert.Equal(0, moved.TickDependsOn);
        Assert.Equal(0, moved.HideObj);
    }

    [Fact]
    public void RawEditorCannotRenumberOrCreateNumberedBlocksOneLeafAtATime()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row(), "OBJECTIVE2", Row(), "FLAG", null, "objective03", null)));
        int notifications = 0;
        document.Changed += () => notifications++;
        Assert.False(document.SetLeaf(0, new[] { 0, 0 }, "OBJECTIVE1"));
        Assert.Throws<InvalidOperationException>(() => document.SetLeaf(0, new[] { 0, 0 }, "OBJECTIVE2"));
        Assert.Throws<InvalidOperationException>(() => document.SetLeaf(0, new[] { 0, 4 }, "OBJECTIVE3"));
        Assert.Throws<InvalidOperationException>(() => document.SetLeaf(0, new[] { 0, 6 }, "OTHER"));
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.False(document.IsDirty);
        Assert.True(document.SetLeaf(0, new[] { 0, 4 }, "OTHER_FLAG"));
    }

    [Fact]
    public void UnsafeRenumberingAndInvalidBatchesLeaveEveryLeafAndRevisionUntouched()
    {
        foreach (List<object?> body in new[]
        {
            Row("OBJECTIVE1", Row("WAKE_OBJECTIVE", Row(2f)), "OBJECTIVE2", Row("UNKNOWN", Row(1f))),
            Row("OBJECTIVE1", Row(), "OBJECTIVE2", Row(), "OBJECTIVE4", Row()),
            Row("OBJECTIVE1", Row("WAKE_OBJECTIVE", Row(2f, "malformed")), "OBJECTIVE2", Row()),
            Row("OBJECTIVE1", Row("NAP_OBJECTIVE_WHEN_I_COMPLETE", Row(3f)), "OBJECTIVE2", Row()),
            Row("OBJECTIVE1", Row("NAP_OBJECTIVE_WHEN_I_COMPLETE", Row(2f, float.NaN)), "OBJECTIVE2", Row()),
            Row("OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", 1.5f, "KEY")), "OBJECTIVE2", Row()),
            Row("OBJECTIVE1", Row("IDENTITY", Row(5f, 1f, "KEY")), "OBJECTIVE2", Row()),
            Row("OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", 1f, "KEY"),
                "IDENTITY", Row("SECONDARY", 3.5f, "DUP")), "OBJECTIVE2", Row()),
            Row("OBJECTIVE1", Row("ANIM_STATE", Row("extra", Row("WAKE_OBJECTIVE", Row(2f)))),
                "OBJECTIVE2", Row()),
            Row("OBJECTIVE1", Row(), "OBJECTIVE2", Row(), "objective2", Row()),
            Row("OBJECTIVE1", Row(), "OBJECTIVE2", Row(), "COUNTER_FILE_KEY", Row()),
        })
        {
            CampaignMissionDocument document = Document(Reader(body));
            List<object?> before = CampaignMissionDocument.Copy(document.Files[0].Root);
            CampaignMissionObjectives binding = CampaignMissionObjectives.Bind(document);
            int notifications = 0;
            document.Changed += () => notifications++;
            Assert.Throws<InvalidOperationException>(() => binding.SwapNumbers(1, 2));
            SameTree(before, document.Files[0].Root);
            Assert.Equal(0, document.Revision);
            Assert.Equal(0, notifications);
            Assert.False(document.IsDirty);
        }

        CampaignMissionDocument batch = Document(Reader(Row(
            "OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", 1f, "A")),
            "OBJECTIVE2", null)));
        Assert.Throws<InvalidOperationException>(() => batch.SetObjectiveLeaves(0, 0, new[]
        {
            ((IReadOnlyList<int>)new[] { 0, 0 }, (object)"OBJECTIVE2"),
            ((IReadOnlyList<int>)new[] { 0, 1, 1, 1 }, (object)"wrong-type"),
        }));
        Assert.Throws<InvalidOperationException>(() => batch.SetObjectiveLeaves(0, 0, new[]
        {
            ((IReadOnlyList<int>)new[] { 0, 0 }, (object)"OBJECTIVE2"),
            ((IReadOnlyList<int>)new[] { 0, 0 }, (object)"OBJECTIVE3"),
        }));
        Assert.Equal("OBJECTIVE1", batch.ValueAt(0, new[] { 0, 0 }));
        Assert.Equal(0, batch.Revision);
    }

    [Fact]
    public void MalformedRootsAndDirectiveShapesRemainRaw()
    {
        foreach (List<object?> root in new[] { Row(), Row((object?)null), Row("not-a-list") })
        {
            CampaignMissionDocument document = Document(new ZrdrFile("objectives.zrd", null, root));
            CampaignMissionObjectives binding = CampaignMissionObjectives.Bind(document);
            Assert.Empty(binding.Entries);
            Assert.NotEmpty(binding.Problem);
            Assert.Empty(Script(document).Objectives);
        }

        CampaignMissionDocument malformed = Document(Reader(Row(
            "OBJECTIVE1", Row(
                "IDENTITY", Row(5f, "bad", "KEY"),
                "SET_HELP_LABEL", Row(Row(2f), "KEY"),
                "INACTIVE1", Row(null, 2f),
                "WAKE_OBJECTIVE", Row(Row(2f)),
                "UNKNOWN", Row("value")),
            "OBJECTIVE2", null)));
        CampaignMissionObjectives view = CampaignMissionObjectives.Bind(malformed);
        Assert.All(view.Entries[0].Directives, directive => Assert.Empty(directive.Rows));
        Assert.Throws<InvalidOperationException>(() => view.SwapNumbers(1, 2));
        Assert.Equal(0, malformed.Revision);

        CampaignMissionDocument nonfinite = Document(Reader(Row(
            "OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", float.NaN, "KEY")))));
        Assert.Empty(CampaignMissionObjectives.Bind(nonfinite).Entries[0].Directives[0].Rows);
    }

    [Fact]
    public void NoEditExportRoundTripsTheCompleteReaderAndFloatBits()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument document = Document(
            Reader(Row(
                "OBJECTIVE1", Row(
                    "IDENTITY", Row("PRIMARY", 1f, "A"),
                    "UNKNOWN", Row(null, -0f, subnormal, Row("nested", Row("untouched"))),
                    "WAKE_OBJECTIVE", Row(2f, 2f)),
                "OBJECTIVE1", null,
                "OBJECTIVE2", Row("UNSUPPORTED", Row("tail")))),
            new ZrdrFile("other.zrd", null, Row("kept", Row(-0f))));
        List<object?> before = CampaignMissionDocument.Copy(document.Files[0].Root);
        _ = CampaignMissionObjectives.Bind(document);
        Assert.False(document.IsDirty);
        Assert.Equal(0, document.Revision);

        string folder = Path.Combine(".scratch", "objective-editor-" + Guid.NewGuid().ToString("N"));
        try
        {
            string output = document.Export(folder);
            ZrdrSet reopened = ZrdrSet.FromExtracted(output);
            Assert.Equal(new[] { "objectives.zrd", "other.zrd" }, reopened.Files.Select(file => file.Key));
            SameTree(before, reopened.LoadFile("objectives.json"));
            SameTree(document.Files[1].Root, reopened.LoadFile("other.json"));
            Assert.False(document.IsDirty);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void SwappingChangesGraphOrderWhilePreservingAuthoredIdentities()
    {
        CampaignMissionDocument document = Document(Reader(Row(
            "OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", 3f, "A"),
                "ADD_OBJECTIVE_TARGET", Row("first")),
            "OBJECTIVE2", Row("IDENTITY", Row("SECONDARY", 3f, "B"),
                "ADD_OBJECTIVE_TARGET", Row("second")))));
        var before = new ObjectiveGraph(Script(document), new FakeWorld());
        before.Step(0.1f);
        Assert.Contains("first", before.ObjectiveTargets);
        Assert.DoesNotContain("second", before.ObjectiveTargets);
        Assert.Equal("A", Assert.Single(before.Rows).MessageKey);
        Assert.Equal(1 << 3, before.CompletedMask);

        Assert.True(CampaignMissionObjectives.Bind(document).SwapNumbers(1, 2));
        var after = new ObjectiveGraph(Script(document), new FakeWorld());
        after.Step(0.1f);
        Assert.Contains("second", after.ObjectiveTargets);
        Assert.DoesNotContain("first", after.ObjectiveTargets);
        Assert.Equal("B", Assert.Single(after.Rows).MessageKey);
        Assert.Equal(1 << 3, after.CompletedMask);
    }

    internal static List<object?> Row(params object?[] values) => new(values);

    internal static ZrdrFile Reader(List<object?> body) => new("objectives.zrd", null, Row(body));

    internal static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, new ZrdrSet("invented", files));

    internal static ObjectiveScript Script(CampaignMissionDocument document) =>
        ObjectiveScript.Parse(document.Files[0].Root);

    internal static CampaignObjectiveRow RowOf(
        CampaignMissionObjectives binding,
        int number,
        string directive,
        int index = 0) =>
        Assert.Single(binding.Entries[number - 1].Directives.Where(item => item.Key == directive)).Rows[index];

    internal static object[] Values(CampaignMissionObjectives binding, CampaignObjectiveRow row) =>
        row.Fields.Select(binding.Value).ToArray();

    private static uint Bits(CampaignMissionDocument document, params int[] path) =>
        BitConverter.SingleToUInt32Bits(Assert.IsType<float>(document.ValueAt(0, path)));

    private static void SameTree(object? expected, object? actual)
    {
        if (expected is List<object?> list)
        {
            List<object?> other = Assert.IsType<List<object?>>(actual);
            Assert.Equal(list.Count, other.Count);
            for (int i = 0; i < list.Count; i++)
            {
                SameTree(list[i], other[i]);
            }
        }
        else if (expected is float number)
        {
            Assert.Equal(BitConverter.SingleToUInt32Bits(number),
                BitConverter.SingleToUInt32Bits(Assert.IsType<float>(actual)));
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }

    private sealed class FakeWorld : IObjectiveWorld
    {
        public bool? NodeInactive(IReadOnlyList<string> path) => false;
        public int AnimState(string anim) => 0;
        public int? GroupLiveCount(int group, string? generator) => 0;
        public void WidenGroupEngagement(int group) { }
        public bool? TravelersMet(TravelersSpec spec) => false;
        public void WakeupEnemies(IReadOnlyList<string> names) { }
        public void WakeupTurrets(IReadOnlyList<string> patterns) { }
        public void WakeupZepTurrets(IReadOnlyList<string> nodes) { }
        public void WakeupGenerator(string name, int count) { }
        public void WakeAnim(string anim, string? node) { }
        public void PlaySoundGroup(string group) { }
        public void StopQueuedSounds(IReadOnlyList<string> names) { }
        public void WarpVehicle(string vehicle, IReadOnlyList<WarpPoint> points) { }
        public void SetAiTeam(IReadOnlyList<(string Name, int Team)> entries) { }
        public void SetAiNet(IReadOnlyList<(string Name, string Net)> entries) { }
        public void SetAiAttackRadius(IReadOnlyList<(string Name, float Radius)> entries) { }
        public void CompletedZepcannons(IReadOnlyList<(string Zeppelin, int Flag)> entries) { }
        public void CompletedStoppoint(IReadOnlyList<(string Net, int Stop, int Flag)> entries) { }
        public void StartTaxi(IReadOnlyList<string> names) { }
    }
}

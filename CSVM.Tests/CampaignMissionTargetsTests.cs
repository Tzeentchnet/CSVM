using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSVM.Flight.Weapons;
using CSVM.Mech3;
using CSVM.Session.Objectives;
using CSVM.Tooling;
using Xunit;

namespace CSVM.Tests;

/// <summary>Exact target-entry editing against invented mission and chapter readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionTargetsTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void ScanFollowsLastPairsAndLastCaseInsensitiveNodeWithoutLosingPaths()
    {
        var first = Row(
            Row("description", "FIRST"),
            Row("DESCRIPTION", "WINNER"),
            Row("nodes", Row("old_node")),
            Row("NODES", Row(Row("parent/branch", "leaf"), "duplicate")),
            Row("objective"),
            Row("OTHER_TARGET", null));
        var second = Row(
            Row("description", "SECOND"),
            Row("nodes", Row("DUPLICATE")));
        CampaignMissionDocument document = Document(
            Targets(Row(first, null, second)));

        CampaignMissionTargets binding = Bind(document);
        CampaignTargetEntry a = binding.Entries[0];
        CampaignTargetEntry b = binding.Entries[1];
        MissionTargets runtime = Load(document);

        Assert.Equal(CampaignTargetOrigin.Mission, binding.Origin);
        Assert.Equal(new[] { 0, 2 }, binding.Entries.Select(entry => entry.RawIndex));
        Assert.Equal(new[] { 0, 1, 1 }, a.LabelPath(CampaignTargetLabel.Description));
        Assert.Equal("WINNER", a.State.Description);
        Assert.True(a.State.Objective && a.State.OtherTarget);
        Assert.False(a.Nodes[0].Selected);
        Assert.False(a.Nodes[0].Effective);
        Assert.Equal("parent/branch/leaf", a.Nodes[1].Key);
        Assert.Equal(new[] { "parent", "branch", "leaf" }, a.Nodes[1].Segments);
        Assert.Equal(new[] { 0, 3, 1, 0 }, a.Nodes[1].Path);
        Assert.Equal(
            new[] { 0, 3, 1, 0, 0 },
            a.Nodes[1].LeafPaths[0]);
        Assert.Equal(
            new[] { 0, 3, 1, 0, 1 },
            a.Nodes[1].LeafPaths[1]);
        Assert.True(a.Nodes[1].Effective);
        Assert.True(a.Nodes[2].Selected);
        Assert.False(a.Nodes[2].Effective);
        Assert.True(Assert.Single(b.Nodes).Effective);
        Assert.Equal("WINNER", runtime.For("PARENT/BRANCH/LEAF").Description);
        Assert.Equal("SECOND", runtime.For("duplicate").Description);
        Assert.Null(runtime.For("old_node").Description);
    }

    [Fact]
    public void TypedEditChangesOnlySelectedValuesAndPreservesUnknownData()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        var target = Row(
            Row("description", "EARLY", "first-tail"),
            Row("DESCRIPTION", "CURRENT", "selected-tail"),
            Row("nodes", Row(Row("root", "leaf"), "sibling"), "nodes-tail"),
            Row("unknown", Row(-0f, subnormal, null, "keep")),
            Row("objective"),
            Row("OBJECTIVE", null),
            Row("other_target", null),
            Row("help_label", "HELP", "help-tail"));
        CampaignMissionDocument document = Document(
            Targets(Row(target)),
            new ZrdrFile("other.zrd", null, Row("untouched")));
        int notifications = 0;
        document.Changed += () => notifications++;
        CampaignTargetEntry entry = Assert.Single(Bind(document).Entries);
        Assert.False(entry.Apply(entry.State));

        Assert.True(entry.Apply(entry.State with
        {
            Description = "EDITED",
            Category = "CATEGORY",
            Objective = false,
        }));

        Assert.True(document.IsDirty);
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Equal("EARLY", document.ValueAt(0, new[] { 0, 0, 1 }));
        Assert.Equal("first-tail", document.ValueAt(0, new[] { 0, 0, 2 }));
        Assert.Equal("EDITED", document.ValueAt(0, new[] { 0, 1, 1 }));
        Assert.Equal("selected-tail", document.ValueAt(0, new[] { 0, 1, 2 }));
        Assert.Equal("nodes-tail", document.ValueAt(0, new[] { 0, 2, 2 }));
        Assert.Equal(
            0x80000000u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(document.ValueAt(0, new[] { 0, 3, 1, 0 }))));
        Assert.Equal(
            1u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(document.ValueAt(0, new[] { 0, 3, 1, 1 }))));
        Assert.Null(document.ValueAt(0, new[] { 0, 3, 1, 2 }));
        Assert.Equal("help-tail", document.ValueAt(0, new[] { 0, 5, 2 }));
        Assert.Equal("untouched", document.ValueAt(1, new[] { 0 }));
        Assert.Equal("CATEGORY", document.ValueAt(0, new[] { 0, 6, 1 }));

        MissionTarget actual = Load(document).For("ROOT/LEAF");
        Assert.Equal(new MissionTarget("EDITED", "CATEGORY", "HELP", false, true), actual);
        Assert.Equal(actual, Load(document).For("sibling"));
        Assert.Throws<InvalidOperationException>(() => entry.Apply(entry.State));
    }

    [Fact]
    public void FlagAdditionAndLabelClearingMatchRuntime()
    {
        CampaignMissionDocument document = Document(
            Targets(Row(Row(
                Row("nodes", Row("site")),
                Row("description", "VISIBLE")))));
        CampaignTargetEntry entry = Assert.Single(Bind(document).Entries);

        Assert.True(entry.Apply(entry.State with
        {
            Description = "",
            Objective = true,
            OtherTarget = true,
        }));

        CampaignTargetEntry rebound = Assert.Single(Bind(document).Entries);
        Assert.False(rebound.Apply(rebound.State));
        MissionTarget runtime = Load(document).For("site");
        Assert.Equal("", runtime.Description);
        Assert.Null(runtime.CategoryLabel);
        Assert.Null(runtime.HelpLabel);
        Assert.True(runtime.Objective && runtime.OtherTarget);
        var flagged = new List<string>();
        ObjectiveSites.CollectFlagged(TargetFlag.Objective, null, null, Load(document), flagged);
        ObjectiveSites.CollectFlagged(TargetFlag.OtherTarget, null, null, Load(document), flagged);
        Assert.Equal(new[] { "site" }, flagged);
    }

    [Fact]
    public void ValidationRejectsAllEditsBeforeMutatingTheEntry()
    {
        var target = Row(
            Row("description", "ORIGINAL"),
            Row("nodes", Row("site")),
            Row("objective", "unknown payload"));
        CampaignMissionDocument document = Document(Targets(Row(target)));
        CampaignTargetEntry entry = Assert.Single(Bind(document).Entries);
        CampaignTargetState before = entry.State;
        int notifications = 0;
        document.Changed += () => notifications++;

        Assert.Throws<InvalidOperationException>(() => entry.Apply(before with
        {
            Description = "CHANGED",
            Objective = false,
        }));
        Assert.Throws<ArgumentException>(() => entry.Apply(before with
        {
            Description = " BAD ",
            OtherTarget = true,
        }));
        Assert.Throws<ArgumentException>(() => entry.Apply(before with
        {
            Description = "BAD\nKEY",
        }));
        Assert.Throws<InvalidOperationException>(() => entry.Apply(before with
        {
            Description = null,
        }));

        Assert.Equal(before, entry.State);
        Assert.Equal("ORIGINAL", document.ValueAt(0, new[] { 0, 0, 1 }));
        Assert.Equal("unknown payload", document.ValueAt(0, new[] { 0, 2, 1 }));
        Assert.False(document.IsDirty);
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void MalformedLastPairsSuppressEarlierValuesAndStayRaw()
    {
        var target = Row(
            Row("description", "EARLY"),
            Row("description", null, "tail"),
            Row("nodes", Row("valid")));
        var ignored = Row(
            Row("nodes", Row("old")),
            Row("nodes", 17f));
        CampaignMissionDocument document = Document(Targets(Row(target, ignored)));
        CampaignMissionTargets binding = Bind(document);
        CampaignTargetEntry first = binding.Entries[0];
        CampaignTargetEntry second = binding.Entries[1];

        Assert.Null(first.State.Description);
        Assert.False(first.LabelWellFormed(CampaignTargetLabel.Description));
        Assert.False(first.CanEditLabel(CampaignTargetLabel.Description));
        Assert.True(first.CanEdit);
        Assert.False(second.CanEdit);
        Assert.Throws<InvalidOperationException>(
            () => first.Apply(first.State with { Description = "REPLACEMENT" }));
        Assert.Throws<InvalidOperationException>(
            () => second.Apply(second.State with { Objective = true }));
        Assert.True(first.Apply(first.State with { Objective = true }));

        Assert.Null(document.ValueAt(0, new[] { 0, 1, 1 }));
        Assert.Equal("tail", document.ValueAt(0, new[] { 0, 1, 2 }));
        Assert.Null(Load(document).For("valid").Description);
        Assert.True(Load(document).For("valid").Objective);
        Assert.False(Load(document).ByNode.ContainsKey("old"));
    }

    [Fact]
    public void MissingUnavailableAndEmptyReadersKeepTheirRuntimeDistinctions()
    {
        ZrdrSet validChapter = Scope(Targets(Row(Row(Row("nodes", Row("inherited"))))));
        CampaignMissionDocument noTargets = Document(
            new ZrdrFile("other.zrd", null, Row("keep")));
        Assert.Equal(CampaignTargetOrigin.None, Bind(noTargets).Origin);
        Assert.Equal(
            CampaignTargetOrigin.Unavailable,
            CampaignMissionTargets.Bind(noTargets, ZrdrSet.None, chapterAvailable: false).Origin);
        Assert.Equal(0, MissionTargets.Load(Snapshot(noTargets), Scope()).Count);

        CampaignMissionDocument emptyLocal = Document(Targets(Row()));
        CampaignMissionTargets invalid = Bind(emptyLocal, validChapter);
        Assert.Equal(CampaignTargetOrigin.Invalid, invalid.Origin);
        Assert.False(invalid.CanCopy);
        Assert.Throws<InvalidDataException>(
            () => MissionTargets.Load(Snapshot(emptyLocal), validChapter));

        ZrdrSet emptyChapter = Scope(Targets(Row()));
        Assert.Equal(CampaignTargetOrigin.Invalid, Bind(noTargets, emptyChapter).Origin);
        Assert.Throws<InvalidDataException>(
            () => MissionTargets.Load(Snapshot(noTargets), emptyChapter));
    }

    [Fact]
    public void LocalReaderWinsWholeEvenWhenChapterHasMatchingNames()
    {
        CampaignMissionDocument document = Document(
            Targets(Row(Row(Row("nodes", Row("local"))))));
        ZrdrSet chapter = Scope(
            Targets(Row(Row(Row("nodes", Row("chapter"))))));
        CampaignMissionTargets binding = Bind(document, chapter);

        Assert.Equal(CampaignTargetOrigin.Mission, binding.Origin);
        Assert.False(binding.CanCopy);
        Assert.Throws<InvalidOperationException>(() => binding.CopyToMission());
        MissionTargets runtime = MissionTargets.Load(Snapshot(document), chapter);
        Assert.True(runtime.ByNode.ContainsKey("local"));
        Assert.False(runtime.ByNode.ContainsKey("chapter"));

        CampaignMissionTargets localWithoutChapter = CampaignMissionTargets.Bind(
            document, ZrdrSet.None, chapterAvailable: false);
        Assert.Equal(CampaignTargetOrigin.Mission, localWithoutChapter.Origin);
        Assert.False(localWithoutChapter.CanCopy);
        Assert.True(MissionTargets.Load(Snapshot(document), ZrdrSet.None).ByNode.ContainsKey("local"));
    }

    [Fact]
    public void UnsupportedBooleanTailSurvivesNoOpEditAndCopy()
    {
        List<object?> source = Row(Row(
            Row("description", "BEFORE"),
            Row("unknown", true),
            Row("nodes", Row("site"))));
        CampaignMissionDocument document = Document(Targets(source));
        CampaignTargetEntry entry = Assert.Single(Bind(document).Entries);

        Assert.False(entry.Apply(entry.State));
        Assert.False(document.IsDirty);
        Assert.True(entry.Apply(entry.State with { Description = "AFTER" }));
        var saved = Assert.IsType<List<object?>>(document.Files[0].Root[0]);
        Assert.True(Assert.IsType<bool>(Assert.IsType<List<object?>>(saved[1])[1]));

        CampaignMissionDocument inheritedDocument = Document();
        CampaignMissionTargets inherited = Bind(inheritedDocument, Scope(Targets(source)));
        int copied = inherited.CopyToMission();
        var copiedEntry = Assert.IsType<List<object?>>(inheritedDocument.Files[copied].Root[0]);
        Assert.True(Assert.IsType<bool>(Assert.IsType<List<object?>>(copiedEntry[1])[1]));
    }

    [Fact]
    public void ChapterIsReadOnlyUntilAnExplicitDeepCopyAndCompleteExport()
    {
        List<object?> source = Row(Row(
            Row("description", "CHAPTER"),
            Row("nodes", Row("site")),
            Row("objective"),
            Row("unknown", Row(-0f, null, "keep"))));
        ZrdrSet chapter = Scope(Targets(source));
        CampaignMissionDocument document = Document(
            new ZrdrFile("other.zrd", null, Row("retained")));
        CampaignMissionTargets inherited = Bind(document, chapter);
        CampaignTargetEntry inheritedEntry = Assert.Single(inherited.Entries);
        int notifications = 0;
        document.Changed += () => notifications++;

        Assert.Equal(CampaignTargetOrigin.Chapter, inherited.Origin);
        Assert.False(inheritedEntry.CanEdit);
        Assert.Throws<InvalidOperationException>(
            () => inheritedEntry.Apply(inheritedEntry.State with { Description = "MUTATED" }));
        Assert.Equal(0, document.Revision);
        Assert.Equal(1, inherited.CopyToMission());
        Assert.Equal(1, document.Revision);
        Assert.True(document.IsDirty);
        Assert.Equal(1, notifications);
        Assert.Throws<InvalidOperationException>(() => inherited.CopyToMission());

        CampaignTargetEntry local = Assert.Single(Bind(document, chapter).Entries);
        Assert.True(local.Apply(local.State with
        {
            Description = "LOCAL",
            OtherTarget = true,
        }));
        Assert.Equal("CHAPTER", ((List<object?>)((List<object?>)source[0]!)[0]!)[1]);
        Assert.Equal("CHAPTER", MissionTargets.Load(Scope(), chapter).For("site").Description);
        Assert.Equal("LOCAL", MissionTargets.Load(Snapshot(document), chapter).For("site").Description);

        string root = Path.Combine(
            TestData.RepoRoot,
            ".scratch",
            $"c21-export-{Guid.NewGuid():N}");
        try
        {
            string archive = document.Export(root);
            ZrdrSet reopened = ZrdrSet.FromExtracted(archive);
            Assert.Equal(new[] { "other.zrd", "targets.zrd" }, reopened.Files.Select(file => file.Key));
            Assert.Equal("retained", reopened.LoadFile("other.json")[0]);
            MissionTarget exported = MissionTargets.Load(reopened, chapter).For("site");
            Assert.Equal("LOCAL", exported.Description);
            Assert.True(exported.Objective && exported.OtherTarget);
            var unknown = Assert.IsType<List<object?>>(
                Assert.IsType<List<object?>>(
                    Assert.IsType<List<object?>>(reopened.Files[1].Root[0])[3])[1]);
            Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(Assert.IsType<float>(unknown[0])));
            Assert.Null(unknown[1]);
            Assert.Equal("keep", unknown[2]);
            Assert.False(document.IsDirty);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void StaleBindingsAndChangedChapterSnapshotsCannotMutate()
    {
        CampaignMissionDocument document = Document(
            new ZrdrFile("other.zrd", null, Row("before")),
            Targets(Row(Row(Row("nodes", Row("site"))))));
        CampaignTargetEntry stale = Assert.Single(Bind(document).Entries);
        Assert.True(document.SetLeaf(0, new[] { 0 }, "after"));
        Assert.Throws<InvalidOperationException>(
            () => stale.Apply(stale.State with { Objective = true }));
        Assert.Equal(1, document.Revision);
        Assert.False(Load(document).For("site").Objective);

        CampaignMissionDocument inheritedDocument = Document(
            new ZrdrFile("other.zrd", null, Row("before")));
        List<object?> chapterRoot = Row(Row(
            Row("description", "before"),
            Row("nodes", Row("site"))));
        ZrdrSet chapter = Scope(Targets(chapterRoot));
        CampaignMissionTargets inherited = Bind(inheritedDocument, chapter);
        ((List<object?>)((List<object?>)chapterRoot[0]!)[0]!)[1] = "changed";
        Assert.Throws<InvalidOperationException>(() => inherited.CopyToMission());
        Assert.False(inheritedDocument.IsDirty);
        Assert.Equal(-1, inheritedDocument.FindFileIndex("targets.json"));

        CampaignMissionTargets staleCopy = Bind(inheritedDocument, chapter);
        Assert.True(inheritedDocument.SetLeaf(0, new[] { 0 }, "after"));
        Assert.Throws<InvalidOperationException>(() => staleCopy.CopyToMission());
        Assert.Equal(-1, inheritedDocument.FindFileIndex("targets.json"));
    }

    private static List<object?> Row(params object?[] values) => new(values);

    private static ZrdrFile Targets(List<object?> root) =>
        new("targets.zrd", null, root);

    private static ZrdrSet Scope(params ZrdrFile[] files) =>
        new("scope", files);

    private static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, Scope(files));

    private static ZrdrSet Snapshot(CampaignMissionDocument document) =>
        new("mission", document.Files);

    private static MissionTargets Load(CampaignMissionDocument document) =>
        MissionTargets.Load(Snapshot(document));

    private static CampaignMissionTargets Bind(
        CampaignMissionDocument document,
        ZrdrSet? chapter = null) =>
        CampaignMissionTargets.Bind(document, chapter ?? Scope(), chapterAvailable: true);
}

/// <summary>Retail target origins and runtime-selected rows through the installed game source.</summary>
[Trait("Tier", "Data")]
public sealed class CampaignMissionTargetCensusTests
{
    [GameFilesFact]
    public void StoryMissionsUseTwentyThreeLocalTablesAndOneChapterTable()
    {
        var source = GameFilesData.Source!;
        IReadOnlyList<CampaignMission> missions = CampaignMissionDocument.LoadMissions(source);
        int local = 0;
        int inherited = 0;
        foreach (CampaignMission mission in missions)
        {
            CampaignMissionDocument document = CampaignMissionDocument.Load(source, mission);
            ZrdrSet chapter = ZrdrSet.Load(source, $"ZBD/{mission.ChapterFolder}/zrdr.zbd");
            Assert.True(chapter.Exists);
            CampaignMissionTargets binding =
                CampaignMissionTargets.Bind(document, chapter, chapterAvailable: true);
            MissionTargets runtime = MissionTargets.Load(new ZrdrSet("mission", document.Files), chapter);
            if (binding.Origin == CampaignTargetOrigin.Mission)
            {
                local++;
            }
            else
            {
                Assert.Equal(CampaignTargetOrigin.Chapter, binding.Origin);
                Assert.Equal("c1c", mission.ChapterFolder);
                Assert.Equal("m01", mission.MissionFolder);
                inherited++;
            }

            var winners = binding.Entries
                .SelectMany(entry => entry.Nodes
                    .Where(node => node.Effective)
                    .Select(node => (Entry: entry, Node: node)))
                .ToArray();
            Assert.Equal(runtime.Count, winners.Length);
            foreach ((CampaignTargetEntry entry, CampaignTargetNode node) in winners)
            {
                MissionTarget target = runtime.For(node.Key);
                Assert.Equal(entry.State.Description, target.Description);
                Assert.Equal(entry.State.Category, target.CategoryLabel);
                Assert.Equal(entry.State.Help, target.HelpLabel);
                Assert.Equal(entry.State.Objective, target.Objective);
                Assert.Equal(entry.State.OtherTarget, target.OtherTarget);
            }
        }

        Assert.Equal(24, missions.Count);
        Assert.Equal(23, local);
        Assert.Equal(1, inherited);
    }
}

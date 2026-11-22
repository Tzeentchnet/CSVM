using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Tooling;
using Xunit;

namespace CSVM.Tests;

/// <summary>Start-animation editing, reference parity, and reader summaries over invented scopes.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionStartAnimationsTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void ReorderMovesWholeEntriesAcrossGroupsWithoutDisturbingUnknownSlots()
    {
        float negativeZero = BitConverter.UInt32BitsToSingle(0x80000000u);
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument document = Document(
            Start(Row(
                Row("alpha", "first-tail", null),
                17f,
                Row(3f, "not-an-entry"),
                Row("alpha", negativeZero, Row("nested-tail", subnormal)),
                Row(),
                "scalar",
                Row("beta", "beta-tail")),
                Row(Row("gamma", "gamma-tail"), null, Row("delta", "delta-tail"))),
            new ZrdrFile("startanims.zrd", "startanims-1.zrd", Row(Row(
                "NEW_GAME_START", Row(Row("other-reader"))))),
            new ZrdrFile("unknown.zrd", null, Row("kept", null, negativeZero)));
        CampaignMissionStartAnimations binding = CampaignMissionStartAnimations.Bind(document);
        Assert.Equal(0, binding.FileIndex);
        Assert.Equal(new[] { "alpha", "alpha", "beta", "gamma", "delta" }, Names(binding));
        Assert.Equal(new[] { 0, 1, 3, 0 }, binding.Entries[1].NamePath);
        Assert.Equal(Names(binding), Runtime(document).StartAnims);
        var moved = binding.Entries[4].Raw;
        var duplicate = binding.Entries[1].Raw;
        int notifications = 0;
        document.Changed += () => notifications++;

        Assert.True(binding.Move(4, 0));
        Assert.Equal(1L, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Same(moved, document.ValueAt(0, new[] { 0, 1, 0 }));
        Assert.Equal("delta-tail", document.ValueAt(0, new[] { 0, 1, 0, 1 }));
        Assert.Equal(17f, document.ValueAt(0, new[] { 0, 1, 1 }));
        Assert.Equal("not-an-entry", document.ValueAt(0, new[] { 0, 1, 2, 1 }));
        Assert.Empty(Assert.IsType<List<object?>>(document.ValueAt(0, new[] { 0, 1, 4 })));
        Assert.Equal("scalar", document.ValueAt(0, new[] { 0, 1, 5 }));
        Assert.Null(document.ValueAt(0, new[] { 0, 5, 1 }));
        Assert.Equal("other-reader", document.ValueAt(1, new[] { 0, 1, 0, 0 }));
        Assert.Throws<InvalidOperationException>(() => binding.Move(0, 0));
        Assert.Throws<InvalidOperationException>(() => binding.Rename(0, "delta"));

        binding = CampaignMissionStartAnimations.Bind(document);
        Assert.Equal(new[] { "delta", "alpha", "alpha", "beta", "gamma" }, Names(binding));
        Assert.Same(duplicate, binding.Entries[2].Raw);
        Assert.True(binding.Rename(2, "omega"));
        Assert.Equal(2L, document.Revision);
        Assert.Equal(2, notifications);
        binding = CampaignMissionStartAnimations.Bind(document);
        Assert.Equal(new[] { "delta", "alpha", "omega", "beta", "gamma" }, Names(binding));
        Assert.Equal(Names(binding), Runtime(document).StartAnims);
        Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 0, 1, 6, 1 })));
        Assert.Equal(1u, Bits(document.ValueAt(0, new[] { 0, 1, 6, 2, 1 })));
        Assert.False(binding.Move(2, 2));
        Assert.False(binding.Rename(2, "omega"));
        Assert.Equal(2L, document.Revision);
    }

    [Fact]
    public void InvalidNamesIndicesAndPermutationsFailBeforeMutation()
    {
        CampaignMissionDocument document = Document(Start(
            Row(Row("one", "tail"), Row("two", null)), Row(Row("three"))));
        CampaignMissionStartAnimations binding = CampaignMissionStartAnimations.Bind(document);
        List<object?> before = CampaignMissionDocument.Copy(document.Files[0].Root);
        int notifications = 0;
        document.Changed += () => notifications++;

        Assert.Throws<ArgumentException>(() => binding.Rename(0, ""));
        Assert.Throws<ArgumentException>(() => binding.Rename(0, "   "));
        Assert.Throws<ArgumentException>(() => binding.Rename(0, "bad\nname"));
        Assert.Throws<ArgumentException>(() => binding.Rename(0, "\ud800"));
        Assert.Throws<ArgumentException>(() => binding.Rename(0, "\udc00"));
        Assert.Throws<ArgumentOutOfRangeException>(() => binding.Rename(-1, "valid"));
        Assert.Throws<ArgumentOutOfRangeException>(() => binding.Move(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => binding.Move(3, 0));
        Assert.Throws<ArgumentException>(() => document.ReorderStartAnimationEntries(
            0, document.Revision, binding.Entries, new[] { 0, 0, 2 }));
        Assert.Throws<ArgumentException>(() => document.ReorderStartAnimationEntries(
            0, document.Revision, binding.Entries, new[] { 0, 1, 3 }));
        AssertTree(before, document.Files[0].Root);
        Assert.Equal(0L, document.Revision);
        Assert.Equal(0, notifications);
        Assert.False(document.IsDirty);

        CampaignMissionDocument emptyName = Document(Start(Row(Row(""))));
        CampaignMissionStartAnimations emptyBinding = CampaignMissionStartAnimations.Bind(emptyName);
        Assert.False(emptyBinding.Rename(0, ""));
        Assert.True(emptyBinding.Rename(0, "fixed\ud83d\ude80"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("flat")]
    [InlineData("no-entries")]
    public void AbsentAndMalformedStartReadersProduceOnlyRuntimeSelectedEntries(string shape)
    {
        var files = new List<ZrdrFile>();
        if (shape != "missing")
        {
            List<object?> root = shape switch
            {
                "empty" => Row(),
                "null" => Row((object?)null),
                "flat" => Row("NEW_GAME_START", Row(Row("ignored"))),
                _ => Row(Row("NEW_GAME_START", Row(null, Row(), Row(5f), "stray"))),
            };
            files.Add(new ZrdrFile("startanims.zrd", null, root));
        }

        CampaignMissionDocument document = Document(files.ToArray());
        CampaignMissionStartAnimations binding = CampaignMissionStartAnimations.Bind(document);
        Assert.Empty(binding.Entries);
        Assert.NotEmpty(binding.Problem);
        Assert.Empty(Runtime(document).StartAnims);
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        Assert.Empty(StartReferences(diagnostics.Current));
        Assert.False(document.IsDirty);
        Assert.Equal(0L, document.Revision);
    }

    [Fact]
    public void FirstReaderAndOnlyFirstStringsOfSelectedEntriesAreReferenced()
    {
        CampaignMissionDocument document = Document(
            new ZrdrFile("STARTANIMS.zrd", null, Row(
                Row("NEW_GAME_START", Row(
                    Row("first", "tail-name", Row("deep-name")),
                    Row(4f, "invalid-first"),
                    "stray",
                    Row("first")),
                    "new_game_start", Row(Row("second"), Row(""))),
                Row("NEW_GAME_START", Row(Row("outer-tail"))))),
            new ZrdrFile("startanims.zrd", null, Row(Row(
                "NEW_GAME_START", Row(Row("second-reader"))))));
        CampaignMissionStartAnimations binding = CampaignMissionStartAnimations.Bind(document);
        Assert.Equal(new[] { "first", "first", "second", "" }, Names(binding));
        Assert.Equal(Names(binding), Runtime(document).StartAnims);
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());
        CheckedReference[] references = StartReferences(diagnostics.Current);
        Assert.Equal(Names(binding), references.Select(reference => reference.Reference.Value.Name));
        Assert.All(references, reference => Assert.Equal(0, reference.Reference.Source.FileIndex));
        Assert.Equal(new[] { 0, 1, 0, 0 }, references[0].Reference.Source.Path);
        Assert.Equal(new[] { 0, 3, 1, 0 }, references[3].Reference.Source.Path);
        Assert.All(references, reference => Assert.Equal(ReferenceState.InheritedMissing, reference.State));
    }

    [Fact]
    public void MissingOwnershipFollowsEachEntryThroughReordersAndDoesNotCoverNewDuplicates()
    {
        CampaignMissionDocument document = Document(Start(Row(
            Row("missing", "one"), Row("good"), Row("missing", "two"))));
        using var diagnostics = new CampaignReferenceDiagnostics(
            document, Bundle(missionAnimations: Archive(new AnimDefinition { Name = "node", AnimName = "good" })));
        var edits = new List<CampaignMissionEdit>();
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });
        Assert.Equal(
            new[] { ReferenceState.InheritedMissing, ReferenceState.Resolved, ReferenceState.InheritedMissing },
            StartReferences(diagnostics.Current).Select(reference => reference.State));
        Assert.False(diagnostics.Current.HasErrors);

        Assert.True(CampaignMissionStartAnimations.Bind(document).Move(0, 2));
        Assert.Equal(new[] { "good", "missing", "missing" },
            StartReferences(diagnostics.Current).Select(reference => reference.Reference.Value.Name));
        Assert.Equal(
            new[] { ReferenceState.Resolved, ReferenceState.InheritedMissing, ReferenceState.InheritedMissing },
            StartReferences(diagnostics.Current).Select(reference => reference.State));
        Assert.False(diagnostics.Current.HasErrors);

        CampaignMissionEdit move = Assert.Single(edits);
        Assert.True(session.Replay(move, forward: false));
        Assert.Equal(
            new[] { ReferenceState.InheritedMissing, ReferenceState.Resolved, ReferenceState.InheritedMissing },
            StartReferences(diagnostics.Current).Select(reference => reference.State));
        Assert.True(session.Replay(move, forward: true));
        Assert.Equal(
            new[] { ReferenceState.Resolved, ReferenceState.InheritedMissing, ReferenceState.InheritedMissing },
            StartReferences(diagnostics.Current).Select(reference => reference.State));

        Assert.True(CampaignMissionStartAnimations.Bind(document).Rename(0, "missing"));
        Assert.Equal(
            new[] { ReferenceState.Missing, ReferenceState.InheritedMissing, ReferenceState.InheritedMissing },
            StartReferences(diagnostics.Current).Select(reference => reference.State));
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(CampaignMissionStartAnimations.Bind(document).Rename(1, "good"));
        Assert.Equal(
            new[] { ReferenceState.Missing, ReferenceState.Resolved, ReferenceState.InheritedMissing },
            StartReferences(diagnostics.Current).Select(reference => reference.State));
        Assert.True(diagnostics.Current.HasErrors);
        Assert.True(CampaignMissionStartAnimations.Bind(document).Rename(0, "good"));
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void DiagnosticsFollowCompiledFirstProgramAndItsScopeAndManifestGatesWithoutMutatingArchives()
    {
        var chapterDoor = new AnimDefinition { Name = "door", AnimName = "door_anim" };
        var chapterFirst = new AnimDefinition { Name = "anchor", AnimName = "dupe" };
        var missionDuplicate = new AnimDefinition { Name = "ANCHOR", AnimName = "DUPE" };
        var missionDoor = new AnimDefinition { Name = "DOOR", AnimName = "DOOR_ANIM" };
        AnimArchive chapterArchive = Archive(chapterFirst, chapterDoor);
        AnimArchive missionArchive = Archive(
            missionDuplicate,
            new AnimDefinition { Name = "other", AnimName = "dupe" },
            new AnimDefinition { Name = "mission", AnimName = "mission_only" },
            missionDoor);
        ZrdrSet shared = Scope(
            Index("anim.zrd", @"..\data\common\zrdr\shared_allowed.zrd"),
            AnimationReader("shared_allowed.zrd", "shared", "shared_good"),
            AnimationReader("shared_blocked.zrd", "shared", "shared_blocked"));
        ZrdrSet chapter = Scope(
            Index("cam_anim.zrd", @"..\data\c3\zrdr\chapter_allowed.zrd"),
            AnimationReader("chapter_allowed.zrd", "chapter", "chapter_good"),
            AnimationReader("chapter_blocked.zrd", "chapter", "chapter_blocked"));
        CampaignMissionDocument document = Document(
            Start(Row(
                Row("DUPE"), Row("mission_only"), Row("shared_good"), Row("chapter_good"),
                Row("door_anim"), Row("shared_blocked"), Row("chapter_blocked"),
                Row("orphan"), Row("missing"))),
            Index("mis_anim.zrd", @"..\data\c3\zrdr\chapter_allowed.zrd"),
            AnimationReader("door.zrd", "door", "door_anim", persist: true),
            AnimationReader("orphan.zrd", "orphan", "orphan"));
        CampaignMissionDependencyBundle bundle = Bundle(shared, chapter, chapterArchive, missionArchive);
        using var diagnostics = new CampaignReferenceDiagnostics(document, bundle);
        CheckedReference[] references = StartReferences(diagnostics.Current);
        AnimProgram inspected = Assert.IsType<AnimProgram>(diagnostics.AnimationInspection.Program);

        Assert.False(chapterDoor.PersistLog);
        Assert.False(missionDoor.PersistLog);
        Assert.Equal(2, inspected.ByAnimName("DuPe").Count);
        Assert.Same(chapterFirst, inspected.ByAnimName("dupe")[0]);
        Assert.DoesNotContain(missionDuplicate, inspected.Defs);
        Assert.Equal(4, inspected.CompiledCount);
        Assert.Equal(2, inspected.ReaderCount);
        Assert.Equal(ReferenceState.Resolved, references[0].State);
        Assert.Equal(ReferenceState.Resolved, references[1].State);
        Assert.Equal(ReferenceState.Resolved, references[2].State);
        Assert.Equal(ReferenceState.Resolved, references[3].State);
        Assert.Equal(ReferenceState.Resolved, references[4].State);
        Assert.All(references.Skip(5), reference => Assert.Equal(ReferenceState.InheritedMissing, reference.State));
        Assert.Contains("shared_blocked", inspected.SharedFilesSkipped);
        Assert.Contains("chapter_blocked", inspected.ChapterFilesSkipped);
        Assert.Contains("orphan/orphan", inspected.MissionLibrarySkipped);

        AnimProgram runtime = AnimProgram.Load(shared, chapter, Scope(document), chapterArchive, missionArchive);
        Assert.True(chapterDoor.PersistLog);
        Assert.False(missionDoor.PersistLog);
        Assert.Equal(
            runtime.Defs.Select(def => (def.Name, def.AnimName)),
            inspected.Defs.Select(def => (def.Name, def.AnimName)));
        Assert.Equal(runtime.StartAnims, Names(CampaignMissionStartAnimations.Bind(document)));
        foreach (CheckedReference reference in references)
        {
            string name = reference.Reference.Value.Name!;
            Assert.Equal(runtime.ByAnimName(name).Count, inspected.ByAnimName(name).Count);
            Assert.Equal(runtime.ByAnimName(name).Count > 0, reference.State == ReferenceState.Resolved);
        }
    }

    [Fact]
    public void MissingOrUnavailableRequiredAnimationInputsRemainUnverified()
    {
        CampaignMissionDocument document = Document(Start(Row(Row("candidate"))));
        CampaignMissionDependencyBundle available = Bundle(
            missionAnimations: Archive(new AnimDefinition { Name = "node", AnimName = "candidate" }));
        var incomplete = new[]
        {
            available with { SharedReaders = Dependency(ZrdrSet.None, "shared", CampaignDependencyState.Missing) },
            available with { ChapterReaders = Dependency(ZrdrSet.None, "chapter", CampaignDependencyState.Unavailable) },
            available with { ChapterAnimations = Dependency<AnimArchive?>(null, "chapter animation", CampaignDependencyState.NotChecked, true) },
            available with { MissionAnimations = Dependency<AnimArchive?>(null, "mission animation", CampaignDependencyState.Unavailable, true) },
        };
        foreach (CampaignMissionDependencyBundle bundle in incomplete)
        {
            using var diagnostics = new CampaignReferenceDiagnostics(document, bundle);
            Assert.Equal(ReferenceState.Unverified, Assert.Single(StartReferences(diagnostics.Current)).State);
            Assert.Null(diagnostics.AnimationInspection.Program);
            Assert.NotNull(diagnostics.AnimationInspection.UnavailableReason);
        }

        using var known = new CampaignReferenceDiagnostics(document, available);
        Assert.Equal(ReferenceState.Resolved, Assert.Single(StartReferences(known.Current)).State);
        using var readerOnly = new CampaignReferenceDiagnostics(
            Document(Start(Row(Row("reader"))), AnimationReader("local.zrd", "node", "reader")),
            Bundle());
        Assert.Equal(ReferenceState.Resolved, Assert.Single(StartReferences(readerOnly.Current)).State);
    }

    [Fact]
    public void SummariesMatchNetReaderAndPreserveIndexPathsWithoutInventingMapSemantics()
    {
        float negativeZero = BitConverter.UInt32BitsToSingle(0x80000000u);
        CampaignMissionDocument document = Document(
            new ZrdrFile("map.zrd", null, Row(Row("MAP", Row("unknown", 12f)), "map-tail")),
            new ZrdrFile("net.zrd", null, Row(Row(
                Row(10f, negativeZero, 3.5f, 90f, "tail"),
                Row(1f, 2f, 3f),
                Row(1f, 2f, "wrong", 4f),
                null,
                Row(-4f, 5f, 6f, -180f)), "net-tail")),
            Index("mis_anim.zrd", @"..\data\c3\zrdr\first.zrd", @"..\data\c3\zrdr\first.zrd"),
            AnimationReader("local.zrd", "node", "local"));
        CampaignMissionDependencyBundle bundle = Bundle();
        using var diagnostics = new CampaignReferenceDiagnostics(document, bundle);
        CampaignMissionReaderSummaries summary =
            CampaignMissionReaderSummaries.Read(document, bundle, diagnostics.AnimationInspection);

        Assert.True(summary.Map.Present);
        Assert.Equal(2, summary.Map.RootCount);
        Assert.Equal("list", summary.Map.FirstKind);
        Assert.Equal(2, summary.Map.FirstListCount);
        Assert.Equal(5, summary.Net.RawRows);
        Assert.Equal(3, summary.Net.IgnoredRows);
        Assert.Null(summary.Net.Problem);
        Assert.Equal(2, summary.Net.Points.Length);
        Assert.Equal(10f, summary.Net.Points[0].Position.X);
        Assert.Equal(0x80000000u, BitConverter.SingleToUInt32Bits(summary.Net.Points[0].Position.Y));
        Assert.Equal(3.5f, summary.Net.Points[0].Position.Z);
        Assert.Equal(90f, summary.Net.Points[0].HeadingDeg);
        Assert.Equal(-180f, summary.Net.Points[1].HeadingDeg);
        Assert.Equal(SpawnPoints.LoadNetTable(Scope(document))!.ToArray(), summary.Net.Points.ToArray());
        Assert.True(summary.Animations.Index.Present);
        Assert.Equal(new[] { @"..\data\c3\zrdr\first.zrd", @"..\data\c3\zrdr\first.zrd" },
            summary.Animations.ListedPaths);
        Assert.Equal(CampaignDependencyState.Missing, summary.Animations.MissionArchive);
        AnimProgram program = Assert.IsType<AnimProgram>(summary.Animations.Inspection.Program);
        Assert.Equal(0, program.CompiledCount);
        Assert.Equal(1, program.ReaderCount);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void SummariesDistinguishAbsentEmptyAndMalformedReaders()
    {
        CampaignMissionDocument absent = Document();
        CampaignMissionDependencyBundle bundle = Bundle();
        using var absentDiagnostics = new CampaignReferenceDiagnostics(absent, bundle);
        CampaignMissionReaderSummaries missing =
            CampaignMissionReaderSummaries.Read(absent, bundle, absentDiagnostics.AnimationInspection);
        Assert.False(missing.Map.Present);
        Assert.False(missing.Net.Reader.Present);
        Assert.False(missing.Animations.Index.Present);
        Assert.Empty(missing.Net.Points);
        Assert.Empty(missing.Animations.ListedPaths);

        CampaignMissionDocument malformed = Document(
            new ZrdrFile("map.zrd", null, Row()),
            new ZrdrFile("net.zrd", null, Row("not-a-table")),
            new ZrdrFile("mis_anim.zrd", null, Row(7f)));
        using var malformedDiagnostics = new CampaignReferenceDiagnostics(malformed, bundle);
        CampaignMissionReaderSummaries raw =
            CampaignMissionReaderSummaries.Read(malformed, bundle, malformedDiagnostics.AnimationInspection);
        Assert.True(raw.Map.Present);
        Assert.Equal("empty", raw.Map.FirstKind);
        Assert.Equal("string", raw.Net.Reader.FirstKind);
        Assert.Equal("float", raw.Animations.Index.FirstKind);
        Assert.Empty(raw.Net.Points);
        Assert.Empty(raw.Animations.ListedPaths);

        CampaignMissionDocument emptyNet = Document(new ZrdrFile("net.zrd", null, Row()));
        using var emptyDiagnostics = new CampaignReferenceDiagnostics(emptyNet, bundle);
        CampaignMissionReaderSummaries empty =
            CampaignMissionReaderSummaries.Read(emptyNet, bundle, emptyDiagnostics.AnimationInspection);
        Assert.True(empty.Net.Reader.Present);
        Assert.Equal("empty", empty.Net.Reader.FirstKind);
        Assert.NotNull(empty.Net.Problem);
        Assert.Empty(empty.Net.Points);
        Assert.Throws<InvalidDataException>(() => SpawnPoints.LoadNetTable(Scope(emptyNet)));
    }

    [Fact]
    public void NoEditAndEditedExportsPreserveTheCompleteScopeAndFloatBits()
    {
        float negativeZero = BitConverter.UInt32BitsToSingle(0x80000000u);
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        ZrdrFile[] files =
        {
            Start(Row(Row("one", negativeZero, null), Row("two", subnormal)), Row(Row("one", "tail"))),
            new("startanims.zrd", "startanims-1.zrd", Row(Row("other", Row((object?)null)))),
            new("map.zrd", null, Row(Row("MAP", Row(negativeZero, subnormal, null)))),
            new("net.zrd", null, Row((object?)null)),
            Index("mis_anim.zrd", @"..\data\c3\zrdr\unknown.zrd"),
            new("unknown.zrd", null, Row("unknown", Row(-0f, subnormal, null, Row("nested")))),
        };
        CampaignMissionDocument document = Document(files);
        CampaignMissionDependencyBundle bundle = Bundle();
        using var diagnostics = new CampaignReferenceDiagnostics(document, bundle);
        _ = diagnostics.Current;
        _ = CampaignMissionReaderSummaries.Read(document, bundle, diagnostics.AnimationInspection);
        _ = CampaignMissionStartAnimations.Bind(document);
        string root = Path.Combine(TestData.RepoRoot, ".scratch", $"c28-start-{Guid.NewGuid():N}");
        try
        {
            string output = document.Export(root);
            byte[] originalZip = File.ReadAllBytes(output);
            Assert.Equal(originalZip, File.ReadAllBytes(document.Export(root)));
            ZrdrSet reopened = ZrdrSet.FromExtracted(output);
            Assert.Equal(files.Select(file => file.Key), reopened.Files.Select(file => file.Key));
            for (int i = 0; i < files.Length; i++)
            {
                AssertTree(files[i].Root, document.Files[i].Root);
                AssertTree(files[i].Root, reopened.Files[i].Root);
            }

            Assert.Equal(0L, document.Revision);
            Assert.False(document.IsDirty);
            Assert.True(CampaignMissionStartAnimations.Bind(document).Move(2, 0));
            Assert.True(CampaignMissionStartAnimations.Bind(document).Rename(1, "renamed"));
            string changedOutput = document.Export(root);
            ZrdrSet changed = ZrdrSet.FromExtracted(changedOutput);
            Assert.Equal(document.Files.Select(file => file.Key), changed.Files.Select(file => file.Key));
            for (int i = 0; i < document.Files.Count; i++)
            {
                AssertTree(document.Files[i].Root, changed.Files[i].Root);
            }

            Assert.Equal(new[] { "one", "renamed", "two" },
                AnimProgram.Load(ZrdrSet.None, ZrdrSet.None, changed, null, null).StartAnims);
            Assert.Equal(2L, document.Revision);
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

    private static CheckedReference[] StartReferences(ReferenceSnapshot snapshot) =>
        snapshot.References.Where(reference => reference.Reference.Rule == ReferenceRule.StartAnimation).ToArray();

    private static string[] Names(CampaignMissionStartAnimations binding) =>
        binding.Entries.Select(entry => entry.Name).ToArray();

    private static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, Scope(files));

    private static ZrdrSet Scope(params ZrdrFile[] files) => new("invented", files);

    private static ZrdrSet Scope(CampaignMissionDocument document) => new("mission", document.Files);

    private static List<object?> Row(params object?[] values) => new(values);

    private static ZrdrFile Start(List<object?> first, List<object?>? second = null)
    {
        var outer = Row("NEW_GAME_START", first, "LOAD_GAME_START", Row(Row("load-only")));
        if (second != null)
        {
            outer.Add("new_game_start");
            outer.Add(second);
        }

        return new ZrdrFile("startanims.zrd", null, Row(outer));
    }

    private static ZrdrFile Index(string name, params string[] paths)
    {
        var values = new List<object?>();
        foreach (string path in paths)
        {
            values.Add("ANIMATION_DEFINITION_FILE");
            values.Add(Row(path));
        }

        return new ZrdrFile(name, null, Row(values));
    }

    private static ZrdrFile AnimationReader(string file, string node, string animation, bool persist = false) =>
        new(file, null, Row(Row(
            "ANIMATION_DEFINITIONS", Row(
                "ANIMATION_LIST", Row(
                    "ANIMATION_DEFINITION", Row(
                        "NAME", Row(node),
                        "ANIMATION_NAME", Row(animation),
                        "PERSIST_LOG", Row(persist ? "ON" : "OFF")))))));

    private static AnimArchive Archive(params AnimDefinition[] definitions) =>
        AnimArchive.FromDecoded("invented", definitions.ToList(), new List<(string Name, SiScript Script)>());

    private static AnimProgram Runtime(CampaignMissionDocument document) =>
        AnimProgram.Load(ZrdrSet.None, ZrdrSet.None, Scope(document), null, null);

    private static CampaignMissionDependencyBundle Bundle(
        ZrdrSet? shared = null,
        ZrdrSet? chapter = null,
        AnimArchive? chapterAnimations = null,
        AnimArchive? missionAnimations = null) =>
        new(
            Guid.NewGuid(), Mission,
            Dependency(shared ?? Scope(), "shared", CampaignDependencyState.Ready),
            Dependency<GameZ?>(new GameZ(), "world", CampaignDependencyState.Ready),
            Dependency(TextureSet.Empty, "textures", CampaignDependencyState.Ready),
            Dependency(chapter ?? Scope(), "chapter", CampaignDependencyState.Ready),
            Dependency<AnimArchive?>(chapterAnimations, "chapter animation",
                chapterAnimations == null ? CampaignDependencyState.Missing : CampaignDependencyState.Ready, true),
            Dependency<AnimArchive?>(missionAnimations, "mission animation",
                missionAnimations == null ? CampaignDependencyState.Missing : CampaignDependencyState.Ready, true),
            Dependency(new Messages(), "messages", CampaignDependencyState.Ready, true),
            Dependency<InterpScripts?>(null, "interp", CampaignDependencyState.Missing, true),
            Dependency<MissionSetup?>(null, "setup", CampaignDependencyState.NotChecked, true),
            null);

    private static CampaignDependency<T> Dependency<T>(
        T value, string path, CampaignDependencyState state, bool optional = false) =>
        new(value, new CampaignDependencyAvailability(path, state, optional));

    private static uint Bits(object? value) =>
        BitConverter.SingleToUInt32Bits(Assert.IsType<float>(value));

    private static void AssertTree(object? expected, object? actual)
    {
        switch (expected)
        {
            case null:
                Assert.Null(actual);
                break;
            case string text:
                Assert.Equal(text, Assert.IsType<string>(actual));
                break;
            case float number:
                Assert.Equal(Bits(number), Bits(actual));
                break;
            case List<object?> values:
                var other = Assert.IsType<List<object?>>(actual);
                Assert.Equal(values.Count, other.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    AssertTree(values[i], other[i]);
                }

                break;
            default:
                throw new InvalidOperationException("The invented tree contains an unsupported value.");
        }
    }
}

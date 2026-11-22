using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Mech3.Anim;
using CSVM.Session.Campaign;
using CSVM.Session.Objectives;
using CSVM.Spec;
using CSVM.Tooling;
using Godot;
using Xunit;
using Xunit.Abstractions;

namespace CSVM.Tests;

/// <summary>Mission danger-zone editing, references, and runtime reader parity.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionDangerZonesTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void AtomicEditsUseLastSelectedValuesAndPreserveUnknownData()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument document = Document(
            Zones(Row(
                "disable", Row("shadow"),
                "DiSaBlE", Row("dzpath1", "DZPATH1", Row("dzpath1", "nested"), 99f),
                "nosnapshot", Row("DZPATH1", "dzpath1", "dzpath2", Row("dzpath1")),
                "objective_numbers", Row(
                    Row("dzpath1", 18f, "shadow-tail"),
                    Row("DZPATH1", 20.75f, "winner-tail", subnormal),
                    Row("dzpath1", "not-a-number", Row("keep")),
                    Row("dzpath2", 21f)),
                "unknown", Row(-0f, subnormal, null, Row("keep")))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1", "dzpath2")))));
        GameZ world = World("dzpath1", "dzpath2");
        CampaignMissionDangerZones binding = Bind(document, world);
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        Assert.False(entry.State.Enabled);
        Assert.False(entry.State.SnapshotAllowed);
        Assert.Equal(20.75f, entry.State.ObjectiveSlot);
        Assert.Equal(new[] { 7, 1, 1 }, entry.SlotPath);
        Assert.Equal(new[] { 1 }, entry.ObjectiveBlocks);
        Assert.True(entry.HasGates);

        CampaignDangerZones before = Assert.IsType<CampaignDangerZones>(Load(document, world));
        Assert.False(before.TryZone("dzpath1", out _, out _));
        Assert.True(before.TryZone("dzpath2", out int oldSlot, out bool oldPhoto));
        Assert.Equal(21, oldSlot);
        Assert.False(oldPhoto);
        DangerZoneRibbons ribbonsBefore = Assert.IsType<DangerZoneRibbons>(
            DangerZoneRibbons.Load(world, CampaignDangerZones.ReadDisabled(Scope(document))));
        Assert.False(ribbonsBefore.ByIndex(1)!.Active);

        int notifications = 0;
        document.Changed += () => notifications++;
        Assert.False(binding.Apply(entry, entry.State));
        Assert.Equal(0, notifications);
        Assert.True(binding.Apply(entry, new CampaignDangerZoneState(true, true, 29f)));
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.True(document.IsDirty);
        Assert.Throws<InvalidOperationException>(() => binding.Apply(entry, entry.State));

        Assert.Equal("shadow", document.ValueAt(0, new[] { 1, 0 }));
        Assert.Equal("dzpath1", document.ValueAt(0, new[] { 3, 0, 0 }));
        Assert.Equal("nested", document.ValueAt(0, new[] { 3, 0, 1 }));
        Assert.Equal(99f, document.ValueAt(0, new[] { 3, 1 }));
        Assert.Equal("dzpath2", document.ValueAt(0, new[] { 5, 0 }));
        Assert.Equal("dzpath1", document.ValueAt(0, new[] { 5, 1, 0 }));
        Assert.Equal(18f, document.ValueAt(0, new[] { 7, 0, 1 }));
        Assert.Equal("shadow-tail", document.ValueAt(0, new[] { 7, 0, 2 }));
        Assert.Equal(29f, document.ValueAt(0, new[] { 7, 1, 1 }));
        Assert.Equal("winner-tail", document.ValueAt(0, new[] { 7, 1, 2 }));
        Assert.Equal(1u, Bits(document, 0, 7, 1, 3));
        Assert.Equal("not-a-number", document.ValueAt(0, new[] { 7, 2, 1 }));
        Assert.Equal("keep", document.ValueAt(0, new[] { 7, 2, 2, 0 }));
        Assert.Equal(0x80000000u, Bits(document, 0, 9, 0));
        Assert.Equal(1u, Bits(document, 0, 9, 1));
        Assert.Null(document.ValueAt(0, new[] { 9, 2 }));

        CampaignDangerZones after = Assert.IsType<CampaignDangerZones>(Load(document, world));
        Assert.True(after.TryZone("DZPATH1", out int slot, out bool snapshot));
        Assert.Equal(29, slot);
        Assert.True(snapshot);
        Assert.True(after.TryZone("dzpath2", out _, out bool secondSnapshot));
        Assert.False(secondSnapshot);
        DangerZoneRibbons ribbonsAfter = Assert.IsType<DangerZoneRibbons>(
            DangerZoneRibbons.Load(world, CampaignDangerZones.ReadDisabled(Scope(document))));
        Assert.True(ribbonsAfter.ByIndex(1)!.Active);
    }

    [Fact]
    public void ValidationRejectsTheWholeRowAndLeavesRevisionAndNotificationsAlone()
    {
        CampaignMissionDocument document = Document(
            Zones(Row("objective_numbers", Row(Row("dzpath1", 18f, "tail")))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        CampaignMissionDangerZones binding = Bind(document, World("dzpath1"));
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        int notifications = 0;
        document.Changed += () => notifications++;
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, 1.5f, -2f, 2147483648f })
        {
            Assert.Throws<ArgumentException>(() =>
                binding.Apply(entry, new CampaignDangerZoneState(false, false, invalid)));
        }

        Assert.Throws<ArgumentException>(() =>
            binding.Apply(entry, new CampaignDangerZoneState(false, false, null)));
        Assert.Throws<ArgumentException>(() =>
            binding.Apply(entry with { }, new CampaignDangerZoneState(false, false, 19f)));
        Assert.Equal(0, notifications);
        Assert.Equal(0, document.Revision);
        Assert.False(document.IsDirty);
        Assert.Equal(18f, document.ValueAt(0, new[] { 1, 0, 1 }));
        Assert.Equal("tail", document.ValueAt(0, new[] { 1, 0, 2 }));
        Assert.Single(document.Files[0].Root.Where(value => value is string));
    }

    [Fact]
    public void AbsentAndChapterOnlyReadersStayAbsentUntilExplicitlyCreated()
    {
        CampaignMissionDocument document = Document(
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        GameZ world = World("dzpath1");
        ZrdrSet chapter = new("chapter", new[]
        {
            Zones(Row("disable", Row("dzpath1"), "objective_numbers", Row(Row("dzpath1", 25f)))),
        });
        CampaignMissionDangerZones binding = Bind(document, world);
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        Assert.True(binding.CanCreate);
        Assert.False(binding.CanEdit);
        Assert.Equal(new CampaignDangerZoneState(true, true, null), entry.State);
        Assert.Equal(-1, document.FindFileIndex("dzones.json"));
        CampaignDangerZones armed = Assert.IsType<CampaignDangerZones>(Load(document, world));
        Assert.True(armed.TryZone("dzpath1", out int slot, out bool snapshot));
        Assert.Equal(-1, slot);
        Assert.True(snapshot);
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(world, chapter));
        Assert.DoesNotContain(diagnostics.Current.References, item =>
            item.Reference.Source.Scope == ReferenceScope.Chapter
            && item.Reference.Kind == ReferenceKind.DangerZone);
        Assert.Throws<InvalidOperationException>(() =>
            binding.Apply(entry, new CampaignDangerZoneState(false, false, 18f)));

        int notifications = 0;
        document.Changed += () => notifications++;
        int index = binding.CreateMissionReader();
        Assert.Equal(1, index);
        Assert.Empty(document.Files[index].Root);
        Assert.Equal(1, notifications);
        Assert.Throws<InvalidOperationException>(() => binding.CreateMissionReader());
        binding = Bind(document, world);
        Assert.True(binding.CanEdit);
        entry = Find(binding, "dzpath1");
        Assert.True(binding.Apply(entry, new CampaignDangerZoneState(true, false, 19f)));
        Assert.Equal(2, notifications);
        Assert.Equal(2, document.Revision);
        armed = Assert.IsType<CampaignDangerZones>(Load(document, world));
        Assert.True(armed.TryZone("dzpath1", out slot, out snapshot));
        Assert.Equal(19, slot);
        Assert.False(snapshot);
        Assert.Equal("dzpath1", chapter.Files[0].Root[1] is List<object?> disabled ? disabled[0] : null);
    }

    [Fact]
    public void FirstReaderAndLastBareOrInvalidPairsFollowRuntimeSelection()
    {
        CampaignMissionDocument document = Document(
            Zones(Row(
                "disable", Row("dzpath1"),
                "objective_numbers", Row(Row("dzpath1", 18f)),
                "OBJECTIVE_NUMBERS", Row(Row("DZPATH1", 23f, "keep"), Row("dzpath1", "bad")),
                "DISABLE")),
            Zones(Row("disable", Row("dzpath1"), "objective_numbers", Row(Row("dzpath1", 30f)))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        GameZ world = World("dzpath1");
        CampaignMissionDangerZones binding = Bind(document, world);
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        Assert.Equal(0, binding.FileIndex);
        Assert.True(entry.State.Enabled);
        Assert.Equal(23f, entry.State.ObjectiveSlot);
        Assert.Equal(new[] { 5, 0, 1 }, entry.SlotPath);
        Assert.True(binding.Apply(entry, new CampaignDangerZoneState(false, true, 24f)));
        Assert.Equal("DISABLE", document.ValueAt(0, new[] { 6 }));
        Assert.Equal("disable", document.ValueAt(0, new[] { 7 }));
        Assert.Equal("dzpath1", document.ValueAt(0, new[] { 8, 0 }));
        Assert.Equal("keep", document.ValueAt(0, new[] { 5, 0, 2 }));
        Assert.Equal(30f, document.ValueAt(1, new[] { 3, 0, 1 }));
        Assert.Null(Load(document, world));

        CampaignMissionDocument emptyFirst = Document(
            Zones(Row()),
            Zones(Row("disable", Row("dzpath1"))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        CampaignMissionDangerZones first = Bind(emptyFirst, world);
        Assert.True(first.CanEdit);
        Assert.True(Find(first, "dzpath1").State.Enabled);
        Assert.IsType<CampaignDangerZones>(Load(emptyFirst, world));
    }

    [Fact]
    public void BareSnapshotAndSlotKeysStayRawWhenNewValuesAreAdded()
    {
        CampaignMissionDocument document = Document(
            Zones(Row(
                "nosnapshot", Row("dzpath1"),
                "NOSNAPSHOT",
                "objective_numbers", Row(Row("dzpath1", 18f, "old-tail")),
                "OBJECTIVE_NUMBERS")),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        GameZ world = World("dzpath1");
        CampaignMissionDangerZones binding = Bind(document, world);
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        Assert.True(entry.State.SnapshotAllowed);
        Assert.Null(entry.State.ObjectiveSlot);
        Assert.True(binding.Apply(entry, new CampaignDangerZoneState(false, true, null)));
        Assert.Equal(-2, CampaignRawList.LastListValueIndex(document.Files[0].Root, "objective_numbers"));
        Assert.Equal(-2, CampaignRawList.LastListValueIndex(document.Files[0].Root, "nosnapshot"));

        binding = Bind(document, world);
        entry = Find(binding, "dzpath1");
        Assert.True(binding.Apply(entry, new CampaignDangerZoneState(true, false, 22f)));
        List<object?> root = document.Files[0].Root;
        Assert.Equal("NOSNAPSHOT", root[2]);
        Assert.Equal("OBJECTIVE_NUMBERS", root[5]);
        Assert.Equal("old-tail", document.ValueAt(0, new[] { 4, 0, 2 }));
        CampaignDangerZones armed = Assert.IsType<CampaignDangerZones>(Load(document, world));
        Assert.True(armed.TryZone("dzpath1", out int slot, out bool snapshot));
        Assert.Equal(22, slot);
        Assert.False(snapshot);
    }

    [Fact]
    public void FlagEditsPreserveAnUnchangedUnusualSlotAndMinusOneKeepsItsTail()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument document = Document(
            Zones(Row("objective_numbers", Row(Row("dzpath1", subnormal, "tail")))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        GameZ world = World("dzpath1");
        CampaignMissionDangerZones binding = Bind(document, world);
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        Assert.True(binding.Apply(entry, entry.State with { SnapshotAllowed = false }));
        Assert.Equal(1u, Bits(document, 0, 1, 0, 1));
        Assert.Equal("tail", document.ValueAt(0, new[] { 1, 0, 2 }));
        CampaignDangerZones armed = Assert.IsType<CampaignDangerZones>(Load(document, world));
        Assert.True(armed.TryZone("dzpath1", out int slot, out bool snapshot));
        Assert.Equal(0, slot);
        Assert.False(snapshot);

        binding = Bind(document, world);
        entry = Find(binding, "dzpath1");
        Assert.True(binding.Apply(entry, entry.State with { ObjectiveSlot = -1f }));
        Assert.Equal(-1f, document.ValueAt(0, new[] { 1, 0, 1 }));
        Assert.Equal("tail", document.ValueAt(0, new[] { 1, 0, 2 }));
        armed = Assert.IsType<CampaignDangerZones>(Load(document, world));
        Assert.True(armed.TryZone("dzpath1", out slot, out _));
        Assert.Equal(-1, slot);
    }

    [Fact]
    public void SlotTextKeepsAnExistingNanPayloadButRejectsNewNonfiniteOrRoundedValues()
    {
        float unusual = BitConverter.UInt32BitsToSingle(0x7fc01234u);
        CampaignMissionDocument document = Document(
            Zones(Row("objective_numbers", Row(Row("dzpath1", unusual, "tail")))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        CampaignMissionDangerZones binding = Bind(document, World("dzpath1"));
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        float? parsed = CampaignMissionDangerZones.ParseSlot("NaN", entry.State.ObjectiveSlot);
        Assert.True(binding.Apply(entry, entry.State with { SnapshotAllowed = false, ObjectiveSlot = parsed }));
        Assert.Equal(0x7fc01234u, Bits(document, 0, 1, 0, 1));
        Assert.Equal("tail", document.ValueAt(0, new[] { 1, 0, 2 }));
        Assert.Throws<FormatException>(() => CampaignMissionDangerZones.ParseSlot("NaN", null));
        Assert.Throws<FormatException>(() => CampaignMissionDangerZones.ParseSlot("Infinity", null));
        Assert.Throws<FormatException>(() => CampaignMissionDangerZones.ParseSlot("2147483647", null));
        Assert.Equal(-1f, CampaignMissionDangerZones.ParseSlot("-1", unusual));
    }

    [Fact]
    public void ObjectiveReferencesUseExactContiguousFirstSelectionsAndChapterGates()
    {
        CampaignMissionDocument document = Document(Objectives(Row(
            "OBJECTIVE1", Row(
                "DANGER_ZONES_COMPLETED", Row("dzpath1", Row("missing", Row("too-deep")), "wrong-scope"),
                "DANGER_ZONES_COMPLETED", Row("shadow-directive")),
            "OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("shadow-block")),
            "OBJECTIVE3", Row("DANGER_ZONES_COMPLETED", Row("after-gap")),
            "objective2", Row("DANGER_ZONES_COMPLETED", Row("wrong-case")),
            "OTHER", Row("DANGER_ZONES_COMPLETED", Row("nested-unrelated")))));
        GameZ world = World("dzpath1");
        ZrdrSet chapter = new("chapter", new[] { Zones(Row("disable", Row("wrong-scope"))) });
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(world, chapter));
        ReferenceSnapshot first = diagnostics.Current;
        Assert.Equal(ReferenceState.Resolved, Ref(first, "dzpath1").State);
        Assert.Equal(ReferenceState.InheritedMissing, Ref(first, "missing").State);
        Assert.Equal(new[] { 0, 1, 1, 1, 0 }, Ref(first, "missing").Reference.Source.Path);
        Assert.Equal(ReferenceState.InheritedMissing, Ref(first, "wrong-scope").State);
        Assert.Contains("selected chapter gamez", Ref(first, "wrong-scope").Detail);
        Assert.Equal(ReferenceState.Suppressed, Ref(first, "shadow-directive").State);
        Assert.Equal(ReferenceState.Suppressed, Ref(first, "shadow-block").State);
        Assert.Equal(ReferenceState.Suppressed, Ref(first, "after-gap").State);
        Assert.DoesNotContain(first.References, item => item.Reference.Value.Name is
            "too-deep" or "wrong-case" or "nested-unrelated");
        Assert.False(first.HasErrors);

        Assert.True(document.SetLeaf(0, new[] { 0, 1, 1, 2 }, "new-missing"));
        ReferenceSnapshot changed = diagnostics.Current;
        Assert.Equal(ReferenceState.Missing, Ref(changed, "new-missing").State);
        Assert.True(changed.HasErrors);
        Assert.True(document.SetLeaf(0, new[] { 0, 1, 1, 2 }, "wrong-scope"));
        Assert.Equal(ReferenceState.InheritedMissing, Ref(diagnostics.Current, "wrong-scope").State);
        Assert.False(diagnostics.Current.HasErrors);

        using var noWorld = new CampaignReferenceDiagnostics(document, Bundle(null, chapter));
        Assert.Equal(ReferenceState.Unverified, Ref(noWorld.Current, "wrong-scope").State);
    }

    [Fact]
    public void BareFirstObjectiveAndDirectiveSuppressLaterDuplicates()
    {
        CampaignMissionDocument document = Document(Objectives(Row(
            "OBJECTIVE1",
            "OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("shadow-block")),
            "OBJECTIVE2", Row(
                "DANGER_ZONES_COMPLETED",
                "DANGER_ZONES_COMPLETED", Row("shadow-directive")),
            "OBJECTIVE3", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        GameZ world = World("dzpath1");
        CampaignDangerZoneEntry entry = Find(Bind(document, world), "dzpath1");
        Assert.Equal(new[] { 3 }, entry.ObjectiveBlocks);
        Assert.IsType<CampaignDangerZones>(Load(document, world));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(world));
        Assert.Equal(ReferenceState.Suppressed, Ref(diagnostics.Current, "shadow-block").State);
        Assert.Equal(ReferenceState.Suppressed, Ref(diagnostics.Current, "shadow-directive").State);
        Assert.Equal(ReferenceState.Resolved, Ref(diagnostics.Current, "dzpath1").State);
    }

    [Fact]
    public void ShadowedObjectiveReadersKeepTheirDangerReferencesSuppressed()
    {
        CampaignMissionDocument document = Document(
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("shadowed-reader")))));
        GameZ world = World("dzpath1");
        CampaignMissionDangerZones binding = Bind(document, world);
        Assert.DoesNotContain(binding.Entries, entry => entry.Name == "shadowed-reader");
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(world));
        Assert.Equal(ReferenceState.Resolved, Ref(diagnostics.Current, "dzpath1").State);
        Assert.Equal(ReferenceState.Suppressed, Ref(diagnostics.Current, "shadowed-reader").State);
        Assert.IsType<CampaignDangerZones>(Load(document, world));
    }

    [Fact]
    public void SlotReferencesAreNotObjectiveGraphLinksAndMissingIdentitySurvivesListEdits()
    {
        CampaignMissionDocument document = Document(
            Zones(Row(
                "disable", Row("dzpath1", "missing-zone"),
                "objective_numbers", Row(Row("dzpath1", 18f), Row("DZPATH1", 31f, "tail")))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        GameZ world = World("dzpath1");
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(world));
        ReferenceSnapshot before = diagnostics.Current;
        Assert.Equal(ReferenceState.InheritedMissing, Ref(before, "missing-zone").State);
        Assert.Equal(new[] { 1, 1 }, Ref(before, "missing-zone").Reference.Source.Path);
        CheckedReference[] slots = before.References.Where(item => item.Reference.Rule == ReferenceRule.DangerSlot).ToArray();
        Assert.Equal(2, slots.Length);
        Assert.Equal(ReferenceState.Suppressed, slots[0].State);
        Assert.Equal(ReferenceState.Resolved, slots[1].State);
        Assert.Contains("does not light a debrief row", slots[1].Detail);
        Assert.Equal(31, slots[1].Reference.Value.Number);
        Assert.DoesNotContain(before.References, item =>
            item.Reference.Rule == ReferenceRule.ObjectiveNumber && item.Reference.Value.Number == 31);

        CampaignMissionDangerZones binding = Bind(document, world);
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        Assert.True(binding.Apply(entry, entry.State with { Enabled = true }));
        ReferenceSnapshot after = diagnostics.Current;
        Assert.Equal(new[] { 1, 0 }, Ref(after, "missing-zone").Reference.Source.Path);
        Assert.Equal(ReferenceState.InheritedMissing, Ref(after, "MISSING-ZONE").State);
        Assert.False(after.HasErrors);
        Assert.False(binding.IsCurrent);
    }

    [Fact]
    public void GateAndRibbonDuplicatesUseTheirDifferentRuntimeSelectionRules()
    {
        var world = new GameZ();
        world.Nodes.Add(new GameZNode { Index = 0, Name = "dzpath1", MeshIndex = -1 });
        AddZone(world, "DZPATH01", 20f);
        AddZone(world, "DZPATH1", 40f);
        world.Nodes.Add(new GameZNode { Index = world.Nodes.Count, Name = "dzpaths" });
        CampaignDangerZoneWorld projection = Project(world);
        Assert.DoesNotContain("dzpaths", projection.Names);
        Assert.False(projection.HasGates("dzpath1"));
        CampaignDangerRibbon ribbon = Assert.IsType<CampaignDangerRibbon>(projection.Ribbon("dzpath01"));
        Assert.Equal("DZPATH1", ribbon.Name);
        DangerZoneRibbons runtime = Assert.IsType<DangerZoneRibbons>(DangerZoneRibbons.Load(world));
        Assert.Equal(runtime.ByIndex(1)!.Name, ribbon.Name);
        Assert.Equal(runtime.ByIndex(1)!.PointAt(0, 0f), ribbon.Points[0]);
        Assert.Equal(runtime.ByIndex(1)!.Lanes.Count, ribbon.Lanes);

        CampaignMissionDocument document = Document(
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        Assert.Null(Load(document, world));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(world));
        Assert.Equal(ReferenceState.InheritedMissing, Ref(diagnostics.Current, "dzpath1").State);
        Assert.Contains("first chapter-world node", Ref(diagnostics.Current, "dzpath1").Detail);
    }

    [Fact]
    public void UnavailableOrMalformedRibbonDataIsReportedWithoutEditingTheWorld()
    {
        CampaignDangerZoneWorld absent = Project(null);
        Assert.Empty(absent.Names);
        Assert.Null(absent.HasGates("dzpath1"));
        Assert.Null(absent.Ribbon("dzpath1"));
        Assert.NotEmpty(absent.Problem);

        GameZ malformed = World("dzpath1");
        malformed.Meshes[0].Vertices[1] = malformed.Meshes[0].Vertices[0];
        CampaignDangerZoneWorld projection = Project(malformed);
        Assert.NotEmpty(projection.Problem);
        Assert.Null(projection.Ribbon("dzpath1"));
        Assert.True(projection.HasGates("dzpath1"));
        Assert.Equal(malformed.Meshes[0].Vertices[0], malformed.Meshes[0].Vertices[1]);
    }

    [Fact]
    public void DisableCanResolveAnAiRibbonEvenWhenThePlayerGateIsInvalid()
    {
        GameZ world = World("dzpath1");
        AddPolygon(world.Meshes[0], 2, 2, 3, 4, 5);
        CampaignMissionDocument aiOnly = Document(Zones(Row("disable", Row("dzpath1"))));
        using var aiDiagnostics = new CampaignReferenceDiagnostics(aiOnly, Bundle(world));
        CheckedReference aiDisable = Assert.Single(aiDiagnostics.Current.References.Where(item =>
            item.Reference.Rule == ReferenceRule.DangerOverridePath));
        Assert.Equal(ReferenceState.Resolved, aiDisable.State);
        Assert.Contains("AI ribbon", aiDisable.Detail);

        CampaignMissionDocument document = Document(
            Zones(Row("disable", Row("dzpath1"))),
            Objectives(Row("OBJECTIVE1", Row("DANGER_ZONES_COMPLETED", Row("dzpath1")))));
        CampaignDangerZoneWorld projection = Project(world);
        Assert.False(projection.HasGates("dzpath1"));
        Assert.NotNull(projection.Ribbon("dzpath1"));
        DangerZoneRibbons ribbons = Assert.IsType<DangerZoneRibbons>(
            DangerZoneRibbons.Load(world, CampaignDangerZones.ReadDisabled(Scope(document))));
        Assert.False(ribbons.ByIndex(1)!.Active);
        Assert.Null(Load(document, world));

        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(world));
        CheckedReference disable = Assert.Single(diagnostics.Current.References.Where(item =>
            item.Reference.Rule == ReferenceRule.DangerOverridePath));
        CheckedReference gate = Assert.Single(diagnostics.Current.References.Where(item =>
            item.Reference.Rule == ReferenceRule.DangerGeometry));
        Assert.Equal(ReferenceState.Resolved, disable.State);
        Assert.Contains("selected objective", disable.Detail);
        Assert.Equal(ReferenceState.Suppressed, gate.State);
        Assert.Contains("disables", gate.Detail);

        CampaignMissionDangerZones binding = Bind(document, world);
        CampaignDangerZoneEntry entry = Find(binding, "dzpath1");
        Assert.True(binding.Apply(entry, entry.State with { Enabled = true }));
        gate = Assert.Single(diagnostics.Current.References.Where(item =>
            item.Reference.Rule == ReferenceRule.DangerGeometry));
        Assert.Equal(ReferenceState.Missing, gate.State);
        Assert.True(diagnostics.Current.HasErrors);

        binding = Bind(document, world);
        entry = Find(binding, "dzpath1");
        Assert.True(binding.Apply(entry, entry.State with { Enabled = false }));
        gate = Assert.Single(diagnostics.Current.References.Where(item =>
            item.Reference.Rule == ReferenceRule.DangerGeometry));
        Assert.Equal(ReferenceState.Suppressed, gate.State);
        Assert.False(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void NumericAliasesDoNotDisableTheDifferentlyNamedSelectedRibbon()
    {
        var world = new GameZ();
        world.Nodes.Add(new GameZNode { Index = 0, Name = "dzpath01", MeshIndex = -1 });
        AddZone(world, "dzpath1", 10f);
        CampaignMissionDocument document = Document(Zones(Row("disable", Row("dzpath01"))));
        CampaignDangerZoneEntry entry = Find(Bind(document, world), "dzpath01");
        Assert.False(entry.State.Enabled);
        Assert.False(entry.HasGates);
        Assert.Equal("dzpath1", Assert.IsType<CampaignDangerRibbon>(entry.Ribbon).Name);
        DangerZoneRibbons ribbons = Assert.IsType<DangerZoneRibbons>(
            DangerZoneRibbons.Load(world, CampaignDangerZones.ReadDisabled(Scope(document))));
        Assert.True(ribbons.ByIndex(1)!.Active);
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle(world));
        CheckedReference disable = Assert.Single(diagnostics.Current.References.Where(item =>
            item.Reference.Rule == ReferenceRule.DangerOverridePath));
        Assert.Equal(ReferenceState.InheritedMissing, disable.State);
    }

    internal static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, new ZrdrSet("mission", files));

    internal static ZrdrFile Zones(List<object?> root) => new("dzones.zrd", null, root);

    internal static ZrdrFile Objectives(List<object?> body) => new("objectives.zrd", null, Row(body));

    internal static List<object?> Row(params object?[] values) => new(values);

    internal static ZrdrSet Scope(CampaignMissionDocument document) => new("mission", document.Files);

    internal static CampaignDangerZoneWorld Project(GameZ? world) =>
        CampaignDangerZoneWorld.Read(Dependency<GameZ?>(
            world,
            world == null ? CampaignDependencyState.Unavailable : CampaignDependencyState.Ready));

    internal static CampaignMissionDangerZones Bind(CampaignMissionDocument document, GameZ world) =>
        CampaignMissionDangerZones.Bind(document, Project(world));

    internal static CampaignDangerZoneEntry Find(CampaignMissionDangerZones binding, string name) =>
        Assert.Single(binding.Entries.Where(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    internal static CampaignDangerZones? Load(CampaignMissionDocument document, GameZ world) =>
        CampaignDangerZones.Load(ObjectiveScript.Load(Scope(document)), world, Scope(document));

    internal static CheckedReference Ref(ReferenceSnapshot snapshot, string name) =>
        Assert.Single(snapshot.References.Where(item =>
            (item.Reference.Rule is ReferenceRule.DangerGeometry or ReferenceRule.DangerOverridePath)
            && string.Equals(item.Reference.Value.Name, name, StringComparison.OrdinalIgnoreCase)));

    internal static GameZ World(params string[] names)
    {
        var world = new GameZ();
        for (int i = 0; i < names.Length; i++)
        {
            AddZone(world, names[i], i * 30f);
        }

        return world;
    }

    internal static void AddZone(GameZ world, string name, float x)
    {
        var mesh = new GameZMesh();
        mesh.Vertices.AddRange(new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f),
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f),
            new Vector3(-1f, -1f, 10f), new Vector3(1f, -1f, 10f),
            new Vector3(1f, 1f, 10f), new Vector3(-1f, 1f, 10f),
        });
        AddPolygon(mesh, 1, 0, 1);
        AddPolygon(mesh, 2, 2, 3, 4, 5);
        AddPolygon(mesh, 2, 6, 7, 8, 9);
        int meshIndex = world.Meshes.Count;
        world.Meshes.Add(mesh);
        world.Nodes.Add(new GameZNode
        {
            Index = world.Nodes.Count,
            Name = name,
            MeshIndex = meshIndex,
            Local = new Transform3D(Basis.Identity, new Vector3(x, 0f, 0f)),
        });
    }

    private static void AddPolygon(GameZMesh mesh, int material, params int[] vertices)
    {
        var polygon = new GameZPolygon { MaterialIndex = material };
        polygon.VertexIndices.AddRange(vertices);
        mesh.Polygons.Add(polygon);
    }

    private static uint Bits(CampaignMissionDocument document, int file, params int[] path) =>
        BitConverter.SingleToUInt32Bits(Assert.IsType<float>(document.ValueAt(file, path)));

    private static CampaignMissionDependencyBundle Bundle(GameZ? world, ZrdrSet? chapter = null)
    {
        ZrdrSet shared = new("shared", Array.Empty<ZrdrFile>());
        ZrdrSet chapters = chapter ?? new ZrdrSet("chapter", Array.Empty<ZrdrFile>());
        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            Mission,
            Dependency(shared, CampaignDependencyState.Ready),
            Dependency<GameZ?>(world, world == null ? CampaignDependencyState.Unavailable : CampaignDependencyState.Ready),
            Dependency(new TextureSet("textures", Array.Empty<TextureImage>()), CampaignDependencyState.Ready),
            Dependency(chapters, CampaignDependencyState.Ready),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, true),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, true),
            Dependency(new Messages(), CampaignDependencyState.Ready, true),
            Dependency<InterpScripts?>(null, CampaignDependencyState.Missing, true),
            Dependency<MissionSetup?>(null, CampaignDependencyState.NotChecked, true),
            null);
    }

    private static CampaignDependency<T> Dependency<T>(T value, CampaignDependencyState state, bool optional = false) =>
        new(value, new CampaignDependencyAvailability("invented", state, optional));
}

/// <summary>Installed story scopes are measured through the same mission and chapter readers.</summary>
[Trait("Tier", "Data")]
public sealed class CampaignMissionDangerZoneCensusTests
{
    private readonly ITestOutputHelper _output;

    public CampaignMissionDangerZoneCensusTests(ITestOutputHelper output) => _output = output;

    [GameFilesFact]
    public void StoryOverridesAreMissionOnlyAndMatchRuntime()
    {
        var source = GameFilesData.Source!;
        GameData data = GameData.LoadCampaignEditor(source);
        IReadOnlyList<CampaignMission> missions = CampaignMissionDocument.LoadMissions(source);
        int local = 0;
        int absent = 0;
        int chapterOnly = 0;
        int bare = 0;
        int selected = 0;
        int suppressed = 0;
        var kinds = new Dictionary<CampaignDangerOverrideKind, int>();
        var shapes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (CampaignMission mission in missions)
        {
            CampaignMissionDocument document = CampaignMissionDocument.Load(source, mission);
            ChapterData chapter = data.LoadChapter(mission.ChapterFolder, mission.MissionFolder);
            GameZ world = Assert.IsType<GameZ>(chapter.Gamez);
            ZrdrSet chapterReaders = ZrdrSet.Load(source, $"ZBD/{mission.ChapterFolder}/zrdr.zbd");
            CampaignDangerZoneWorld projection = CampaignMissionDangerZonesTests.Project(world);
            CampaignMissionDangerZones binding = CampaignMissionDangerZones.Bind(document, projection);
            if (binding.FileIndex < 0)
            {
                absent++;
                chapterOnly += chapterReaders.HasFile("dzones.json") ? 1 : 0;
            }
            else
            {
                local++;
                List<object?> root = document.Files[binding.FileIndex].Root;
                foreach (string key in new[] { "disable", "nosnapshot", "objective_numbers" })
                {
                    int index = CampaignRawList.LastListValueIndex(root, key);
                    bare += index == -2 ? 1 : 0;
                    if (index >= 0)
                    {
                        CountShapes(key, (List<object?>)root[index]!, shapes);
                    }
                }

                foreach (CampaignDangerOccurrence occurrence in CampaignMissionDangerZones.Scan(root))
                {
                    kinds[occurrence.Kind] = kinds.GetValueOrDefault(occurrence.Kind) + 1;
                    selected += occurrence.Effective ? 1 : 0;
                    suppressed += occurrence.Effective ? 0 : 1;
                }
            }

            CampaignDangerZones? actual = CampaignMissionDangerZonesTests.Load(document, world);
            HashSet<string> disabled = CampaignDangerZones.ReadDisabled(CampaignMissionDangerZonesTests.Scope(document));
            foreach (CampaignDangerZoneEntry entry in binding.Entries)
            {
                Assert.Equal(!disabled.Contains(entry.Name), entry.State.Enabled);
                bool expected = !entry.ObjectiveBlocks.IsEmpty && entry.State.Enabled && entry.HasGates == true;
                int slot = -1;
                bool snapshot = false;
                bool armed = actual?.TryZone(entry.Name, out slot, out snapshot) == true;
                Assert.Equal(expected, armed);
                if (armed)
                {
                    Assert.Equal(entry.State.ObjectiveSlot is float number ? unchecked((int)number) : -1, slot);
                    Assert.Equal(entry.State.SnapshotAllowed, snapshot);
                }
            }
        }

        Assert.Equal(24, missions.Count);
        Assert.Equal(22, local);
        Assert.Equal(2, absent);
        _output.WriteLine($"Story readers: {local} local, {absent} absent, {chapterOnly} chapter-only, {bare} selected bare keys.");
        _output.WriteLine($"Typed occurrences: {selected} effective, {suppressed} suppressed.");
        foreach ((CampaignDangerOverrideKind kind, int count) in kinds.OrderBy(pair => pair.Key))
        {
            _output.WriteLine($"{kind}: {count} typed occurrences including suppressed duplicates.");
        }

        foreach ((string shape, int count) in shapes.OrderBy(pair => pair.Key))
        {
            _output.WriteLine($"{shape}: {count}");
        }
    }

    private static void CountShapes(string key, List<object?> values, Dictionary<string, int> counts)
    {
        foreach (object? item in values)
        {
            string shape = key == "objective_numbers"
                ? item is List<object?> { Count: >= 2 } pair && pair[0] is string && pair[1] is float
                    ? $"valid pair width {pair.Count}"
                    : "malformed pair or scalar"
                : item is string ? "direct string" : item is List<object?> ? "nested list" : "other value";
            string label = $"{key}: {shape}";
            counts[label] = counts.GetValueOrDefault(label) + 1;
        }
    }
}

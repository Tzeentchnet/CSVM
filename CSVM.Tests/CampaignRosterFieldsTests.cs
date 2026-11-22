using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Session.Campaign;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Exact-index roster field editing and runtime parity on invented readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignRosterFieldsTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    private static VehicleDefs Defs => VehicleDefs.FromRoot(Row(
        "basic_airplane", Props("mode", "jet", "nodename", "firebrand"),
        "player_airplane", Props("kind_of", "basic_airplane"),
        "fury", Props("kind_of", "basic_airplane"),
        "pfury", Props("kind_of", "player_airplane", "nodename", "player_fury"),
        "secfury", Props("kind_of", "fury"),
        "wingman", Props("kind_of", "fury", "mode", "wingman")));

    private static IReadOnlyList<AiNet> Nets => new[] { Net(5), Net(7) };

    [Fact]
    public void NetChoicesKeepOrderDuplicatesAndNonNumericEntries()
    {
        List<object?> fields = Fields(81);
        fields[0] = Row(5f, "opaque", 7f, null, 5f, Row("nested"));
        CampaignMissionDocument document = Document(("secfury_1", fields));
        CampaignRosterStart entry = Entry(document);
        CampaignRosterFieldGroup group = Group(entry, "AI net IDs");
        Assert.Equal(new[] { "net_0", "net_2", "net_4" }, group.Fields.Select(field => field.Key));
        Assert.Equal(new[] { 1, 1, 0, 2 }, Field(group, "net_2").Path);
        int notifications = 0;
        document.Changed += () => notifications++;

        Assert.Throws<ArgumentException>(() => entry.Apply(group, Values(entry, group,
            ("net_0", 7f), ("net_2", 5.5f))));
        Assert.Equal(0, document.Revision);
        Assert.Equal(5f, document.ValueAt(0, new[] { 1, 1, 0, 0 }));
        Assert.True(entry.Apply(group, Values(entry, group,
            ("net_0", 7f), ("net_2", 5f))));
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Equal("opaque", document.ValueAt(0, new[] { 1, 1, 0, 1 }));
        Assert.Null(document.ValueAt(0, new[] { 1, 1, 0, 3 }));
        Assert.Equal("nested", document.ValueAt(0, new[] { 1, 1, 0, 5, 0 }));

        List<object?> runtime = RuntimeFields(document);
        Assert.Equal(new[] { 7, 5, 5 }, AiSkills.RosterNetIds(runtime));
        Assert.Equal(7, Plan(document, draw: 0).Net!.Id);
        Assert.Equal(5, Plan(document, draw: 1).Net!.Id);
        Assert.Equal(5, Plan(document, draw: 2).Net!.Id);
    }

    [Fact]
    public void ScalarNetChangesTheWingmanForkWithoutChangingPlayerPlacement()
    {
        List<object?> player = Fields(42);
        player[0] = -1f;
        List<object?> wingman = Fields(42);
        wingman[0] = -1f;
        wingman[6] = "player";
        CampaignMissionDocument document = Document(
            ("player", player), ("wingman_1", wingman));
        PlayerStart? before = SpawnPoints.LoadPlayerInit(Scope(document));
        Assert.True(before.HasValue);
        Assert.Equal(new Vector3(25f, 50f, 75f), before.Value.Spawn.Position);
        Assert.Equal(90f, before.Value.Spawn.HeadingDeg);
        CampaignRosterStart human = Entry(document, 0);
        Assert.True(human.IsPlayer);
        Assert.Empty(human.Groups);
        Assert.Throws<InvalidOperationException>(() => human.Apply(human.State with { X = 100f }));
        Assert.Equal(0, document.Revision);

        CampaignRosterStart entry = Entry(document, 1);
        Assert.True(Plan(document, "wingman_1").Escorts);
        CampaignRosterFieldGroup net = Group(entry, "AI net IDs");
        Assert.True(entry.Apply(net, Values(entry, net, ("net", 5f))));
        Assert.False(Plan(document, "wingman_1").Escorts);
        Assert.Equal(5, Plan(document, "wingman_1").Net!.Id);
        Assert.Equal(before, SpawnPoints.LoadPlayerInit(Scope(document)));
        Assert.Equal(1f, document.ValueAt(0, new[] { 1, 1, 1, 0 }));
        Assert.Contains(CampaignRosterPlan.Build(AiSkills.LoadRoster(Scope(document)), Defs, Nets).Skipped,
            skipped => skipped.Name == "player");
    }

    [Fact]
    public void TemplateNetChoiceStillSuppliesPlanVolumes()
    {
        List<object?> fields = Fields(42, enabled: 0f);
        fields[0] = Row(5f, 7f);
        CampaignMissionDocument document = Document(("secfury_1", fields));
        RosterSpawnPlan before = CampaignRosterPlan.GeneratorTemplates(Scope(document), Defs, Nets)["label_0"];
        Assert.Equal(500f, before.Volumes.Activation.Radius);
        Apply(document, "AI net IDs", ("net_0", 7f), ("net_1", 5f));
        RosterSpawnPlan after = CampaignRosterPlan.GeneratorTemplates(Scope(document), Defs, Nets)["label_0"];
        Assert.Equal(700f, after.Volumes.Activation.Radius);
        Assert.Equal(7, after.Net!.Id);
    }

    [Fact]
    public void DurabilityActivationSkillsAndManeuversReachTheRosterPlan()
    {
        CampaignMissionDocument document = Document(("secfury_1", Fields(81)));
        Apply(document, "Durability", ("health", 180f), ("armor", 0f));
        RosterSpawnPlan plan = Plan(document);
        Assert.Equal(180f, plan.InitHealth);
        Assert.Equal(0f, plan.Armor);
        Apply(document, "Durability", ("health", 0f), ("armor", -1f));
        Assert.Null(Plan(document).InitHealth);
        Assert.Null(Plan(document).Armor);

        Apply(document, "Activation and nitro", ("deactivated", 1f), ("nitro", 1f));
        plan = Plan(document);
        Assert.True(plan.Inert);
        Assert.True(plan.Nitro);
        Assert.True(AiSkills.RosterDeactivated(RuntimeFields(document)));
        Assert.True(AiSkills.RosterNitro(RuntimeFields(document)));

        Apply(document, "Pilot ratings",
            ("skill_0", 0f), ("skill_1", 1f), ("skill_2", 2f),
            ("skill_3", 3f), ("skill_4", 4f), ("skill_5", 5f),
            ("skill_6", 6f), ("skill_7", 7f), ("skill_8", 9f), ("ace", 1f));
        plan = Plan(document);
        Assert.Equal(0, plan.Skills.DareDevil);
        Assert.Equal(1, plan.Skills.NaturalTouch);
        Assert.Equal(2, plan.Skills.SixthSense);
        Assert.Equal(3, plan.Skills.DeadEye);
        Assert.Equal(4, plan.Skills.QuickDraw);
        Assert.Equal(5, plan.Skills.SteadyHand);
        Assert.Equal(6, plan.Skills.StunRecovery);
        Assert.Equal(7, plan.Skills.Talker);
        Assert.Equal(9, plan.Skills.Constitution);
        Assert.True(plan.Ace);
        AiSkillVector read = AiSkills.RosterSkills(RuntimeFields(document));
        Assert.Equal(plan.Skills.DareDevil, read.DareDevil);
        Assert.Equal(plan.Skills.Constitution, read.Constitution);
        Apply(document, "Pilot ratings", ("skill_0", -1f), ("ace", 0f));
        Assert.Null(Plan(document).Skills.DareDevil);
        Assert.False(Plan(document).Ace);

        Apply(document, "Signature maneuvers", ("signature", 2049f));
        plan = Plan(document);
        Assert.Equal(2049L, plan.SignatureMask);
        Assert.Equal(new[] { "roll", "split_s" }, Maneuvers.SignatureNames(plan.SignatureMask));
    }

    [Fact]
    public void TargetMarkerPathAndVoiceFieldsReachTheirRuntimeReaders()
    {
        List<object?> fields = Fields(81);
        fields[40] = "pp1";
        CampaignMissionDocument document = Document(("secfury_1", fields));
        Apply(document, "Target and display", ("primary", "player"), ("title", "MSG_PILOT"));
        RosterSpawnPlan plan = Plan(document);
        Assert.Equal("player", plan.LeaderName);
        Assert.Equal("MSG_PILOT", plan.Title);
        Assert.Equal("player", AiSkills.RosterPrimaryTarget(RuntimeFields(document)));
        Assert.Equal("MSG_PILOT", AiSkills.RosterTitle(RuntimeFields(document)));

        CampaignRosterStart entry = Entry(document);
        CampaignRosterFieldGroup marker = Group(entry, "Objective marker");
        Assert.Equal(new[] { "marker" }, marker.Fields.Select(field => field.Key));
        Assert.Equal("designer text", document.ValueAt(0, new[] { 1, 1, 39 }));
        Assert.True(entry.Apply(marker, Values(entry, marker, ("marker", 1f))));
        entry = Entry(document);
        marker = Group(entry, "Objective marker");
        Assert.Equal(new[] { "marker", "category", "help" }, marker.Fields.Select(field => field.Key));
        Assert.Throws<ArgumentException>(() => entry.Apply(marker, Values(entry, marker,
            ("marker", 0f), ("help", "MSG_NEW"))));
        Assert.True(entry.Apply(marker, Values(entry, marker,
            ("category", "MSG_TYPE"), ("help", "MSG_FOLLOW"))));
        plan = Plan(document);
        Assert.True(plan.ObjectiveTarget);
        Assert.Equal("MSG_TYPE", plan.CategoryLabel);
        Assert.Equal("MSG_FOLLOW", plan.HelpLabel);

        Apply(document, "Scripted path", ("taxi", "pp2"));
        Apply(document, "Voice", ("accent", 12f));
        plan = Plan(document);
        Assert.Equal("pp2", plan.TaxiPath);
        Assert.Equal(12, plan.AccentId);
        Assert.Equal(new[] { 12 }, CombatVoice.MissionAccentIds(Scope(document)));
    }

    [Theory]
    [InlineData("AI net IDs", "net_0", 7f)]
    [InlineData("Durability", "health", 180f)]
    [InlineData("Activation and nitro", "deactivated", 1f)]
    [InlineData("Pilot ratings", "skill_0", 7f)]
    [InlineData("Signature maneuvers", "signature", 2049f)]
    [InlineData("Target and display", "primary", "player")]
    [InlineData("Objective marker", "marker", 1f)]
    [InlineData("Scripted path", "taxi", "pp2")]
    [InlineData("Voice", "accent", 12f)]
    public void EveryGroupChangesOnlyItsExactLeaf(string groupName, string key, object value)
    {
        List<object?> fields = Fields(81);
        fields[0] = Row(5f, "opaque-net", 5f, Row(null, -0f));
        fields[8] = -0f;
        fields[33] = Row(Row("pattern", -1f, "opaque-third"));
        fields[40] = "pp1";
        fields[80] = Row("opaque-tail", -0f, null);
        var blocks = new[] { ("secfury_1", fields), ("secfury_1", Fields(42)) };
        CampaignMissionDocument document = Document(blocks);
        CampaignMissionDocument expected = Document(blocks);
        CampaignRosterStart entry = Entry(document);
        CampaignRosterFieldGroup group = Group(entry, groupName);
        CampaignRosterField field = Field(group, key);

        Assert.True(expected.SetLeaf(0, field.Path, value));
        Assert.True(entry.Apply(group, Values(entry, group, (key, value))));
        Assert.Equal(expected.Files.Count, document.Files.Count);
        for (int i = 0; i < expected.Files.Count; i++)
        {
            Assert.Equal(expected.Files[i].Name, document.Files[i].Name);
            Assert.Equal(expected.Files[i].Rename, document.Files[i].Rename);
            EqualTree(expected.Files[i].Root, document.Files[i].Root);
        }
    }

    [Theory]
    [InlineData("Durability", "health", 30f, "armor", -2f)]
    [InlineData("Activation and nitro", "deactivated", 1f, "nitro", 2f)]
    [InlineData("Pilot ratings", "skill_0", 5f, "skill_8", 10f)]
    [InlineData("Pilot ratings", "skill_0", 5f, "ace", 0.5f)]
    [InlineData("Signature maneuvers", "signature", 131072f, "signature", 131072f)]
    [InlineData("Target and display", "primary", "player", "title", " bad ")]
    [InlineData("Objective marker", "marker", 2f, "marker", 2f)]
    [InlineData("Scripted path", "taxi", "bad\npath", "taxi", "bad\npath")]
    [InlineData("Voice", "accent", 2.5f, "accent", 2.5f)]
    public void InvalidGroupValuesNeverPartiallyMutate(
        string groupName, string firstKey, object firstValue, string invalidKey, object invalidValue)
    {
        List<object?> fields = Fields(81);
        fields[40] = "pp1";
        CampaignMissionDocument document = Document(("secfury_1", fields));
        CampaignRosterStart entry = Entry(document);
        CampaignRosterFieldGroup group = Group(entry, groupName);
        object[] original = Values(entry, group);
        object[] proposed = Values(entry, group, (firstKey, firstValue), (invalidKey, invalidValue));
        int notifications = 0;
        document.Changed += () => notifications++;

        Assert.Throws<ArgumentException>(() => entry.Apply(group, proposed));
        Assert.Equal(original, Values(entry, group));
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void MixedWidthsAndMalformedSlotsBindIndependentlyAndPreserveUnknownData()
    {
        int[] widths = { 42, 65, 66, 67, 68, 81 };
        var records = new List<(string, List<object?>)>();
        foreach (int width in widths)
        {
            List<object?> fields = Fields(width);
            fields[25] = "malformed skill";
            fields[32] = 131072f;
            fields[33] = Row(Row("pattern", -1f, "opaque-third"), null);
            records.Add(($"secfury_{width}", fields));
        }

        CampaignMissionDocument document = Document(records.ToArray());
        Assert.True(CampaignRosterStarts.TryBind(document, out CampaignRosterStarts? roster, out string problem), problem);
        Assert.Equal(widths.Length, roster.Entries.Count);
        for (int i = 0; i < widths.Length; i++)
        {
            CampaignRosterStart entry = roster.Entries[i];
            Assert.DoesNotContain(Group(entry, "Pilot ratings").Fields, field => field.Key == "skill_3");
            Assert.DoesNotContain(entry.Groups, group => group.Label == "Signature maneuvers");
            Assert.Equal(widths[i] > 65, entry.Groups.Any(group => group.Label == "Voice"));
            Assert.Equal(widths[i] > 66, Group(entry, "Durability").Fields.Any(field => field.Key == "armor"));
            Assert.Equal(widths[i] > 67, Group(entry, "Pilot ratings").Fields.Any(field => field.Key == "ace"));
            Assert.Equal(widths[i], Assert.IsType<List<object?>>(document.ValueAt(0, new[] { i + 1, 1 })).Count);
        }

        CampaignRosterStart selected = roster.Entries[0];
        CampaignRosterFieldGroup durability = Group(selected, "Durability");
        Assert.True(selected.Apply(durability, Values(selected, durability, ("health", 42f))));
        Assert.Equal("malformed skill", document.ValueAt(0, new[] { 1, 1, 25 }));
        Assert.Equal(131072f, document.ValueAt(0, new[] { 1, 1, 32 }));
        Assert.Equal("opaque-third", document.ValueAt(0, new[] { 1, 1, 33, 0, 2 }));
        Assert.Null(document.ValueAt(0, new[] { 1, 1, 33, 1 }));
        Assert.Equal(0f, document.ValueAt(0, new[] { 2, 1, 7 }));
    }

    [Fact]
    public void MissingOptionalFieldsAndAnOpaqueNetListStayAbsent()
    {
        List<object?> fields = Row(Row(null, "opaque"), Row(1f, 2f, 3f), 4f, 1f, 0f, 1f);
        CampaignMissionDocument document = Document(("secfury_1", fields));
        CampaignRosterStart entry = Entry(document);
        Assert.Empty(entry.Groups);
        Assert.True(entry.Apply(entry.State with { X = 8f }));
        Assert.Equal(6, Assert.IsType<List<object?>>(document.ValueAt(0, new[] { 1, 1 })).Count);
        Assert.Null(document.ValueAt(0, new[] { 1, 1, 0, 0 }));
        Assert.Equal("opaque", document.ValueAt(0, new[] { 1, 1, 0, 1 }));
    }

    [Fact]
    public void DuplicateNamesAndTemplateLabelsKeepRawOrderAndLastPlannableWinner()
    {
        List<object?> first = Fields(42, enabled: 0f);
        List<object?> second = Fields(68, enabled: 0f);
        List<object?> invalid = Fields(42, enabled: 0f);
        first[22] = 2f;
        second[22] = 7f;
        var root = Row(
            Row(3f, 10f, "same", 11f, "SAME", 12f, "same"),
            Row("secfury_1", first),
            Row("secfury_1", second),
            Row("unmatched_1", invalid));
        CampaignMissionDocument document = DocumentFromFiles(new ZrdrFile("aiv.zrd", null, root));
        Assert.True(CampaignRosterStarts.TryBind(document, out CampaignRosterStarts? roster, out string problem), problem);
        Assert.Equal(new[] { 1, 2, 3 }, roster.Entries.Select(item => item.RawIndex));
        Assert.Equal(new[] { "same", "SAME", "same" }, roster.Entries.Select(item => item.TemplateLabel));
        Assert.Equal(3, AiSkills.LoadRoster(Scope(document)).Count);
        Assert.Equal(3, AiSkills.LoadGeneratorRoster(Scope(document)).Count);
        var templates = CampaignRosterPlan.GeneratorTemplates(Scope(document), Defs, Nets);
        Assert.Single(templates);
        Assert.Equal(7, templates["same"].Skills.DareDevil);

        CampaignRosterStart selected = roster.Entries[1];
        CampaignRosterFieldGroup firstSkills = Group(roster.Entries[0], "Pilot ratings");
        Assert.Throws<ArgumentException>(() => selected.Apply(firstSkills,
            Values(roster.Entries[0], firstSkills, ("skill_0", 9f))));
        Assert.Equal(0, document.Revision);
        CampaignRosterFieldGroup skills = Group(selected, "Pilot ratings");
        Assert.True(selected.Apply(skills, Values(selected, skills, ("skill_0", 9f))));
        Assert.Equal(2f, document.ValueAt(0, new[] { 1, 1, 22 }));
        Assert.Equal(9, CampaignRosterPlan.GeneratorTemplates(Scope(document), Defs, Nets)["SAME"].Skills.DareDevil);
        Assert.Equal("SAME", document.ValueAt(0, new[] { 0, 4 }));
        Assert.Equal(0f, document.ValueAt(0, new[] { 2, 1, 5 }));
    }

    [Fact]
    public void AStaleBindingAndAChangedPrefixCannotPartiallyWrite()
    {
        List<object?> fields = Fields(81);
        fields[3] = -0f;
        CampaignMissionDocument document = Document(("secfury_1", fields));
        CampaignRosterStart entry = Entry(document);
        Assert.False(entry.Apply(entry.State));
        Assert.Equal(0x80000000u, Bits(document, 1, 1, 3));
        CampaignRosterFieldGroup skills = Group(entry, "Pilot ratings");
        object[] values = Values(entry, skills, ("skill_0", 9f));
        Assert.True(document.SetLeaf(0, new[] { 1, 1, 7 }, 10f));
        Assert.Throws<InvalidOperationException>(() => entry.Apply(skills, values));
        Assert.Equal(-1f, document.ValueAt(0, new[] { 1, 1, 22 }));
        Assert.Equal(1, document.Revision);

        CampaignRosterStart current = Entry(document);
        Assert.True(current.Apply(current.State with { X = 12f, Y = 13f, Team = 2 }));
        Assert.Equal(2, document.Revision);
        Assert.Equal(new Vector3(12f, 13f, 3f), AiSkills.RosterSpawnPose(RuntimeFields(document))!.Value.Position);
    }

    [Fact]
    public void MalformedRosterRowsAndLateBatchPathErrorsNeverPartiallyWrite()
    {
        CampaignMissionDocument document = Document(("secfury_1", Fields(42)));
        var invalidPath = new (IReadOnlyList<int> Path, object Value)[]
        {
            (new[] { 1, 1, 7 }, 10f),
            (new[] { 1, 1, 999 }, 20f),
        };
        var duplicatePath = new (IReadOnlyList<int> Path, object Value)[]
        {
            (new[] { 1, 1, 7 }, 10f),
            (new[] { 1, 1, 7 }, 20f),
        };
        Assert.Throws<InvalidOperationException>(() => document.SetRosterLeaves(0, 0, invalidPath));
        Assert.Throws<InvalidOperationException>(() => document.SetRosterLeaves(0, 0, duplicatePath));
        Assert.Equal(0f, document.ValueAt(0, new[] { 1, 1, 7 }));
        Assert.Equal(0, document.Revision);

        CampaignMissionDocument malformed = DocumentFromFiles(new ZrdrFile("aiv.zrd", null, Row(
            Row(2f, 1f, "good", 2f, "bad"),
            Row("secfury_1", Fields(42)),
            Row("secfury_2", Row(5f, Row(0f, 0f), 0f, 0f, 0f, 1f)))));
        Assert.False(CampaignRosterStarts.TryBind(malformed, out _, out string problem));
        Assert.Contains("position", problem);
        Assert.Equal(0, malformed.Revision);
    }

    [Fact]
    public void CompleteScopeRoundTripPreservesTailsAndFloatBitsAfterAnEdit()
    {
        List<object?> fields = Fields(81);
        fields[8] = -0f;
        fields[33] = Row(Row("pattern", -1f, "third"), null);
        fields.Add(Row(-0f, BitConverter.UInt32BitsToSingle(1), null, "tail"));
        CampaignMissionDocument document = Document(("secfury_1", fields));
        string root = Path.Combine(TestData.RepoRoot, ".scratch", "c27-roster-" + Guid.NewGuid().ToString("N"));
        try
        {
            string archive = document.Export(root);
            ZrdrSet reopened = ZrdrSet.FromExtracted(archive);
            Assert.Equal(0, document.Revision);
            EqualScope(document, reopened);

            Apply(document, "Durability", ("health", 50f), ("armor", 0f));
            archive = document.Export(root);
            reopened = ZrdrSet.FromExtracted(archive);
            EqualScope(document, reopened);
            List<object?> runtime = Assert.Single(AiSkills.LoadRoster(reopened)).Fields;
            Assert.Equal(50f, AiSkills.RosterInitHealth(runtime));
            Assert.Equal(0f, AiSkills.RosterArmor(runtime));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void EqualScope(CampaignMissionDocument document, ZrdrSet reopened)
    {
        Assert.Equal(document.Files.Select(file => file.Key), reopened.Files.Select(file => file.Key));
        for (int i = 0; i < document.Files.Count; i++)
        {
            EqualTree(document.Files[i].Root, reopened.Files[i].Root);
        }
    }

    private static void EqualTree(object? expected, object? actual)
    {
        if (expected is List<object?> left)
        {
            List<object?> right = Assert.IsType<List<object?>>(actual);
            Assert.Equal(left.Count, right.Count);
            for (int i = 0; i < left.Count; i++)
            {
                EqualTree(left[i], right[i]);
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

    private static void Apply(CampaignMissionDocument document, string name, params (string Key, object Value)[] edits)
    {
        CampaignRosterStart entry = Entry(document);
        CampaignRosterFieldGroup group = Group(entry, name);
        Assert.True(entry.Apply(group, Values(entry, group, edits)));
    }

    private static CampaignRosterStart Entry(CampaignMissionDocument document, int index = 0)
    {
        Assert.True(CampaignRosterStarts.TryBind(document, out CampaignRosterStarts? roster, out string problem), problem);
        return roster.Entries[index];
    }

    private static CampaignRosterFieldGroup Group(CampaignRosterStart entry, string name) =>
        Assert.Single(entry.Groups.Where(group => group.Label == name));

    private static CampaignRosterField Field(CampaignRosterFieldGroup group, string key) =>
        Assert.Single(group.Fields.Where(field => field.Key == key));

    private static object[] Values(
        CampaignRosterStart entry, CampaignRosterFieldGroup group, params (string Key, object Value)[] changes)
    {
        foreach (var change in changes)
        {
            Assert.Contains(group.Fields, field => field.Key == change.Key);
        }

        return group.Fields.Select(field => changes.LastOrDefault(change => change.Key == field.Key) is var change
            && change.Key != null ? change.Value : entry.Value(field)).ToArray();
    }

    private static CampaignMissionDocument Document(params (string Name, List<object?> Fields)[] blocks)
    {
        var header = Row((float)blocks.Length);
        var root = Row(header);
        for (int i = 0; i < blocks.Length; i++)
        {
            header.Add((float)i);
            header.Add($"label_{i}");
            root.Add(Row(blocks[i].Name, blocks[i].Fields));
        }

        return DocumentFromFiles(
            new ZrdrFile("aiv.zrd", null, root),
            new ZrdrFile("objectives.zrd", null,
                Row(Row("PLAYER_INIT", Row(1f, Row(25f, 50f, 75f), Row(0f, 90f, 0f), 0.8f, 100f)))));
    }

    private static CampaignMissionDocument DocumentFromFiles(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, new ZrdrSet("invented", files));

    private static ZrdrSet Scope(CampaignMissionDocument document) => new("mission", document.Files);

    private static List<object?> RuntimeFields(CampaignMissionDocument document) =>
        Assert.Single(AiSkills.LoadRoster(Scope(document))).Fields;

    private static RosterSpawnPlan Plan(CampaignMissionDocument document, string name = "secfury_1", int draw = 0) =>
        Assert.Single(CampaignRosterPlan.Build(AiSkills.LoadRoster(Scope(document)), Defs, Nets,
            netDraw: _ => draw).Spawns.Where(spawn => spawn.Name == name));

    private static AiNet Net(int id) => new()
    {
        Id = id,
        Name = $"net{id}",
        Nodes = new[] { new AiNetNode(Vector3.Zero, Array.Empty<float>()) },
        Edges = Array.Empty<(int, int)>(),
        Volumes = new AiVolumeSet(new AiVolume(id * 100f, 0f, 0f), default, default),
    };

    private static List<object?> Props(params object?[] pairs)
    {
        var result = new List<object?>();
        for (int i = 0; i + 1 < pairs.Length; i += 2)
        {
            result.Add(pairs[i]);
            result.Add(Row(pairs[i + 1]));
        }

        return result;
    }

    private static List<object?> Fields(int width, float enabled = 1f)
    {
        var result = Enumerable.Repeat<object?>(-1f, width).ToList();
        result[0] = 5f;
        result[1] = Row(1f, 2f, 3f, "position-tail");
        result[2] = 4f;
        result[3] = 1f;
        result[4] = 0f;
        result[5] = enabled;
        result[6] = "";
        result[7] = 0f;
        for (int i = 8; i <= 19; i++)
        {
            result[i] = 0f;
        }

        result[20] = "";
        result[21] = 0f;
        result[32] = 0f;
        result[33] = null;
        result[34] = 0f;
        result[37] = 0f;
        result[38] = "";
        result[39] = "designer text";
        result[40] = 0f;
        return result;
    }

    private static uint Bits(CampaignMissionDocument document, params int[] path) =>
        BitConverter.SingleToUInt32Bits(Assert.IsType<float>(document.ValueAt(0, path)));

    private static List<object?> Row(params object?[] values) => new(values);
}

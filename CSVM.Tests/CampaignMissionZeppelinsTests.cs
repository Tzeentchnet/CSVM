using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSVM.Flight.Ai;
using CSVM.Flight.Airframe;
using CSVM.Mech3;
using CSVM.Session.World;
using CSVM.Tooling;
using Godot;
using Xunit;
using Xunit.Abstractions;

namespace CSVM.Tests;

/// <summary>Exact zeppelin bindings and transactions over invented mission readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionZeppelinsTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void PlacementWritesSelectedLeavesAtomicallyAndPreservesAllOtherBits()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        var record = Row(
            "node", Row("old_hull", "old-tail"),
            "NODE", Row("hull", "node-tail"),
            "position", Row(1f, 2f, -0f),
            "net", Row("old_route"),
            "NET", Row("route", "net-tail"),
            "yaw", Row(10f, subnormal, "yaw-tail"),
            "pitch", Row(4f),
            "max_speed", Row(12f),
            "min_pitch", Row(-30f),
            "max_pitch", Row(30f),
            "unknown", Row(-0f, subnormal, null, Row("targets", Row("keep"))));
        CampaignMissionDocument document = Document(Reader(Row(null, record, 8f)));
        CampaignMissionZeppelins binding = CampaignMissionZeppelins.Bind(document);
        CampaignZeppelinEntry entry = Assert.Single(binding.Entries);
        CampaignZeppelinRow row = One(entry, "Placement and motion");
        int notifications = 0;
        document.Changed += () => notifications++;

        Assert.Equal(1, entry.RawIndex);
        Assert.Equal(new[] { 0, 1, 3, 0 }, Field(row, "Hull node").Path);
        Assert.Equal(new[] { 0, 1, 9, 0 }, Field(row, "Route net").Path);
        Assert.Equal(new[] { 0, 1, 5, 2 }, Field(row, "Position Z (m)").Path);
        Assert.DoesNotContain(row.Fields, field => field.Label.Contains("bounds", StringComparison.OrdinalIgnoreCase));
        object[] original = Values(binding, row);
        Assert.False(binding.Apply(row, original));
        Assert.Equal(0, document.Revision);

        Assert.True(binding.Apply(row, Values(binding, row, new()
        {
            ["Position X (m)"] = 101f,
            ["Position Y (m, up)"] = 202f,
            ["Yaw (degrees)"] = -90f,
            ["Route net"] = "new_route",
        })));

        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Equal("old_hull", document.ValueAt(0, new[] { 0, 1, 1, 0 }));
        Assert.Equal("node-tail", document.ValueAt(0, new[] { 0, 1, 3, 1 }));
        Assert.Equal("old_route", document.ValueAt(0, new[] { 0, 1, 7, 0 }));
        Assert.Equal("net-tail", document.ValueAt(0, new[] { 0, 1, 9, 1 }));
        Assert.Equal(1u, Bits(document, 0, 1, 11, 1));
        Assert.Equal("yaw-tail", document.ValueAt(0, new[] { 0, 1, 11, 2 }));
        Assert.Equal(0x80000000u, Bits(document, 0, 1, 5, 2));
        Assert.Equal(0x80000000u, Bits(document, 0, 1, 21, 0));
        Assert.Equal(1u, Bits(document, 0, 1, 21, 1));
        Assert.Null(document.ValueAt(0, new[] { 0, 1, 21, 2 }));
        Assert.Throws<InvalidOperationException>(() => binding.Apply(row, original));

        ZeppelinDef actual = Assert.Single(Load(document));
        Assert.Equal("hull", actual.Node);
        Assert.Equal("new_route", actual.Net);
        Assert.Equal(new Vector3(101f, 202f, -0f), actual.Position);
        Assert.Equal(-90f, actual.YawDeg);
        Assert.Equal(4f, actual.PitchDeg);
        Assert.Equal(-30f, actual.MinPitchDeg);
        Assert.Equal(30f, actual.MaxPitchDeg);
    }

    [Fact]
    public void OptionalAndVariableWidthRowsFollowTheRuntimeAndPreserveTails()
    {
        var record = Row(
            "node", Row("hull"),
            "position", Row(0f, 10f, 20f),
            "net", Row("route"),
            "team", Row("Enemy", "team-tail"),
            "deactivated", Row(7f, "flag-tail"),
            "num_healthy_required", Row(9f),
            "healthy", Row(Row("bag", "panels", "healthy-tail"), Row("bag", "panels"), Row("bad"), null),
            "targets", Row("player", Row("not-a-target"), null, "other"),
            "engines", Row("engine", 1f, Row("not-an-engine")),
            "gasbags", Row(Row("bag", 100f, Row("destroy", "animation-tail"), "bag-tail"), Row("bad", "hp"), Row("bare", 20f)),
            "left_cannons", Row(Row("left", "deploy", "retract", "cannon-tail"), Row("bad", "deploy")),
            "right_cannons", Row(Row("right", "out", "in")),
            "cannon_health", Row(Row("left", "unused-gunback", "unused-frame", "bag", 200f,
                Row("dead", "destroy-tail"), Row(Row(0.6f, "sixty", "stage-tail"), Row(0.3f, 3f), null), "health-tail")),
            "cannon_inaccuracy", Row(10f));
        CampaignMissionDocument document = Document(Reader(Row(record)));
        CampaignMissionZeppelins binding = CampaignMissionZeppelins.Bind(document);
        CampaignZeppelinEntry entry = Assert.Single(binding.Entries);
        Assert.Equal(2, Section(entry, "Healthy zones").Rows.Count);
        Assert.Equal(2, Section(entry, "Broadside targets").Rows.Count);
        Assert.Single(Section(entry, "Engines").Rows);
        Assert.Equal(2, Section(entry, "Gasbags").Rows.Count);
        Assert.Single(Section(entry, "Left cannons").Rows);
        Assert.Single(Section(entry, "Cannon damage").Rows);
        CampaignZeppelinRow damage = One(entry, "Cannon damage");
        Assert.Equal(6, damage.Fields.Count);
        Assert.DoesNotContain(damage.Fields, field => field.Label.Contains("gunback", StringComparison.OrdinalIgnoreCase));

        ZeppelinDef before = Assert.Single(Load(document));
        Assert.Equal(2, before.NumHealthyRequired);
        Assert.True(before.Deactivated);
        Assert.Equal(new[] { "player", "other" }, before.Targets);
        Assert.Equal(2, before.Gasbags.Count);
        Assert.Single(before.CannonHealth[0].Stages);

        CampaignZeppelinRow behavior = One(entry, "Team and behavior");
        Assert.True(binding.Apply(behavior, Values(binding, behavior, new() { ["Team name"] = "ally" })));
        binding = CampaignMissionZeppelins.Bind(document);
        entry = Assert.Single(binding.Entries);
        CampaignZeppelinRow gasbag = Section(entry, "Gasbags").Rows[0];
        Assert.True(binding.Apply(gasbag, Values(binding, gasbag, new()
        {
            ["Hit points"] = 125f,
            ["Destroy animation"] = "new_destroy",
        })));
        binding = CampaignMissionZeppelins.Bind(document);
        entry = Assert.Single(binding.Entries);
        damage = One(entry, "Cannon damage");
        Assert.True(binding.Apply(damage, Values(binding, damage, new()
        {
            ["Associated gasbag"] = "other_bag",
            ["Stage #0 fraction"] = 0.4f,
            ["Stage #0 animation"] = "new_stage",
        })));

        ZeppelinDef actual = Assert.Single(Load(document));
        Assert.Equal("ally", actual.Team);
        Assert.True(actual.Deactivated);
        Assert.Equal(2, actual.NumHealthyRequired);
        Assert.Equal(125f, actual.Gasbags[0].Hp);
        Assert.Equal("new_destroy", actual.Gasbags[0].DestroyAnim);
        Assert.Equal("other_bag", actual.CannonHealth[0].Gasbag);
        Assert.Equal(new[] { (0.4f, "new_stage") }, actual.CannonHealth[0].Stages);
        Assert.Equal("team-tail", document.ValueAt(0, new[] { 0, 0, 7, 1 }));
        Assert.Equal(7f, document.ValueAt(0, new[] { 0, 0, 9, 0 }));
        Assert.Equal("animation-tail", document.ValueAt(0, new[] { 0, 0, 19, 0, 2, 1 }));
        Assert.Equal("bag-tail", document.ValueAt(0, new[] { 0, 0, 19, 0, 3 }));
        Assert.Equal("unused-gunback", document.ValueAt(0, new[] { 0, 0, 25, 0, 1 }));
        Assert.Equal("destroy-tail", document.ValueAt(0, new[] { 0, 0, 25, 0, 5, 1 }));
        Assert.Equal("stage-tail", document.ValueAt(0, new[] { 0, 0, 25, 0, 6, 0, 2 }));
        Assert.Equal("health-tail", document.ValueAt(0, new[] { 0, 0, 25, 0, 7 }));
    }

    [Fact]
    public void InvalidProposalsDoNotWriteAnyLeafOrNotify()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("hull"),
            "position", Row(1f, 2f, 3f),
            "net", Row("route"),
            "team", Row(2f),
            "deactivated", Row(0f),
            "healthy", Row(Row("bag", "panels")),
            "num_healthy_required", Row(1f)))));
        CampaignMissionZeppelins binding = CampaignMissionZeppelins.Bind(document);
        CampaignZeppelinEntry entry = Assert.Single(binding.Entries);
        CampaignZeppelinRow placement = One(entry, "Placement and motion");
        CampaignZeppelinRow behavior = One(entry, "Team and behavior");
        int events = 0;
        document.Changed += () => events++;

        Assert.Throws<ArgumentException>(() => binding.Apply(placement, Values(binding, placement, new()
        {
            ["Position X (m)"] = 50f,
            ["Route net"] = " bad ",
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(placement, Values(binding, placement, new()
        {
            ["Position X (m)"] = 50f,
            ["Route net"] = "bad\tname",
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(behavior, Values(binding, behavior, new()
        {
            ["Team ID"] = 1.5f,
            ["Deactivated (0 or 1)"] = 1f,
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(behavior, Values(binding, behavior, new()
        {
            ["Survivors required"] = 2f,
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(placement, Values(binding, placement, new()
        {
            ["Position X (m)"] = float.PositiveInfinity,
        })));
        Assert.Throws<InvalidOperationException>(() => document.SetZeppelinLeaves(0, 0, new[]
        {
            ((IReadOnlyList<int>)new[] { 0, 0, 3, 0 }, (object)50f),
            ((IReadOnlyList<int>)new[] { 0, 0, 5, 0 }, (object)12f),
        }));
        Assert.Throws<InvalidOperationException>(() => document.SetZeppelinLeaves(0, 0, new[]
        {
            ((IReadOnlyList<int>)new[] { 0, 0, 3, 0 }, (object)50f),
            ((IReadOnlyList<int>)new[] { 0, 0, 3, 0 }, (object)51f),
        }));
        CampaignMissionZeppelins foreign = CampaignMissionZeppelins.Bind(Document(Reader(Row(Row(
            "node", Row("other"), "position", Row(0f, 0f, 0f), "net", Row("route"))))));
        CampaignZeppelinRow foreignRow = One(Assert.Single(foreign.Entries), "Placement and motion");
        Assert.Throws<ArgumentException>(() => binding.Apply(foreignRow, Values(foreign, foreignRow)));

        Assert.Equal(1f, document.ValueAt(0, new[] { 0, 0, 3, 0 }));
        Assert.Equal("route", document.ValueAt(0, new[] { 0, 0, 5, 0 }));
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, events);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void EditedPoseAndLimitsDriveMotionInAuthoredAxesAndUnits()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("hull"),
            "position", Row(10f, 20f, 30f),
            "net", Row("route"),
            "yaw", Row(0f),
            "pitch", Row(0f),
            "max_speed", Row(10f),
            "max_accel", Row(4f),
            "min_pitch", Row(-1f),
            "max_pitch", Row(1f),
            "engines", Row("one", "two"),
            "team", Row(2f)))));
        CampaignMissionZeppelins binding = CampaignMissionZeppelins.Bind(document);
        CampaignZeppelinRow row = One(Assert.Single(binding.Entries), "Placement and motion");
        Assert.True(binding.Apply(row, Values(binding, row, new()
        {
            ["Yaw (degrees)"] = 90f,
            ["Pitch (degrees)"] = 60f,
            ["Max speed (m/s)"] = 20f,
        })));

        ZeppelinDef definition = Assert.Single(Load(document));
        var net = new AiNet
        {
            Id = 1,
            Name = "route",
            Nodes = new[] { new AiNetNode(Vector3.Zero, Array.Empty<float>()) },
            Edges = Array.Empty<(int A, int B)>(),
        };
        var motion = new ZeppelinMotion(definition, new AiNetFollower(net, new Random(1)));
        Assert.Equal(new Vector3(10f, 20f, 30f), motion.Position);
        Assert.Equal(Mathf.Pi / 2f, motion.YawRad, 5);
        Assert.Equal(Mathf.Pi / 3f, motion.PitchRad, 5);
        Assert.Equal(-0.5f, motion.Forward.X, 5);
        Assert.Equal(Mathf.Sqrt(3f) / 2f, motion.Forward.Y, 5);
        Assert.Equal(0f, motion.Forward.Z, 5);
        Assert.Equal(2, ZeppelinRuntime.AuthoredTeam(definition));
        motion.AliveEngines = 1;
        Assert.Equal(20f * Mathf.Sqrt(0.5f), motion.EffectiveMaxSpeed, 5);
    }

    [Fact]
    public void EmptyMalformedAndBareOverridesAreNotInvented()
    {
        CampaignMissionDocument absent = Document();
        Assert.Empty(CampaignMissionZeppelins.Bind(absent).Entries);
        Assert.Throws<FileNotFoundException>(() => Load(absent));
        CampaignMissionDocument empty = Document(new ZrdrFile("zeppelins.zrd", null, Row()));
        Assert.Contains("empty", CampaignMissionZeppelins.Bind(empty).Problem);
        Assert.Throws<InvalidDataException>(() => Load(empty));
        CampaignMissionDocument noRecords =
            Document(new ZrdrFile("zeppelins.zrd", null, Row((object?)null)));
        Assert.Empty(CampaignMissionZeppelins.Bind(noRecords).Entries);
        Assert.Empty(Load(noRecords));

        var invalid = Row("node", Row("hull"), "position", Row(1f, 2f, 3f, 4f), "net", Row("route"));
        var missingNet = Row("node", Row("hull"), "position", Row(1f, 2f, 3f), "net", Row("earlier"), "NET");
        var optional = Row(
            "node", Row("hull"),
            "position", Row(1f, 2f, 3f),
            "net", Row("route"),
            "yaw", Row(90f),
            "YAW",
            "team", Row("enemy"),
            "TEAM", Row((object?)null),
            "targets", Row("old"),
            "TARGETS",
            "engines", Row("engine", Row("nested")));
        CampaignMissionDocument document = Document(Reader(Row(invalid, missingNet, optional)), Reader(Row(Row("ignored"))));
        CampaignMissionZeppelins binding = CampaignMissionZeppelins.Bind(document);
        Assert.Equal(3, binding.Entries.Count);
        Assert.Empty(binding.Entries[0].Sections);
        Assert.Empty(binding.Entries[1].Sections);
        Assert.DoesNotContain(One(binding.Entries[2], "Placement and motion").Fields, field => field.Label == "Yaw (degrees)");
        Assert.Empty(Section(binding.Entries[2], "Team and behavior").Rows);
        Assert.Empty(Section(binding.Entries[2], "Broadside targets").Rows);
        Assert.Single(Section(binding.Entries[2], "Engines").Rows);
        Assert.Throws<InvalidDataException>(() => Load(document));

        CampaignMissionDocument optionalDocument = Document(Reader(Row(optional)));
        ZeppelinDef actual = Assert.Single(Load(optionalDocument));
        Assert.Equal(0f, actual.YawDeg);
        Assert.Null(actual.Team);
        Assert.Empty(actual.Targets);
        Assert.Equal(new[] { "engine" }, actual.Engines);
    }

    internal static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, new ZrdrSet("invented", files));

    internal static ZrdrFile Reader(List<object?> records) =>
        new("zeppelins.zrd", null, Row(records));

    internal static List<object?> Row(params object?[] values) => new(values);

    internal static List<ZeppelinDef> Load(CampaignMissionDocument document) =>
        Zeppelins.Load(new ZrdrSet("mission", document.Files));

    internal static CampaignZeppelinSection Section(CampaignZeppelinEntry entry, string label) =>
        Assert.Single(entry.Sections.Where(section => section.Label == label));

    internal static CampaignZeppelinRow One(CampaignZeppelinEntry entry, string label) =>
        Assert.Single(Section(entry, label).Rows);

    internal static CampaignZeppelinField Field(CampaignZeppelinRow row, string label) =>
        Assert.Single(row.Fields.Where(field => field.Label == label));

    internal static object[] Values(
        CampaignMissionZeppelins binding,
        CampaignZeppelinRow row,
        Dictionary<string, object>? changes = null) =>
        row.Fields.Select(field => changes != null && changes.TryGetValue(field.Label, out object? value)
            ? value!
            : binding.Value(field)).ToArray();

    private static uint Bits(CampaignMissionDocument document, params int[] path) =>
        BitConverter.SingleToUInt32Bits(Assert.IsType<float>(document.ValueAt(0, path)));
}

/// <summary>Story-scope reader widths and optional fields through an installed game source.</summary>
[Trait("Tier", "Data")]
public sealed class CampaignMissionZeppelinCensusTests
{
    private readonly ITestOutputHelper _output;

    public CampaignMissionZeppelinCensusTests(ITestOutputHelper output) => _output = output;

    [GameFilesFact]
    public void StoryReaderWidthsAndOptionalPresenceMatchTheRuntime()
    {
        var source = GameFilesData.Source!;
        IReadOnlyList<CampaignMission> missions = CampaignMissionDocument.LoadMissions(source);
        var widths = new Dictionary<string, SortedDictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        var present = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bare = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[] keys =
        {
            "node", "position", "net", "yaw", "pitch", "max_speed", "max_accel", "accel_pitch",
            "accel_yaw", "max_rate_yaw", "max_rate_pitch", "min_pitch", "max_pitch", "team",
            "deactivated", "num_healthy_required", "healthy", "engines", "gasbags", "targets",
            "left_cannons", "right_cannons", "cannon_health", "cannon_fire_delay", "cannon_fire_range",
            "cannon_inaccuracy",
        };
        int recordCount = 0;
        foreach (CampaignMission mission in missions)
        {
            CampaignMissionDocument document = CampaignMissionDocument.Load(source, mission);
            CampaignMissionZeppelins binding = CampaignMissionZeppelins.Bind(document);
            if (binding.FileIndex < 0)
            {
                Assert.Throws<FileNotFoundException>(() => CampaignMissionZeppelinsTests.Load(document));
                continue;
            }

            List<ZeppelinDef> runtime = CampaignMissionZeppelinsTests.Load(document);
            Assert.Equal(runtime.Count, binding.Entries.Count);
            var rawRecords = document.Files[binding.FileIndex].Root[0] as List<object?>;
            for (int i = 0; i < binding.Entries.Count; i++)
            {
                CampaignZeppelinEntry entry = binding.Entries[i];
                Assert.Empty(entry.Problem);
                var raw = Assert.IsType<List<object?>>(rawRecords![entry.RawIndex]);
                ZeppelinDef definition = runtime[i];
                CampaignZeppelinRow placement = CampaignMissionZeppelinsTests.One(entry, "Placement and motion");
                Assert.Equal(definition.Node, binding.Value(CampaignMissionZeppelinsTests.Field(placement, "Hull node")));
                Assert.Equal(definition.Net, binding.Value(CampaignMissionZeppelinsTests.Field(placement, "Route net")));
                Assert.Equal(definition.Position.X, binding.Value(CampaignMissionZeppelinsTests.Field(placement, "Position X (m)")));
                Assert.Equal(definition.Position.Y, binding.Value(CampaignMissionZeppelinsTests.Field(placement, "Position Y (m, up)")));
                Assert.Equal(definition.Position.Z, binding.Value(CampaignMissionZeppelinsTests.Field(placement, "Position Z (m)")));
                AssertOptionalScalar(binding, placement, "Yaw (degrees)", definition.YawDeg);
                AssertOptionalScalar(binding, placement, "Pitch (degrees)", definition.PitchDeg);
                AssertOptionalScalar(binding, placement, "Max speed (m/s)", definition.MaxSpeed);
                AssertOptionalScalar(binding, placement, "Max acceleration (m/s²)", definition.MaxAccel);
                AssertOptionalScalar(binding, placement, "Yaw acceleration (degrees/s²)", definition.AccelYawDeg);
                AssertOptionalScalar(binding, placement, "Pitch acceleration (degrees/s²)", definition.AccelPitchDeg);
                AssertOptionalScalar(binding, placement, "Max yaw rate (degrees/s)", definition.MaxRateYawDeg);
                AssertOptionalScalar(binding, placement, "Max pitch rate (degrees/s)", definition.MaxRatePitchDeg);
                Assert.Equal(definition.Targets.Count, CampaignMissionZeppelinsTests.Section(entry, "Broadside targets").Rows.Count);
                Assert.Equal(definition.Healthy.Count, CampaignMissionZeppelinsTests.Section(entry, "Healthy zones").Rows.Count);
                Assert.Equal(definition.Engines.Count, CampaignMissionZeppelinsTests.Section(entry, "Engines").Rows.Count);
                Assert.Equal(definition.Gasbags.Count, CampaignMissionZeppelinsTests.Section(entry, "Gasbags").Rows.Count);
                Assert.Equal(definition.LeftCannons.Count, CampaignMissionZeppelinsTests.Section(entry, "Left cannons").Rows.Count);
                Assert.Equal(definition.RightCannons.Count, CampaignMissionZeppelinsTests.Section(entry, "Right cannons").Rows.Count);
                Assert.Equal(definition.CannonHealth.Count, CampaignMissionZeppelinsTests.Section(entry, "Cannon damage").Rows.Count);
                AddWidth(widths, "record", raw.Count);
                foreach (string key in keys)
                {
                    int selected = CampaignRawList.LastListValueIndex(raw, key);
                    if (selected >= 0 && raw[selected] is List<object?> selectedValues)
                    {
                        present[key] = present.GetValueOrDefault(key) + 1;
                        AddWidth(widths, key + " value", selectedValues.Count);
                    }
                    else if (selected == -2)
                    {
                        bare[key] = bare.GetValueOrDefault(key) + 1;
                    }
                }

                foreach (string key in new[] { "healthy", "gasbags", "left_cannons", "right_cannons", "cannon_health" })
                {
                    int index = CampaignRawList.LastListValueIndex(raw, key);
                    if (index >= 0 && raw[index] is List<object?> rows)
                    {
                        foreach (object? item in rows)
                        {
                            if (item is List<object?> values)
                            {
                                AddWidth(widths, key + " entry", values.Count);
                                int animationSlot = key == "gasbags" ? 2 : key == "cannon_health" ? 5 : -1;
                                if (animationSlot >= 0 && values.Count > animationSlot
                                    && values[animationSlot] is List<object?> animations)
                                {
                                    AddWidth(widths, key + " animation", animations.Count);
                                }

                                if (key == "cannon_health" && values.Count > 6 && values[6] is List<object?> stages)
                                {
                                    AddWidth(widths, "cannon stage list", stages.Count);
                                    foreach (object? stage in stages)
                                    {
                                        if (stage is List<object?> fields)
                                        {
                                            AddWidth(widths, "cannon stage", fields.Count);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                recordCount++;
            }
        }

        Assert.Equal(24, missions.Count);
        Assert.True(recordCount > 0);
        _output.WriteLine($"Story records: {recordCount}");
        foreach (string key in keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"{key}: {present.GetValueOrDefault(key)} selected lists, {bare.GetValueOrDefault(key)} bare");
        }

        foreach ((string key, SortedDictionary<int, int> counts) in widths.OrderBy(pair => pair.Key))
        {
            _output.WriteLine($"{key} widths: {string.Join(", ", counts.Select(pair => $"{pair.Key}={pair.Value}"))}");
        }
    }

    private static void AssertOptionalScalar(
        CampaignMissionZeppelins binding,
        CampaignZeppelinRow row,
        string label,
        float runtimeValue)
    {
        CampaignZeppelinField? field = row.Fields.FirstOrDefault(candidate => candidate.Label == label);
        if (field != null)
        {
            Assert.Equal(runtimeValue, Assert.IsType<float>(binding.Value(field)));
        }
    }

    private static void AddWidth(
        Dictionary<string, SortedDictionary<int, int>> widths,
        string key,
        int width)
    {
        if (!widths.TryGetValue(key, out SortedDictionary<int, int>? sizes))
        {
            sizes = new();
            widths.Add(key, sizes);
        }

        sizes[width] = sizes.GetValueOrDefault(width) + 1;
    }
}

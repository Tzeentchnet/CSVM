using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSVM.Mech3;
using CSVM.Session.Roster;
using CSVM.Tooling;
using Godot;
using Xunit;
using Xunit.Abstractions;

namespace CSVM.Tests;

/// <summary>Generator bindings, atomic edits, and runtime parity over invented readers.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionGeneratorsTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void ExactSelectedLeavesPreserveDuplicatesTailsAndFloatBits()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        var record = Row(
            "node", Row("old_host", "old-tail"),
            "NODE", Row("host", "host-tail"),
            "vehicle", Row("params", Row("old_template"), "nets", Row("old_net")),
            "VEHICLE", Row("params", Row("template", "params-tail"),
                "nets", Row("net_one", 17f, Row("nested_net"), "net_two"),
                "choose_nets", Row("random", "choice-tail")),
            "capacity", Row(0f, "capacity-tail"),
            "max_active", Row(4f),
            "wave_size", Row(2f),
            "wave_period", Row(9f, subnormal),
            "ind_period", Row(3f),
            "zeppelin", Row(0f),
            "origin", Row("bay", "origin-tail"),
            "rotation", Row(-90f, -0f, subnormal),
            "min_altitude", Row(100f),
            "open_anim", Row("open", "open-tail"),
            "close_anim", Row("close", "close-tail"),
            "unknown", Row(-0f, subnormal, null, Row("node", Row("keep"))),
            "moving_path");
        CampaignMissionDocument document = Document(Reader(Row(null, record, 12f)));
        CampaignMissionGenerators binding = CampaignMissionGenerators.Bind(document);
        CampaignGeneratorEntry entry = Assert.Single(binding.Entries);
        Assert.Equal(1, entry.RawIndex);
        Assert.Equal("host", entry.Host);
        Assert.Equal(new[] { 0, 1, 3, 0 }, entry.HostPath);
        Assert.True(entry.IsZeppelin);
        Assert.True(entry.MovingPath);
        Assert.Equal("random", entry.ChooseNets);
        Assert.DoesNotContain(entry.Sections.SelectMany(section => section.Rows)
            .SelectMany(row => row.Fields), field => field.Key is "node" or "choose_nets");

        CampaignGeneratorRow cycle = One(entry, "Timing and capacity");
        Assert.Equal(new[] { 0, 1, 15, 0 }, Field(cycle, "wave_period").Path);
        CampaignGeneratorRow template = One(entry, "Vehicle template");
        Assert.Equal(new[] { 0, 1, 7, 1, 0 }, Field(template, "params").Path);
        CampaignGeneratorSection nets = Section(entry, "AI nets");
        Assert.Equal(2, nets.Rows.Count);
        Assert.Equal(new[] { 0, 1, 7, 3, 0 }, Assert.Single(nets.Rows[0].Fields).Path);
        Assert.Equal(new[] { 0, 1, 7, 3, 3 }, Assert.Single(nets.Rows[1].Fields).Path);
        CampaignGeneratorRow origin = One(entry, "Origin, gate and doors");
        Assert.Equal(new[] { 0, 1, 21, 0 }, Field(origin, "origin").Path);
        Assert.Equal(new[] { 0, 1, 23, 0 }, Field(origin, "rotation").Path);

        int notifications = 0;
        document.Changed += () => notifications++;
        Assert.False(binding.Apply(cycle, Values(binding, cycle)));
        Assert.True(binding.Apply(cycle, Values(binding, cycle, new()
        {
            ["capacity"] = 7f,
            ["max_active"] = 3f,
            ["wave_period"] = 4f,
            ["ind_period"] = 2f,
        })));
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, notifications);
        Assert.Throws<InvalidOperationException>(() => binding.Apply(cycle, Values(binding, cycle)));

        binding = CampaignMissionGenerators.Bind(document);
        entry = Assert.Single(binding.Entries);
        template = One(entry, "Vehicle template");
        Assert.True(binding.Apply(template, Values(binding, template, new() { ["params"] = "new_template" })));
        binding = CampaignMissionGenerators.Bind(document);
        entry = Assert.Single(binding.Entries);
        CampaignGeneratorRow net = Section(entry, "AI nets").Rows[1];
        Assert.True(binding.Apply(net, Values(binding, net, new() { ["net"] = "new_net" })));
        binding = CampaignMissionGenerators.Bind(document);
        entry = Assert.Single(binding.Entries);
        origin = One(entry, "Origin, gate and doors");
        Assert.True(binding.Apply(origin, Values(binding, origin, new()
        {
            ["origin"] = "new_bay",
            ["rotation"] = -45f,
            ["min_altitude"] = 120f,
            ["open_anim"] = "new_open",
        })));

        Assert.Equal("old_host", document.ValueAt(0, new[] { 0, 1, 1, 0 }));
        Assert.Equal("host-tail", document.ValueAt(0, new[] { 0, 1, 3, 1 }));
        Assert.Equal("old_template", document.ValueAt(0, new[] { 0, 1, 5, 1, 0 }));
        Assert.Equal("params-tail", document.ValueAt(0, new[] { 0, 1, 7, 1, 1 }));
        Assert.Equal("nested_net", document.ValueAt(0, new[] { 0, 1, 7, 3, 2, 0 }));
        Assert.Equal("choice-tail", document.ValueAt(0, new[] { 0, 1, 7, 5, 1 }));
        Assert.Equal(1u, Bits(document, 0, 1, 15, 1));
        Assert.Equal("origin-tail", document.ValueAt(0, new[] { 0, 1, 21, 1 }));
        Assert.Equal(0x80000000u, Bits(document, 0, 1, 23, 1));
        Assert.Equal(1u, Bits(document, 0, 1, 23, 2));
        Assert.Equal(0x80000000u, Bits(document, 0, 1, 31, 0));
        Assert.Null(document.ValueAt(0, new[] { 0, 1, 31, 2 }));
        Assert.Equal("keep", document.ValueAt(0, new[] { 0, 1, 31, 3, 1, 0 }));

        EnemyGeneratorDef actual = Assert.Single(Load(document));
        Assert.Equal("host", actual.Node);
        Assert.Equal("new_template", actual.VehicleParams);
        Assert.Equal(new[] { "net_one", "new_net" }, actual.Nets);
        Assert.True(actual.ChooseNetsRandom);
        Assert.Equal(7, actual.Capacity);
        Assert.Equal(3, actual.MaxActive);
        Assert.Equal(2, actual.WaveSize);
        Assert.Equal(4f, actual.WavePeriod);
        Assert.Equal(2f, actual.IndPeriod);
        Assert.True(actual.IsZeppelin);
        Assert.True(actual.MovingPath);
        Assert.Equal("new_bay", actual.Origin);
        Assert.Equal(new Vector3(-45f, -0f, subnormal), actual.RotationDeg);
        Assert.Equal(120f, actual.MinAltitude);
        Assert.Equal("new_open", actual.OpenAnim);
        Assert.Equal("close", actual.CloseAnim);
    }

    [Fact]
    public void TimingEditsDriveTheCycleAndCapacityStillStartsEmpty()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("host"),
            "vehicle", Row("nets", Row("route")),
            "capacity", Row(9f),
            "max_active", Row(4f),
            "wave_size", Row(1f),
            "wave_period", Row(8f),
            "ind_period", Row(1f),
            "min_altitude", Row(100f)))));
        CampaignMissionGenerators binding = CampaignMissionGenerators.Bind(document);
        CampaignGeneratorRow row = One(Assert.Single(binding.Entries), "Timing and capacity");
        Assert.True(binding.Apply(row, Values(binding, row, new()
        {
            ["max_active"] = 3f,
            ["wave_size"] = 2f,
            ["wave_period"] = 4f,
            ["ind_period"] = 2f,
        })));

        var cycle = new GeneratorCycle(Assert.Single(Load(document)));
        Assert.Equal(0, cycle.CapacityRemaining);
        Assert.Equal(6f, cycle.NextEvent);
        cycle.GrantCapacity(2);
        Assert.False(cycle.Step(1f, 99f));
        Assert.False(cycle.DoorOpen);
        Assert.False(cycle.Step(1f, 100f));
        Assert.True(cycle.DoorOpen);
        Assert.True(cycle.Step(4f, 100f));
        Assert.Equal(2f, cycle.NextEvent);
        Assert.True(cycle.Step(2f, 100f));
        Assert.Equal(6f, cycle.NextEvent);
        Assert.Equal(2, cycle.Active);
        Assert.Equal(0, cycle.CapacityRemaining);
    }

    [Fact]
    public void InvalidRowsAndBatchPathsNeverMutateOrNotify()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("host"),
            "vehicle", Row("nets", Row("route")),
            "capacity", Row(0f),
            "max_active", Row(4f),
            "wave_size", Row(2f),
            "wave_period", Row(4f),
            "ind_period", Row(2f),
            "zeppelin",
            "origin", Row("bay"),
            "min_altitude", Row(100f)))));
        CampaignMissionGenerators binding = CampaignMissionGenerators.Bind(document);
        CampaignGeneratorEntry entry = Assert.Single(binding.Entries);
        CampaignGeneratorRow cycle = One(entry, "Timing and capacity");
        CampaignGeneratorRow origin = One(entry, "Origin, gate and doors");
        int notifications = 0;
        document.Changed += () => notifications++;

        Assert.Throws<ArgumentException>(() => binding.Apply(cycle, Values(binding, cycle, new()
        {
            ["capacity"] = 8f,
            ["wave_size"] = 5f,
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(cycle, Values(binding, cycle, new()
        {
            ["max_active"] = 3.5f,
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(cycle, Values(binding, cycle, new()
        {
            ["wave_period"] = -1f,
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(cycle, Values(binding, cycle, new()
        {
            ["wave_period"] = float.MaxValue,
            ["ind_period"] = float.MaxValue,
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(cycle, Values(binding, cycle, new()
        {
            ["ind_period"] = float.NaN,
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(origin, Values(binding, origin, new()
        {
            ["origin"] = "new_bay",
            ["min_altitude"] = -2f,
        })));
        Assert.Throws<ArgumentException>(() => binding.Apply(origin, Values(binding, origin, new()
        {
            ["origin"] = " bad ",
        })));
        Assert.Throws<InvalidOperationException>(() => document.SetGeneratorLeaves(0, 0, new[]
        {
            ((IReadOnlyList<int>)new[] { 0, 0, 5, 0 }, (object)2f),
            ((IReadOnlyList<int>)new[] { 0, 0, 1, 0 }, (object)2f),
        }));
        Assert.Throws<InvalidOperationException>(() => document.SetGeneratorLeaves(0, 0, new[]
        {
            ((IReadOnlyList<int>)new[] { 0, 0, 5, 0 }, (object)2f),
            ((IReadOnlyList<int>)new[] { 0, 0, 5, 0 }, (object)3f),
        }));

        Assert.Equal(0f, document.ValueAt(0, new[] { 0, 0, 5, 0 }));
        Assert.Equal("bay", document.ValueAt(0, new[] { 0, 0, 16, 0 }));
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, notifications);
        Assert.False(document.IsDirty);

        Assert.True(binding.Apply(origin, Values(binding, origin, new() { ["min_altitude"] = -1f })));
        Assert.Null(Assert.Single(Load(document)).MinAltitude);
    }

    [Fact]
    public void MissingMalformedAndVariableRecordsFollowTheReader()
    {
        CampaignMissionDocument absent = Document();
        Assert.Empty(CampaignMissionGenerators.Bind(absent).Entries);
        Assert.Throws<FileNotFoundException>(() => Load(absent));
        CampaignMissionDocument emptyRoot = Document(new ZrdrFile("egen.zrd", null, Row()));
        Assert.Empty(CampaignMissionGenerators.Bind(emptyRoot).Entries);
        Assert.Throws<InvalidDataException>(() => Load(emptyRoot));
        foreach (List<object?> root in new[] { Row((object?)null), Row(12f) })
        {
            CampaignMissionDocument empty = Document(new ZrdrFile("egen.zrd", null, root));
            Assert.Empty(CampaignMissionGenerators.Bind(empty).Entries);
            Assert.Empty(Load(empty));
        }

        var bareVehicle = Row(
            "node", Row("surface"),
            "vehicle", Row("params", Row("old")),
            "VEHICLE",
            "wave_period", Row(4f),
            "WAVE_PERIOD",
            "origin", Row("ignored"),
            "rotation", Row(-90f, 0f, 0f, 42f));
        CampaignMissionDocument bare = Document(Reader(Row(null, bareVehicle, 7f)));
        CampaignGeneratorEntry entry = Assert.Single(CampaignMissionGenerators.Bind(bare).Entries);
        Assert.Empty(entry.Problem);
        Assert.Empty(Section(entry, "Vehicle template").Rows);
        Assert.Empty(Section(entry, "AI nets").Rows);
        Assert.Empty(Section(entry, "Timing and capacity").Rows);
        Assert.Empty(Section(entry, "Origin, gate and doors").Rows);
        EnemyGeneratorDef actual = Assert.Single(Load(bare));
        Assert.Null(actual.VehicleParams);
        Assert.Empty(actual.Nets);
        Assert.Equal(0f, actual.WavePeriod);
        Assert.Null(actual.RotationDeg);

        foreach (List<object?> invalid in new[]
        {
            Row("node", Row("host"), "vehicle", Row(), "NODE"),
            Row("node", Row(1f), "vehicle", Row()),
            Row("node", Row("host")),
        })
        {
            CampaignMissionDocument broken = Document(Reader(Row(invalid)));
            Assert.NotEmpty(Assert.Single(CampaignMissionGenerators.Bind(broken).Entries).Problem);
            Assert.Throws<InvalidDataException>(() => Load(broken));
        }
    }

    [Fact]
    public void OnlyTheFirstReaderAndFirstOuterRecordListAreBound()
    {
        CampaignMissionDocument document = Document(
            new ZrdrFile("egen.zrd", null, Row(
                Row(Row("node", Row("first"), "vehicle", Row())),
                Row(Row("node", Row("outer_tail"), "vehicle", Row())))),
            Reader(Row(Row("node", Row("second_reader"), "vehicle", Row()))));
        CampaignMissionGenerators binding = CampaignMissionGenerators.Bind(document);
        Assert.Equal(0, binding.FileIndex);
        Assert.Equal("first", Assert.Single(binding.Entries).Host);
        Assert.Equal("first", Assert.Single(Load(document)).Node);
    }

    [Fact]
    public void CountAndPeriodBoundariesAreCheckedBeforeAnyLeafChanges()
    {
        CampaignMissionDocument document = Document(Reader(Row(Row(
            "node", Row("host"),
            "vehicle", Row(),
            "capacity", Row(0f),
            "max_active", Row(4f),
            "wave_size", Row(2f),
            "wave_period", Row(4f),
            "ind_period", Row(2f)))));
        CampaignMissionGenerators binding = CampaignMissionGenerators.Bind(document);
        CampaignGeneratorRow row = One(Assert.Single(binding.Entries), "Timing and capacity");
        foreach (Dictionary<string, object> changes in new[]
        {
            new Dictionary<string, object> { ["capacity"] = -1f },
            new Dictionary<string, object> { ["capacity"] = 1.5f },
            new Dictionary<string, object> { ["capacity"] = 2147483648f },
            new Dictionary<string, object> { ["max_active"] = 0f },
            new Dictionary<string, object> { ["wave_size"] = 0f },
            new Dictionary<string, object> { ["wave_period"] = float.PositiveInfinity },
        })
        {
            Assert.Throws<ArgumentException>(() => binding.Apply(row, Values(binding, row, changes)));
            Assert.Equal(0, document.Revision);
        }

        Assert.True(binding.Apply(row, Values(binding, row, new()
        {
            ["capacity"] = 1f,
            ["max_active"] = 1f,
            ["wave_size"] = 1f,
            ["wave_period"] = 0f,
            ["ind_period"] = 0f,
        })));
        EnemyGeneratorDef actual = Assert.Single(Load(document));
        Assert.Equal(1, actual.Capacity);
        Assert.Equal(1, actual.MaxActive);
        Assert.Equal(1, actual.WaveSize);
        Assert.Equal(0f, actual.WavePeriod);
        Assert.Equal(0f, actual.IndPeriod);
    }

    [Fact]
    public void OriginOffsetIsInTheHostsFrameAndNeverUsesAnOutsideNamesake()
    {
        var world = new GameZ();
        var host = new GameZNode
        {
            Index = 0,
            Name = "host",
            Kind = "Group",
            Local = new Transform3D(Basis.Identity, new Vector3(100f, 200f, 300f)),
        };
        host.Children.Add(1);
        var pivot = new GameZNode
        {
            Index = 1,
            Name = "pivot",
            Kind = "Group",
            Local = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2f), new Vector3(0f, 2f, 0f)),
        };
        pivot.Children.Add(2);
        var bay = new GameZNode
        {
            Index = 2,
            Name = "bay",
            Kind = "Group",
            Local = new Transform3D(Basis.Identity, new Vector3(1f, 0f, 0f)),
        };
        var other = new GameZNode { Index = 3, Name = "other", Kind = "Group" };
        other.Children.Add(4);
        var outside = new GameZNode { Index = 4, Name = "bay", Kind = "Group" };
        world.Nodes.AddRange(new[] { host, pivot, bay, other, outside });

        CampaignGeneratorOrigin result = CampaignGeneratorWorld.OriginOffset(world, "host", "bay");
        Vector3 offset = Assert.IsType<Vector3>(result.Offset);
        Assert.Equal(0f, offset.X, 5);
        Assert.Equal(2f, offset.Y, 5);
        Assert.Equal(-1f, offset.Z, 5);
        Assert.Equal(Vector3.Zero, CampaignGeneratorWorld.OriginOffset(world, "host", null).Offset);
        Assert.Null(CampaignGeneratorWorld.OriginOffset(world, "host", "other").Offset);
        Assert.Null(CampaignGeneratorWorld.OriginOffset(world, "missing", "bay").Offset);
        Assert.Null(CampaignGeneratorWorld.OriginOffset(null, "host", "bay").Offset);
    }

    internal static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, new ZrdrSet("invented", files));

    internal static ZrdrFile Reader(List<object?> records) =>
        new("egen.zrd", null, Row(records));

    internal static List<object?> Row(params object?[] values) => new(values);

    internal static List<EnemyGeneratorDef> Load(CampaignMissionDocument document) =>
        EnemyGenerators.Load(new ZrdrSet("mission", document.Files));

    internal static CampaignGeneratorSection Section(CampaignGeneratorEntry entry, string label) =>
        Assert.Single(entry.Sections.Where(section => section.Label == label));

    internal static CampaignGeneratorRow One(CampaignGeneratorEntry entry, string label) =>
        Assert.Single(Section(entry, label).Rows);

    internal static CampaignGeneratorField Field(CampaignGeneratorRow row, string key) =>
        Assert.Single(row.Fields.Where(field => field.Key == key));

    internal static object[] Values(
        CampaignMissionGenerators binding,
        CampaignGeneratorRow row,
        Dictionary<string, object>? changes = null) =>
        row.Fields.Select(field => changes != null && changes.TryGetValue(field.Key, out object? value)
            ? value!
            : binding.Value(field)).ToArray();

    private static uint Bits(CampaignMissionDocument document, params int[] path) =>
        BitConverter.SingleToUInt32Bits(Assert.IsType<float>(document.ValueAt(0, path)));
}

/// <summary>Story-scope generator widths and optional directives through an installed source.</summary>
[Trait("Tier", "Data")]
public sealed class CampaignMissionGeneratorCensusTests
{
    private readonly ITestOutputHelper _output;

    public CampaignMissionGeneratorCensusTests(ITestOutputHelper output) => _output = output;

    [GameFilesFact]
    public void StoryReaderWidthsAndOptionalPresenceMatchTheRuntime()
    {
        var source = GameFilesData.Source!;
        IReadOnlyList<CampaignMission> missions = CampaignMissionDocument.LoadMissions(source);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var widths = new Dictionary<string, SortedDictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        int records = 0;
        foreach (CampaignMission mission in missions)
        {
            CampaignMissionDocument document = CampaignMissionDocument.Load(source, mission);
            CampaignMissionGenerators binding = CampaignMissionGenerators.Bind(document);
            List<EnemyGeneratorDef> runtime = CampaignMissionGeneratorsTests.Load(document);
            Assert.Equal(runtime.Count, binding.Entries.Count);
            if (binding.FileIndex < 0 || binding.Entries.Count == 0)
            {
                continue;
            }

            var rawRecords = Assert.IsType<List<object?>>(document.Files[binding.FileIndex].Root[0]);
            for (int i = 0; i < binding.Entries.Count; i++)
            {
                CampaignGeneratorEntry entry = binding.Entries[i];
                Assert.Empty(entry.Problem);
                var record = Assert.IsType<List<object?>>(rawRecords[entry.RawIndex]);
                Assert.Equal(runtime[i].Node, entry.Host);
                AddWidth(widths, "record", record.Count);
                CountSelectedDirectives(record, "", counts, widths);

                int vehicle = CampaignRawList.LastListValueIndex(record, "vehicle");
                if (vehicle >= 0 && record[vehicle] is List<object?> fields)
                {
                    CountSelectedDirectives(fields, "vehicle.", counts, widths);
                }

                records++;
            }
        }

        Assert.Equal(24, missions.Count);
        _output.WriteLine($"Story generator records: {records}");
        foreach ((string key, int count) in counts.OrderBy(pair => pair.Key))
        {
            _output.WriteLine($"{key}: {count}");
        }

        foreach ((string key, SortedDictionary<int, int> sizes) in widths.OrderBy(pair => pair.Key))
        {
            _output.WriteLine($"{key} widths: {string.Join(", ", sizes.Select(pair => $"{pair.Key}={pair.Value}"))}");
        }
    }

    private static void CountSelectedDirectives(
        List<object?> values,
        string prefix,
        Dictionary<string, int> counts,
        Dictionary<string, SortedDictionary<int, int>> widths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < values.Count;)
        {
            if (values[i] is not string key)
            {
                i++;
                continue;
            }

            if (seen.Add(key))
            {
                string label = prefix + key.ToLowerInvariant();
                int index = CampaignRawList.LastListValueIndex(values, key);
                if (index >= 0 && values[index] is List<object?> selected)
                {
                    counts[label] = counts.GetValueOrDefault(label) + 1;
                    AddWidth(widths, label, selected.Count);
                }
                else if (index == -2)
                {
                    counts[label + " (bare)"] = counts.GetValueOrDefault(label + " (bare)") + 1;
                }
            }

            i += i + 1 < values.Count && values[i + 1] is List<object?> ? 2 : 1;
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

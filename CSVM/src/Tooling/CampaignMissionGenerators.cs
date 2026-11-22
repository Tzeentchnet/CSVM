using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using CSVM.Mech3;
using CSVM.Mech3.Anim;
using Godot;

namespace CSVM.Tooling;

internal enum CampaignGeneratorFieldKind
{
    Name,
    Number,
    NonNegative,
    Count,
    PositiveCount,
    Altitude,
}

internal sealed record CampaignGeneratorField(
    string Key,
    string Label,
    ImmutableArray<int> Path,
    CampaignGeneratorFieldKind Kind);

internal sealed record CampaignGeneratorRow(
    string Label,
    IReadOnlyList<CampaignGeneratorField> Fields,
    string Note = "");

internal sealed record CampaignGeneratorSection(
    string Label,
    IReadOnlyList<CampaignGeneratorRow> Rows);

internal sealed record CampaignGeneratorEntry(
    int RawIndex,
    string Label,
    string Host,
    ImmutableArray<int> HostPath,
    string? Origin,
    bool IsZeppelin,
    bool MovingPath,
    string? ChooseNets,
    string Problem,
    IReadOnlyList<CampaignGeneratorSection> Sections);

/// <summary>Exact-index views of existing runtime-consumed enemy-generator fields.</summary>
internal sealed class CampaignMissionGenerators
{
    private readonly CampaignMissionDocument _document;
    private readonly long _revision;

    private CampaignMissionGenerators(
        CampaignMissionDocument document,
        int fileIndex,
        string problem,
        IReadOnlyList<CampaignGeneratorEntry> entries)
    {
        _document = document;
        _revision = document.ChangeGeneration;
        FileIndex = fileIndex;
        Problem = problem;
        Entries = entries;
    }

    internal int FileIndex { get; }

    internal string Problem { get; }

    internal IReadOnlyList<CampaignGeneratorEntry> Entries { get; }

    internal bool IsCurrent => _document.ChangeGeneration == _revision;

    internal static CampaignMissionGenerators Bind(CampaignMissionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int fileIndex = document.FindFileIndex("egen.json");
        if (fileIndex < 0)
        {
            return new(document, -1, "The mission has no enemy-generator reader.", Array.Empty<CampaignGeneratorEntry>());
        }

        List<object?> root = document.Files[fileIndex].Root;
        if (root.Count == 0)
        {
            return new(document, fileIndex, "The empty enemy-generator reader is rejected by the runtime loader.",
                Array.Empty<CampaignGeneratorEntry>());
        }

        if (root[0] is not List<object?> records)
        {
            return new(document, fileIndex, "This reader contains no generators.", Array.Empty<CampaignGeneratorEntry>());
        }

        var entries = new List<CampaignGeneratorEntry>();
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i] is List<object?> record)
            {
                entries.Add(BindEntry(record, i));
            }
        }

        return new(document, fileIndex,
            entries.Count == 0 ? "This reader contains no generators." : "",
            entries.AsReadOnly());
    }

    internal static bool HasRequiredFields(List<object?> record) =>
        SelectedStringIndex(record, "node") >= 0
        && CampaignRawList.LastListValueIndex(record, "vehicle") != -1;

    internal static int SelectedStringIndex(List<object?> values, string key)
    {
        int index = CampaignRawList.LastListValueIndex(values, key);
        return index >= 0 && values[index] is List<object?> { Count: > 0 } selected
            && selected[0] is string ? index : -1;
    }

    internal static string? SelectedString(List<object?> values, string key)
    {
        int index = SelectedStringIndex(values, key);
        return index < 0 ? null : (string)((List<object?>)values[index]!)[0]!;
    }

    internal object Value(CampaignGeneratorField field)
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("The generator binding is stale; select the record again.");
        }

        return _document.ValueAt(FileIndex, field.Path)
            ?? throw new InvalidOperationException("The selected generator leaf is no longer available.");
    }

    internal bool Apply(CampaignGeneratorRow row, IReadOnlyList<object> values)
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("The generator binding is stale; select the record again.");
        }

        bool belongs = false;
        foreach (CampaignGeneratorEntry entry in Entries)
        {
            foreach (CampaignGeneratorSection section in entry.Sections)
            {
                foreach (CampaignGeneratorRow candidate in section.Rows)
                {
                    belongs |= ReferenceEquals(row, candidate);
                }
            }
        }

        if (!belongs || values.Count != row.Fields.Count)
        {
            throw new ArgumentException("The edited fields do not match this generator row.", nameof(values));
        }

        var edits = new List<(IReadOnlyList<int> Path, object Value)>();
        var proposed = new Dictionary<string, float>(StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < row.Fields.Count; i++)
        {
            CampaignGeneratorField field = row.Fields[i];
            object previous = Value(field);
            object value = values[i];
            Validate(field, previous, value);
            if (value is float number)
            {
                proposed[field.Key] = number;
                if (previous is float oldNumber
                    && BitConverter.SingleToUInt32Bits(oldNumber) != BitConverter.SingleToUInt32Bits(number))
                {
                    changed.Add(field.Key);
                }
            }

            edits.Add((field.Path, value));
        }

        if ((changed.Contains("max_active") || changed.Contains("wave_size"))
            && proposed.TryGetValue("max_active", out float maximum)
            && proposed.TryGetValue("wave_size", out float waveSize)
            && waveSize > maximum)
        {
            throw new ArgumentException("Wave size cannot exceed max active.");
        }

        if ((changed.Contains("wave_period") || changed.Contains("ind_period"))
            && proposed.TryGetValue("wave_period", out float wavePeriod)
            && proposed.TryGetValue("ind_period", out float individualPeriod)
            && !float.IsFinite(wavePeriod + individualPeriod))
        {
            throw new ArgumentException("The combined wave and individual period must be finite.");
        }

        return _document.SetGeneratorLeaves(FileIndex, _revision, edits);
    }

    private static void Validate(CampaignGeneratorField field, object previous, object value)
    {
        if (previous is string oldText && value is string text)
        {
            if (string.Equals(oldText, text, StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text)
                || !string.Equals(text, text.Trim(), StringComparison.Ordinal)
                || HasControl(text))
            {
                throw new ArgumentException($"{field.Label} must be a nonempty name without control characters or surrounding whitespace.");
            }

            return;
        }

        if (previous is not float oldNumber || value is not float number || !float.IsFinite(number))
        {
            throw new ArgumentException($"{field.Label} must preserve its type and contain a finite value.");
        }

        if (BitConverter.SingleToUInt32Bits(oldNumber) == BitConverter.SingleToUInt32Bits(number))
        {
            return;
        }

        bool integral = number == MathF.Truncate(number) && number < 2147483648f;
        bool valid = field.Kind switch
        {
            CampaignGeneratorFieldKind.Number => true,
            CampaignGeneratorFieldKind.NonNegative => number >= 0f,
            CampaignGeneratorFieldKind.Count => number >= 0f && integral,
            CampaignGeneratorFieldKind.PositiveCount => number >= 1f && integral,
            CampaignGeneratorFieldKind.Altitude => number == -1f || number >= 0f,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException($"{field.Label} is outside the supported authored range.");
        }
    }

    private static CampaignGeneratorEntry BindEntry(List<object?> record, int rawIndex)
    {
        int node = SelectedStringIndex(record, "node");
        string host = node < 0 ? "" : (string)((List<object?>)record[node]!)[0]!;
        ImmutableArray<int> hostPath = node < 0
            ? ImmutableArray<int>.Empty
            : ImmutableArray.Create(0, rawIndex, node, 0);
        string label = string.Create(CultureInfo.InvariantCulture, $"#{rawIndex}: {(node < 0 ? "malformed" : host)}");
        bool zeppelin = CampaignRawList.LastListValueIndex(record, "zeppelin") != -1;
        bool moving = CampaignRawList.LastListValueIndex(record, "moving_path") != -1;
        string? origin = SelectedString(record, "origin");
        int vehicleIndex = CampaignRawList.LastListValueIndex(record, "vehicle");
        List<object?>? vehicle = vehicleIndex >= 0 ? (List<object?>)record[vehicleIndex]! : null;
        string? chooseNets = vehicle == null ? null : SelectedString(vehicle, "choose_nets");
        if (!HasRequiredFields(record))
        {
            return new(rawIndex, label, host, hostPath, origin, zeppelin, moving, chooseNets,
                "The runtime requires a node string and a vehicle key. Repair the selected raw record.",
                Array.Empty<CampaignGeneratorSection>());
        }

        var sections = new List<CampaignGeneratorSection>();
        var cycle = new List<CampaignGeneratorField>();
        AddScalar(cycle, record, rawIndex, "capacity", "Capacity (parsed, unused)", CampaignGeneratorFieldKind.Count);
        AddScalar(cycle, record, rawIndex, "max_active", "Max active", CampaignGeneratorFieldKind.PositiveCount);
        AddScalar(cycle, record, rawIndex, "wave_size", "Wave size", CampaignGeneratorFieldKind.PositiveCount);
        AddScalar(cycle, record, rawIndex, "wave_period", "Wave period (s)", CampaignGeneratorFieldKind.NonNegative);
        AddScalar(cycle, record, rawIndex, "ind_period", "Individual period (s)", CampaignGeneratorFieldKind.NonNegative);
        sections.Add(Single("Timing and capacity", cycle,
            "The inter-wave gap is individual plus wave period. Every cycle starts with zero credit regardless of capacity."));

        var placement = new List<CampaignGeneratorField>();
        if (zeppelin)
        {
            AddString(placement, record, rawIndex, "origin", "Origin node under host");
            int rotation = CampaignRawList.LastListValueIndex(record, "rotation");
            if (rotation >= 0 && record[rotation] is List<object?> { Count: 3 } angles
                && Finite(angles[0]) && Finite(angles[1]) && Finite(angles[2]))
            {
                placement.Add(Field("rotation", "Drop pitch X (degrees)", rawIndex,
                    CampaignGeneratorFieldKind.Number, rotation, 0));
            }
        }

        AddScalar(placement, record, rawIndex, "min_altitude", "Minimum altitude (m, -1 disables)", CampaignGeneratorFieldKind.Altitude);
        AddString(placement, record, rawIndex, "open_anim", "Open animation");
        AddString(placement, record, rawIndex, "close_anim", "Close animation");
        AddString(placement, record, rawIndex, "healthy", "Healthy node");
        sections.Add(Single("Origin, gate and doors", placement,
            "The origin is a node under the existing host, not a stored position. Only X of the drop rotation drives CSVM."));

        var template = new List<CampaignGeneratorField>();
        if (vehicle != null)
        {
            int parameters = SelectedStringIndex(vehicle, "params");
            if (parameters >= 0)
            {
                template.Add(Field("params", "Template label", rawIndex,
                    CampaignGeneratorFieldKind.Name, vehicleIndex, parameters, 0));
            }
        }

        sections.Add(Single("Vehicle template", template,
            "The label names a disabled roster template in this mission; an absent label takes the runtime fallback."));

        var nets = new List<CampaignGeneratorRow>();
        if (vehicle != null)
        {
            int netIndex = CampaignRawList.LastListValueIndex(vehicle, "nets");
            if (netIndex >= 0 && vehicle[netIndex] is List<object?> names)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (names[i] is string name)
                    {
                        nets.Add(new CampaignGeneratorRow(
                            string.Create(CultureInfo.InvariantCulture, $"#{i}: {name}"),
                            new[] { Field("net", "Chapter AI net", rawIndex,
                                CampaignGeneratorFieldKind.Name, vehicleIndex, netIndex, i) },
                            "Names are tried in authored order. A generator is dropped when none resolve."));
                    }
                }
            }
        }

        sections.Add(new CampaignGeneratorSection("AI nets", nets.AsReadOnly()));
        return new(rawIndex, label, host, hostPath, origin, zeppelin, moving, chooseNets, "", sections.AsReadOnly());
    }

    private static CampaignGeneratorSection Single(
        string label, List<CampaignGeneratorField> fields, string note) =>
        new(label, fields.Count == 0
            ? Array.Empty<CampaignGeneratorRow>()
            : new[] { new CampaignGeneratorRow(label, fields.AsReadOnly(), note) });

    private static void AddScalar(
        List<CampaignGeneratorField> fields,
        List<object?> record,
        int rawIndex,
        string key,
        string label,
        CampaignGeneratorFieldKind kind)
    {
        int index = CampaignRawList.LastListValueIndex(record, key);
        if (index >= 0 && record[index] is List<object?> { Count: > 0 } values && Finite(values[0]))
        {
            fields.Add(Field(key, label, rawIndex, kind, index, 0));
        }
    }

    private static void AddString(
        List<CampaignGeneratorField> fields,
        List<object?> record,
        int rawIndex,
        string key,
        string label)
    {
        int index = SelectedStringIndex(record, key);
        if (index >= 0)
        {
            fields.Add(Field(key, label, rawIndex, CampaignGeneratorFieldKind.Name, index, 0));
        }
    }

    private static CampaignGeneratorField Field(
        string key, string label, int rawIndex, CampaignGeneratorFieldKind kind, params int[] suffix)
    {
        var path = ImmutableArray.CreateBuilder<int>(suffix.Length + 2);
        path.Add(0);
        path.Add(rawIndex);
        path.AddRange(suffix);
        return new(key, label, path.MoveToImmutable(), kind);
    }

    private static bool Finite(object? value) => value is float number && float.IsFinite(number);

    private static bool HasControl(string text)
    {
        foreach (char character in text)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record CampaignGeneratorOrigin(Vector3? Offset, string Detail);

internal static class CampaignGeneratorWorld
{
    internal static CampaignGeneratorOrigin OriginOffset(GameZ? world, string hostName, string? originName)
    {
        if (world == null)
        {
            return new(null, "The chapter world is unavailable.");
        }

        var names = new NameResolver<GameZNode>();
        foreach (GameZNode node in world.Nodes)
        {
            names.Add(node, node.Name, world.ParentOf(node));
        }

        List<GameZNode> hosts = names.FindAll(hostName, null);
        if (hosts.Count != 1)
        {
            return new(null, hosts.Count == 0
                ? "The host is unresolved in the chapter world."
                : "Multiple source nodes match the host; its live binding is unverified.");
        }

        GameZNode host = hosts[0];
        GameZNode root = host;
        while (world.ParentOf(root) is { } parent)
        {
            root = parent;
        }

        if (world.IsLibraryRoot(root) || world.IsStagedProp(root))
        {
            return new(null, "The host requires runtime staging.");
        }

        if (originName == null)
        {
            return new(Vector3.Zero, "No origin is authored; the runtime uses the host.");
        }

        List<GameZNode> origins = names.FindAll(originName, host);
        if (origins.Count != 1)
        {
            return new(null, origins.Count == 0
                ? "The origin is unresolved under this host; the runtime falls back to the host."
                : "Multiple origin nodes match under this host; the live binding is unverified.");
        }

        Transform3D offset = Transform3D.Identity;
        for (GameZNode? node = origins[0]; node != null && !ReferenceEquals(node, host); node = world.ParentOf(node))
        {
            offset = (node.Local ?? Transform3D.Identity) * offset;
        }

        return new(offset.Origin, "Source chapter pose before mission setup or live animation.");
    }
}

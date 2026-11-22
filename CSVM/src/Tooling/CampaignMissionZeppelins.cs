using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace CSVM.Tooling;

internal enum CampaignZeppelinFieldKind
{
    Name,
    TeamName,
    Number,
    NonNegative,
    Fraction,
    Count,
    TeamId,
    Flag,
}

internal sealed record CampaignZeppelinField(
    string Label,
    ImmutableArray<int> Path,
    CampaignZeppelinFieldKind Kind,
    int Maximum = int.MaxValue);

internal sealed record CampaignZeppelinRow(
    string Label,
    IReadOnlyList<CampaignZeppelinField> Fields,
    string Note = "");

internal sealed record CampaignZeppelinSection(
    string Label,
    IReadOnlyList<CampaignZeppelinRow> Rows);

internal sealed record CampaignZeppelinEntry(
    int RawIndex,
    string Label,
    string Problem,
    IReadOnlyList<CampaignZeppelinSection> Sections);

/// <summary>Editable runtime-consumed leaves in the first mission zeppelin reader.</summary>
internal sealed class CampaignMissionZeppelins
{
    private readonly CampaignMissionDocument _document;
    private readonly long _revision;

    private CampaignMissionZeppelins(
        CampaignMissionDocument document,
        int fileIndex,
        string problem,
        IReadOnlyList<CampaignZeppelinEntry> entries)
    {
        _document = document;
        _revision = document.ChangeGeneration;
        FileIndex = fileIndex;
        Problem = problem;
        Entries = entries;
    }

    internal int FileIndex { get; }

    internal string Problem { get; }

    internal IReadOnlyList<CampaignZeppelinEntry> Entries { get; }

    internal bool IsCurrent => _document.ChangeGeneration == _revision;

    internal static CampaignMissionZeppelins Bind(CampaignMissionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int index = document.FindFileIndex("zeppelins.json");
        if (index < 0)
        {
            return new(document, -1, "The mission has no zeppelins reader; there are no editable records.", Array.Empty<CampaignZeppelinEntry>());
        }

        List<object?> root = document.Files[index].Root;
        if (root.Count == 0)
        {
            return new(document, index, "The zeppelins reader is empty; the runtime rejects it.", Array.Empty<CampaignZeppelinEntry>());
        }

        if (root[0] is not List<object?> records)
        {
            return new(document, index, "The first reader element contains no zeppelin records.", Array.Empty<CampaignZeppelinEntry>());
        }

        var entries = new List<CampaignZeppelinEntry>();
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i] is List<object?> record)
            {
                entries.Add(BindEntry(record, i));
            }
        }

        string problem = entries.Count == 0
            ? "The first reader element contains no zeppelin records."
            : "";
        return new(document, index, problem, entries.AsReadOnly());
    }

    internal static bool HasHealthyNode(List<object?> record, string name)
    {
        int index = CampaignRawList.LastListValueIndex(record, "healthy");
        if (index >= 0 && record[index] is List<object?> values)
        {
            foreach (object? value in values)
            {
                if (value is List<object?> { Count: >= 2 } pair
                    && pair[0] is string node && pair[1] is string
                    && node.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool IsLastGasbag(List<object?> values, int index, string name)
    {
        for (int i = index + 1; i < values.Count; i++)
        {
            if (values[i] is List<object?> { Count: >= 2 } bag
                && bag[0] is string other && bag[1] is float
                && other.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool HasUsableRequiredFields(List<object?> record)
    {
        int position = CampaignRawList.LastListValueIndex(record, "position");
        return SelectedString(record, "node") >= 0
            && SelectedString(record, "net") >= 0
            && position >= 0
            && record[position] is List<object?> { Count: 3 } xyz
            && Finite(xyz[0]) && Finite(xyz[1]) && Finite(xyz[2]);
    }

    internal object Value(CampaignZeppelinField field)
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("The zeppelin binding is stale; select the record again.");
        }

        return _document.ValueAt(FileIndex, field.Path)
            ?? throw new InvalidOperationException("The selected zeppelin leaf is no longer available.");
    }

    internal bool Apply(CampaignZeppelinRow row, IReadOnlyList<object> values)
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("The zeppelin binding is stale; select the record again.");
        }

        bool belongs = false;
        foreach (CampaignZeppelinEntry entry in Entries)
        {
            foreach (CampaignZeppelinSection section in entry.Sections)
            {
                foreach (CampaignZeppelinRow candidate in section.Rows)
                {
                    belongs |= ReferenceEquals(row, candidate);
                }
            }
        }

        if (!belongs || values.Count != row.Fields.Count)
        {
            throw new ArgumentException("The edited fields do not match this zeppelin row.", nameof(values));
        }

        var edits = new List<(IReadOnlyList<int> Path, object Value)>();
        for (int i = 0; i < row.Fields.Count; i++)
        {
            CampaignZeppelinField field = row.Fields[i];
            object value = values[i];
            object previous = Value(field);
            Validate(field, previous, value);
            edits.Add((field.Path, value));
        }

        return _document.SetZeppelinLeaves(FileIndex, _revision, edits);
    }

    private static void Validate(CampaignZeppelinField field, object previous, object value)
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

            if (field.Kind == CampaignZeppelinFieldKind.TeamName
                && !text.Equals("ally", StringComparison.OrdinalIgnoreCase)
                && !text.Equals("enemy", StringComparison.OrdinalIgnoreCase)
                && !text.Equals("neutral", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Team must be ally, enemy, or neutral.");
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

        bool valid = field.Kind switch
        {
            CampaignZeppelinFieldKind.Number => true,
            CampaignZeppelinFieldKind.NonNegative => number >= 0f,
            CampaignZeppelinFieldKind.Fraction => number is >= 0f and <= 1f,
            CampaignZeppelinFieldKind.Count => number >= 0f
                && number < 2147483648f
                && number <= field.Maximum
                && number == MathF.Truncate(number),
            CampaignZeppelinFieldKind.TeamId => number >= int.MinValue
                && number < 2147483648f
                && number == MathF.Truncate(number),
            CampaignZeppelinFieldKind.Flag => number is 0f or 1f,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException($"{field.Label} is outside the supported authored range.");
        }
    }

    private static CampaignZeppelinEntry BindEntry(List<object?> record, int rawIndex)
    {
        int node = SelectedString(record, "node");
        int net = SelectedString(record, "net");
        int position = CampaignRawList.LastListValueIndex(record, "position");
        string label = Numbered(rawIndex, node >= 0
            ? (string)((List<object?>)record[node]!)[0]!
            : "malformed");
        if (!HasUsableRequiredFields(record))
        {
            return new(rawIndex, label,
                "The runtime requires node and net strings and exactly three position floats; the editor requires finite values. Repair malformed structure in the source reader.",
                Array.Empty<CampaignZeppelinSection>());
        }

        var sections = new List<CampaignZeppelinSection>();
        var placement = new List<CampaignZeppelinField>
        {
            Field("Hull node", rawIndex, CampaignZeppelinFieldKind.Name, node, 0),
            Field("Route net", rawIndex, CampaignZeppelinFieldKind.Name, net, 0),
            Field("Position X (m)", rawIndex, CampaignZeppelinFieldKind.Number, position, 0),
            Field("Position Y (m, up)", rawIndex, CampaignZeppelinFieldKind.Number, position, 1),
            Field("Position Z (m)", rawIndex, CampaignZeppelinFieldKind.Number, position, 2),
        };
        AddScalar(placement, record, rawIndex, "yaw", "Yaw (degrees)", CampaignZeppelinFieldKind.Number);
        AddScalar(placement, record, rawIndex, "pitch", "Pitch (degrees)", CampaignZeppelinFieldKind.Number);
        AddScalar(placement, record, rawIndex, "max_speed", "Max speed (m/s)", CampaignZeppelinFieldKind.NonNegative);
        AddScalar(placement, record, rawIndex, "max_accel", "Max acceleration (m/s²)", CampaignZeppelinFieldKind.NonNegative);
        AddScalar(placement, record, rawIndex, "accel_yaw", "Yaw acceleration (degrees/s²)", CampaignZeppelinFieldKind.NonNegative);
        AddScalar(placement, record, rawIndex, "accel_pitch", "Pitch acceleration (degrees/s²)", CampaignZeppelinFieldKind.NonNegative);
        AddScalar(placement, record, rawIndex, "max_rate_yaw", "Max yaw rate (degrees/s)", CampaignZeppelinFieldKind.NonNegative);
        AddScalar(placement, record, rawIndex, "max_rate_pitch", "Max pitch rate (degrees/s)", CampaignZeppelinFieldKind.NonNegative);
        sections.Add(Single("Placement and motion", placement));

        var behavior = new List<CampaignZeppelinField>();
        int team = CampaignRawList.LastListValueIndex(record, "team");
        if (team >= 0 && record[team] is List<object?> { Count: > 0 } teamValues)
        {
            if (teamValues[0] is string)
            {
                behavior.Add(Field("Team name", rawIndex, CampaignZeppelinFieldKind.TeamName, team, 0));
            }
            else if (Finite(teamValues[0]))
            {
                behavior.Add(Field("Team ID", rawIndex, CampaignZeppelinFieldKind.TeamId, team, 0));
            }
        }

        AddScalar(behavior, record, rawIndex, "deactivated", "Deactivated (0 or 1)", CampaignZeppelinFieldKind.Flag);
        int healthyCount = HealthyCount(record);
        AddScalar(behavior, record, rawIndex, "num_healthy_required", "Survivors required", CampaignZeppelinFieldKind.Count,
            healthyCount > 0 ? healthyCount : int.MaxValue);
        AddScalar(behavior, record, rawIndex, "cannon_fire_delay", "Cannon delay (s)", CampaignZeppelinFieldKind.NonNegative);
        AddScalar(behavior, record, rawIndex, "cannon_fire_range", "Cannon range (m)", CampaignZeppelinFieldKind.NonNegative);
        AddScalar(behavior, record, rawIndex, "cannon_inaccuracy", "Cannon inaccuracy (degrees)", CampaignZeppelinFieldKind.NonNegative);
        sections.Add(Single("Team and behavior", behavior));
        sections.Add(Names(record, rawIndex, "targets", "Broadside targets"));
        sections.Add(Healthy(record, rawIndex));
        sections.Add(Names(record, rawIndex, "engines", "Engines"));
        sections.Add(Gasbags(record, rawIndex));
        sections.Add(Cannons(record, rawIndex, "left_cannons", "Left cannons"));
        sections.Add(Cannons(record, rawIndex, "right_cannons", "Right cannons"));
        sections.Add(CannonHealth(record, rawIndex));
        return new(rawIndex, label, "", sections.AsReadOnly());
    }

    private static CampaignZeppelinSection Single(string label, List<CampaignZeppelinField> fields) =>
        new(label, fields.Count == 0
            ? Array.Empty<CampaignZeppelinRow>()
            : new[] { new CampaignZeppelinRow(label, fields.AsReadOnly()) });

    private static CampaignZeppelinSection Names(
        List<object?> record, int rawIndex, string key, string label)
    {
        var rows = new List<CampaignZeppelinRow>();
        int index = CampaignRawList.LastListValueIndex(record, key);
        if (index >= 0 && record[index] is List<object?> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is string name)
                {
                    rows.Add(new(Numbered(i, name), new[]
                    {
                        Field("Name", rawIndex, CampaignZeppelinFieldKind.Name, index, i),
                    }));
                }
            }
        }

        return new(label, rows.AsReadOnly());
    }

    private static CampaignZeppelinSection Healthy(List<object?> record, int rawIndex)
    {
        var rows = new List<CampaignZeppelinRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = CampaignRawList.LastListValueIndex(record, "healthy");
        if (index >= 0 && record[index] is List<object?> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is List<object?> { Count: >= 2 } pair
                    && pair[0] is string name && pair[1] is string)
                {
                    bool first = seen.Add(name);
                    rows.Add(new(Numbered(i, name), new[]
                    {
                        Field("Zone node", rawIndex, CampaignZeppelinFieldKind.Name, index, i, 0),
                        Field("State child", rawIndex, CampaignZeppelinFieldKind.Name, index, i, 1),
                    }, first
                        ? "Each healthy entry counts toward the survivor threshold."
                        : "This duplicate counts toward the threshold but uses the first entry's state child."));
                }
            }
        }

        return new("Healthy zones", rows.AsReadOnly());
    }

    private static CampaignZeppelinSection Gasbags(List<object?> record, int rawIndex)
    {
        var rows = new List<CampaignZeppelinRow>();
        int index = CampaignRawList.LastListValueIndex(record, "gasbags");
        if (index >= 0 && record[index] is List<object?> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is not List<object?> { Count: >= 2 } bag
                    || bag[0] is not string name || !Finite(bag[1]))
                {
                    continue;
                }

                var fields = new List<CampaignZeppelinField>
                {
                    Field("Gasbag node", rawIndex, CampaignZeppelinFieldKind.Name, index, i, 0),
                    Field("Hit points", rawIndex, CampaignZeppelinFieldKind.NonNegative, index, i, 1),
                };
                AddAnimation(fields, bag, 2, "Destroy animation", rawIndex, index, i);
                bool last = IsLastGasbag(values, i, name);
                bool matched = HasHealthyNode(record, name);
                string note = !last
                    ? "A later gasbag with this name supplies the hit points and animation."
                    : !matched
                        ? "No healthy zone currently consumes this gasbag's hit points or animation."
                        : "This gasbag supplies the matching healthy zone's hit points and animation.";
                string suffix = !last ? " (shadowed)" : !matched ? " (unmatched)" : "";
                rows.Add(new(Numbered(i, name + suffix), fields.AsReadOnly(), note));
            }
        }

        return new("Gasbags", rows.AsReadOnly());
    }

    private static CampaignZeppelinSection Cannons(
        List<object?> record, int rawIndex, string key, string label)
    {
        var rows = new List<CampaignZeppelinRow>();
        int index = CampaignRawList.LastListValueIndex(record, key);
        if (index >= 0 && record[index] is List<object?> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is List<object?> { Count: >= 3 } cannon
                    && cannon[0] is string name
                    && cannon[1] is string && cannon[2] is string)
                {
                    rows.Add(new(Numbered(i, name), new[]
                    {
                        Field("Cannon node", rawIndex, CampaignZeppelinFieldKind.Name, index, i, 0),
                        Field("Deploy animation", rawIndex, CampaignZeppelinFieldKind.Name, index, i, 1),
                        Field("Retract animation", rawIndex, CampaignZeppelinFieldKind.Name, index, i, 2),
                    }));
                }
            }
        }

        return new(label, rows.AsReadOnly());
    }

    private static CampaignZeppelinSection CannonHealth(List<object?> record, int rawIndex)
    {
        var rows = new List<CampaignZeppelinRow>();
        int index = CampaignRawList.LastListValueIndex(record, "cannon_health");
        if (index >= 0 && record[index] is List<object?> values)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] is not List<object?> { Count: >= 7 } cannon
                    || cannon[0] is not string name || cannon[3] is not string
                    || !Finite(cannon[4]))
                {
                    continue;
                }

                var fields = new List<CampaignZeppelinField>
                {
                    Field("Cannon node", rawIndex, CampaignZeppelinFieldKind.Name, index, i, 0),
                    Field("Associated gasbag", rawIndex, CampaignZeppelinFieldKind.Name, index, i, 3),
                    Field("Hit points", rawIndex, CampaignZeppelinFieldKind.NonNegative, index, i, 4),
                };
                AddAnimation(fields, cannon, 5, "Destroy animation", rawIndex, index, i);
                if (cannon[6] is List<object?> stages)
                {
                    for (int stageIndex = 0; stageIndex < stages.Count; stageIndex++)
                    {
                        if (stages[stageIndex] is List<object?> { Count: >= 2 } stage
                            && Finite(stage[0]) && stage[1] is string)
                        {
                            string stageNumber = stageIndex.ToString(CultureInfo.InvariantCulture);
                            fields.Add(Field($"Stage #{stageNumber} fraction", rawIndex,
                                CampaignZeppelinFieldKind.Fraction, index, i, 6, stageIndex, 0));
                            fields.Add(Field($"Stage #{stageNumber} animation", rawIndex,
                                CampaignZeppelinFieldKind.Name, index, i, 6, stageIndex, 1));
                        }
                    }
                }

                rows.Add(new(Numbered(i, name), fields.AsReadOnly()));
            }
        }

        return new("Cannon damage", rows.AsReadOnly());
    }

    private static void AddAnimation(
        List<CampaignZeppelinField> fields,
        List<object?> entry,
        int animationIndex,
        string label,
        int rawIndex,
        int listIndex,
        int entryIndex)
    {
        if (entry.Count > animationIndex
            && entry[animationIndex] is List<object?> { Count: > 0 } anims
            && anims[0] is string)
        {
            fields.Add(Field(label, rawIndex, CampaignZeppelinFieldKind.Name,
                listIndex, entryIndex, animationIndex, 0));
        }
    }

    private static int HealthyCount(List<object?> record)
    {
        int index = CampaignRawList.LastListValueIndex(record, "healthy");
        int count = 0;
        if (index >= 0 && record[index] is List<object?> values)
        {
            foreach (object? value in values)
            {
                if (value is List<object?> { Count: >= 2 } pair
                    && pair[0] is string && pair[1] is string)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int SelectedString(List<object?> record, string key)
    {
        int index = CampaignRawList.LastListValueIndex(record, key);
        return index >= 0 && record[index] is List<object?> { Count: > 0 } values
            && values[0] is string ? index : -1;
    }

    private static void AddScalar(
        List<CampaignZeppelinField> fields,
        List<object?> record,
        int rawIndex,
        string key,
        string label,
        CampaignZeppelinFieldKind kind,
        int maximum = int.MaxValue)
    {
        int index = CampaignRawList.LastListValueIndex(record, key);
        if (index >= 0 && record[index] is List<object?> { Count: > 0 } values
            && Finite(values[0]))
        {
            fields.Add(new(label, ImmutableArray.Create(0, rawIndex, index, 0), kind, maximum));
        }
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

    private static string Numbered(int index, string value) =>
        string.Create(CultureInfo.InvariantCulture, $"#{index}: {value}");

    private static CampaignZeppelinField Field(
        string label, int rawIndex, CampaignZeppelinFieldKind kind, params int[] suffix)
    {
        var path = ImmutableArray.CreateBuilder<int>(suffix.Length + 2);
        path.Add(0);
        path.Add(rawIndex);
        path.AddRange(suffix);
        return new(label, path.MoveToImmutable(), kind);
    }
}

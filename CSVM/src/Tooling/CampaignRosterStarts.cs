using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CSVM.Mech3;

namespace CSVM.Tooling;

internal enum CampaignRosterFieldKind
{
    NetId,
    Durability,
    Rating,
    SentinelFlag,
    SignatureMask,
    Text,
}

/// <summary>The editable values of one <c>aiv.zrd</c> roster start.</summary>
public readonly record struct CampaignRosterStartState(
    float X,
    float Y,
    float Z,
    float YawDeg,
    int Team,
    int Group);

/// <summary>The exact-index typed views over one mission's <c>aiv.zrd</c> roster starts.</summary>
public sealed class CampaignRosterStarts
{
    internal const int MaxExactInteger = 16_777_216;

    private const string ReaderName = "aiv.json";

    private readonly CampaignMissionDocument _document;
    private readonly long _revision;
    private readonly List<CampaignRosterStart> _entries;

    private CampaignRosterStarts(
        CampaignMissionDocument document,
        int fileIndex,
        List<CampaignRosterStart> entries)
    {
        _document = document;
        _revision = document.ChangeGeneration;
        FileIndex = fileIndex;
        _entries = entries;
    }

    /// <summary>Roster entries in raw file order.</summary>
    public IReadOnlyList<CampaignRosterStart> Entries =>
        _entries;

    internal int FileIndex { get; }

    internal bool IsCurrent => _document.ChangeGeneration == _revision;

    /// <summary>Binds every roster entry when the reader has its positional header and every
    /// block carries the complete six-slot spawn prefix. Additional fields bind independently
    /// only when their existing values have supported shapes.</summary>
    public static bool TryBind(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignRosterStarts? binding,
        out string problem)
    {
        binding = null;
        int fileIndex =
            document.FindFileIndex(ReaderName);
        if (fileIndex < 0)
        {
            problem = $"'{ReaderName}' is absent";
            return false;
        }

        List<object?> root = document.Files[fileIndex].Root;
        if (root.Count == 0
            || root[0] is not List<object?> header)
        {
            problem = $"'{ReaderName}' has no positional header";
            return false;
        }

        var entries = new List<CampaignRosterStart>(
            Math.Max(0, root.Count - 1));
        for (int rootIndex = 1; rootIndex < root.Count; rootIndex++)
        {
            if (root[rootIndex] is not List<object?> { Count: >= 2 } entry
                || entry[0] is not string { Length: > 0 } name
                || entry[1] is not List<object?> fields)
            {
                problem =
                    $"'{ReaderName}' entry [{rootIndex}] is not a named roster block";
                return false;
            }

            string? fieldProblem =
                SpawnPrefixProblem(fields);
            if (fieldProblem != null)
            {
                problem =
                    $"'{ReaderName}' entry [{rootIndex}] '{name}' {fieldProblem}";
                return false;
            }

            entries.Add(
                new CampaignRosterStart(
                    document,
                    fileIndex,
                    rootIndex,
                    name,
                    fields,
                    header));
        }

        binding =
            new CampaignRosterStarts(document, fileIndex, entries);
        problem = "";
        return true;
    }

    private static string? SpawnPrefixProblem(
        List<object?> fields)
    {
        if (fields.Count < 6)
        {
            return "does not carry the complete six-slot spawn prefix";
        }

        if (fields[1] is not List<object?> position
            || position.Count < 3
            || position[0] is not float x
            || !float.IsFinite(x)
            || position[1] is not float y
            || !float.IsFinite(y)
            || position[2] is not float z
            || !float.IsFinite(z))
        {
            return "has no finite three-component spawn position";
        }

        if (fields[2] is not float yaw
            || !float.IsFinite(yaw))
        {
            return "has no finite spawn yaw";
        }

        if (fields[3] is not float team
            || !Integral(team)
            || team < -1f)
        {
            return "has a non-integral or unsupported team";
        }

        if (fields[4] is not float group
            || !Integral(group)
            || group < 0f)
        {
            return "has a non-integral or negative group";
        }

        if (fields[5] is not float enabled
            || (enabled != 0f && enabled != 1f))
        {
            return "has an enabled flag other than 0 or 1";
        }

        return null;
    }

    private static bool Integral(float value) =>
        float.IsFinite(value)
        && value == MathF.Truncate(value)
        && value >= -MaxExactInteger
        && value <= MaxExactInteger;
}

/// <summary>One roster block's exact-index spawn and supported AI field views.</summary>
public sealed class CampaignRosterStart
{
    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _rootIndex;
    private long _revision;

    internal CampaignRosterStart(
        CampaignMissionDocument document,
        int fileIndex,
        int rootIndex,
        string name,
        List<object?> fields,
        List<object?> header)
    {
        _document = document;
        _fileIndex = fileIndex;
        _rootIndex = rootIndex;
        _revision = document.ChangeGeneration;
        Name = name;
        TemplateLabel = fields[5] is float enabled && enabled == 0f
            && rootIndex <= (header.Count - 1) / 2
            && header[rootIndex * 2] is string { Length: > 0 } label
                ? label
                : null;
        Groups = IsPlayer
            ? Array.Empty<CampaignRosterFieldGroup>()
            : BindGroups(fields);
    }

    /// <summary>The roster block's node name.</summary>
    public string Name { get; }

    /// <summary>The raw root index of this block, retained for diagnostics and selection.</summary>
    public int RawIndex =>
        _rootIndex;

    /// <summary>Whether this is the human player's roster block. Campaign placement reads
    /// <c>PLAYER_INIT</c> instead.</summary>
    public bool IsPlayer =>
        Name.Equals(
            "player",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this block is present in the initial roster. False marks an enemy-generator
    /// parameter template.</summary>
    public bool Enabled =>
        Scalar(5) == 1f;

    /// <summary>The current typed spawn prefix, read from the raw document on every access.</summary>
    public CampaignRosterStartState State =>
        new(
            Vector(0),
            Vector(1),
            Vector(2),
            Scalar(2),
            (int)Scalar(3),
            (int)Scalar(4));

    internal string? TemplateLabel { get; }

    internal IReadOnlyList<CampaignRosterFieldGroup> Groups { get; }

    /// <summary>Writes every editable spawn field after validating them together. Returns whether
    /// any raw float changed.</summary>
    public bool Apply(CampaignRosterStartState state)
    {
        EnsureEditable();
        Validate(state);
        var edits = new List<(IReadOnlyList<int> Path, object Value)>
        {
            (Path(1, 0), state.X),
            (Path(1, 1), state.Y),
            (Path(1, 2), state.Z),
            (Path(2), state.YawDeg),
            (Path(3), UnchangedInteger(3, state.Team)),
            (Path(4), UnchangedInteger(4, state.Group)),
        };
        return ApplyLeaves(edits);
    }

    internal object Value(CampaignRosterField field)
    {
        EnsureCurrent();
        return _document.ValueAt(_fileIndex, field.Path)
            ?? throw new InvalidOperationException("The selected roster leaf is no longer available.");
    }

    internal bool Apply(CampaignRosterFieldGroup group, IReadOnlyList<object> values)
    {
        EnsureEditable();
        bool belongs = false;
        foreach (CampaignRosterFieldGroup candidate in Groups)
        {
            belongs |= ReferenceEquals(group, candidate);
        }

        if (!belongs || values.Count != group.Fields.Count)
        {
            throw new ArgumentException("The edited fields do not match this roster group.", nameof(values));
        }

        var edits = new List<(IReadOnlyList<int> Path, object Value)>();
        float? marker = null;
        bool changedMarkerLabel = false;
        for (int i = 0; i < group.Fields.Count; i++)
        {
            CampaignRosterField field = group.Fields[i];
            object previous = Value(field);
            object value = values[i];
            Validate(field, previous, value);
            if (field.Slot == 37 && value is float flag)
            {
                marker = flag;
            }
            else if (field.Slot is 38 or 39
                && previous is string oldText && value is string text
                && !string.Equals(oldText, text, StringComparison.Ordinal))
            {
                changedMarkerLabel = true;
            }

            edits.Add((field.Path, value));
        }

        if (changedMarkerLabel && marker is not >= 1f)
        {
            throw new ArgumentException("Marker labels can only be changed when the objective marker is enabled.");
        }

        return ApplyLeaves(edits);
    }

    private static void AddGroup(
        List<CampaignRosterFieldGroup> groups,
        string label,
        List<CampaignRosterField> fields,
        string note)
    {
        if (fields.Count > 0)
        {
            groups.Add(new CampaignRosterFieldGroup(label, fields.AsReadOnly(), note));
        }
    }

    private static bool SupportedNumber(object? value, CampaignRosterFieldKind kind)
    {
        if (value is not float number || !float.IsFinite(number))
        {
            return false;
        }

        bool integer = number == MathF.Truncate(number);
        return kind switch
        {
            CampaignRosterFieldKind.NetId => integer && number >= -1f
                && number <= CampaignRosterStarts.MaxExactInteger,
            CampaignRosterFieldKind.Durability => number == -1f || number >= 0f,
            CampaignRosterFieldKind.Rating => integer && number >= -1f && number <= 9f,
            CampaignRosterFieldKind.SentinelFlag => number is -1f or 0f or 1f,
            CampaignRosterFieldKind.SignatureMask => integer && number >= -1f && number <= 131_071f,
            _ => false,
        };
    }

    private static void Validate(CampaignRosterField field, object previous, object value)
    {
        if (previous is string oldText && value is string text)
        {
            if (string.Equals(oldText, text, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException($"{field.Label} cannot have surrounding whitespace.");
            }

            foreach (char character in text)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException($"{field.Label} cannot contain control characters.");
                }
            }

            return;
        }

        if (previous is not float || !SupportedNumber(value, field.Kind))
        {
            throw new ArgumentException($"{field.Label} must be a supported finite value of the existing type.");
        }
    }

    private static void Validate(
        CampaignRosterStartState state)
    {
        if (!float.IsFinite(state.X)
            || !float.IsFinite(state.Y)
            || !float.IsFinite(state.Z)
            || !float.IsFinite(state.YawDeg))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "roster position and yaw must be finite");
        }

        if (state.Team < -1
            || state.Team > CampaignRosterStarts.MaxExactInteger)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                $"roster team must be between -1 and {CampaignRosterStarts.MaxExactInteger}");
        }

        if (state.Group < 0
            || state.Group > CampaignRosterStarts.MaxExactInteger)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                $"roster group must be between 0 and {CampaignRosterStarts.MaxExactInteger}");
        }
    }

    private bool ApplyLeaves(IReadOnlyList<(IReadOnlyList<int> Path, object Value)> edits)
    {
        bool changed = _document.SetRosterLeaves(_fileIndex, _revision, edits);
        _revision = _document.ChangeGeneration;
        return changed;
    }

    private void EnsureEditable()
    {
        EnsureCurrent();
        if (IsPlayer)
        {
            throw new InvalidOperationException("The human player's placement belongs to PLAYER_INIT.");
        }
    }

    private void EnsureCurrent()
    {
        if (_revision != _document.ChangeGeneration)
        {
            throw new InvalidOperationException("The roster binding is stale; select the record again.");
        }
    }

    private IReadOnlyList<CampaignRosterFieldGroup> BindGroups(List<object?> fields)
    {
        var groups = new List<CampaignRosterFieldGroup>();
        var nets = new List<CampaignRosterField>();
        if (fields[0] is List<object?> choices)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                if (SupportedNumber(choices[i], CampaignRosterFieldKind.NetId))
                {
                    nets.Add(new CampaignRosterField(
                        $"net_{i}",
                        string.Create(CultureInfo.InvariantCulture, $"Net choice [{i}]"),
                        0,
                        Path(0, i),
                        CampaignRosterFieldKind.NetId));
                }
            }
        }
        else
        {
            AddNumber(nets, fields, 0, "net", "Chapter net ID", CampaignRosterFieldKind.NetId);
        }

        AddGroup(groups, "AI net IDs", nets,
            "A supported scalar or numeric list choice is edited in place. "
            + "Negative one is no net; initial roster lists draw among their choices, including duplicates. "
            + "Netless wingmen escort, while other initial roster blocks can be skipped. "
            + "A template plan uses its first eligible choice for range defaults; the generator chooses its patrol net.");

        var durability = new List<CampaignRosterField>();
        AddNumber(durability, fields, 7, "health", "Initial health", CampaignRosterFieldKind.Durability);
        AddNumber(durability, fields, 66, "armor", "Armor", CampaignRosterFieldKind.Durability);
        AddGroup(groups, "Durability", durability,
            "Health uses the default at -1 or 0; armor uses the default at -1 and treats 0 as an override.");

        var activation = new List<CampaignRosterField>();
        AddNumber(activation, fields, 21, "deactivated", "Initially deactivated (-1/0/1)",
            CampaignRosterFieldKind.SentinelFlag);
        AddNumber(activation, fields, 34, "nitro", "Nitro (-1/0/1)", CampaignRosterFieldKind.SentinelFlag);
        AddGroup(groups, "Activation and nitro", activation,
            "Deactivated starts the actor inactive; nitro enables its injector. -1 and 0 read as off.");

        var skills = new List<CampaignRosterField>();
        string[] skillNames =
        {
            "Daredevil", "Natural touch", "Sixth sense", "Dead eye", "Quick draw",
            "Steady hand", "Stun recovery", "Talker", "Constitution",
        };
        for (int i = 0; i < skillNames.Length; i++)
        {
            AddNumber(skills, fields, 22 + i, $"skill_{i}", skillNames[i] + " (-1 or 0-9)",
                CampaignRosterFieldKind.Rating);
        }

        AddNumber(skills, fields, 67, "ace", "Ace (-1/0/1)", CampaignRosterFieldKind.SentinelFlag);
        AddGroup(groups, "Pilot ratings", skills,
            "Each authored rating is independent: -1 defers to the vehicle definition and 0 is a rating. "
            + "Difficulty can shift the ratings; an ace is exempt.");

        var maneuvers = new List<CampaignRosterField>();
        AddNumber(maneuvers, fields, 32, "signature", "Signature maneuver mask",
            CampaignRosterFieldKind.SignatureMask);
        AddGroup(groups, "Signature maneuvers", maneuvers,
            "A 17-bit mask in the runtime maneuver order; 0 or -1 means no signature.");

        var targets = new List<CampaignRosterField>();
        AddText(targets, fields, 6, "primary", "Primary target or escort leader");
        AddText(targets, fields, 20, "title", "Display message key");
        AddGroup(groups, "Target and display", targets,
            "Primary target names an escort leader on a netless wingman and a target otherwise. "
            + "The display key overrides the vehicle title when present.");

        var marker = new List<CampaignRosterField>();
        AddNumber(marker, fields, 37, "marker", "Objective marker (-1/0/1)",
            CampaignRosterFieldKind.SentinelFlag);
        if (marker.Count != 0 && AiSkills.RosterObjectiveTarget(fields))
        {
            AddText(marker, fields, 38, "category", "Marker label message key");
            AddText(marker, fields, 39, "help", "Marker category message key");
        }

        AddGroup(groups, "Objective marker", marker,
            "The flag marks this actor. Label strings are editable only while the marker is enabled; "
            + "otherwise slot 39 may be designer text.");

        var taxi = new List<CampaignRosterField>();
        AddText(taxi, fields, 40, "taxi", "Scripted taxi path");
        AddGroup(groups, "Scripted path", taxi,
            "An existing string names a chapter-world path. Numeric or absent no-path values remain raw.");

        var voice = new List<CampaignRosterField>();
        AddNumber(voice, fields, 65, "accent", "Voice accent ID (-1 unset)",
            CampaignRosterFieldKind.NetId);
        AddGroup(groups, "Voice", voice,
            "The accent selects a voice entry. Its audio assets require runtime verification.");
        return groups.AsReadOnly();
    }

    private void AddNumber(
        List<CampaignRosterField> target,
        List<object?> fields,
        int slot,
        string key,
        string label,
        CampaignRosterFieldKind kind)
    {
        if ((uint)slot < (uint)fields.Count && SupportedNumber(fields[slot], kind))
        {
            target.Add(new CampaignRosterField(key, label, slot, Path(slot), kind));
        }
    }

    private void AddText(
        List<CampaignRosterField> target,
        List<object?> fields,
        int slot,
        string key,
        string label)
    {
        if ((uint)slot < (uint)fields.Count && fields[slot] is string)
        {
            target.Add(new CampaignRosterField(key, label, slot, Path(slot), CampaignRosterFieldKind.Text));
        }
    }

    private float Scalar(int slot) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { _rootIndex, 1, slot })!;

    private float Vector(int component) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { _rootIndex, 1, 1, component })!;

    private float UnchangedInteger(int slot, int value)
    {
        float previous = Scalar(slot);
        return previous == value ? previous : value;
    }

    private ImmutableArray<int> Path(params int[] suffix)
    {
        var path = ImmutableArray.CreateBuilder<int>(suffix.Length + 2);
        path.Add(_rootIndex);
        path.Add(1);
        path.AddRange(suffix);
        return path.MoveToImmutable();
    }
}

internal sealed record CampaignRosterField(
    string Key,
    string Label,
    int Slot,
    ImmutableArray<int> Path,
    CampaignRosterFieldKind Kind);

internal sealed record CampaignRosterFieldGroup(
    string Label,
    IReadOnlyList<CampaignRosterField> Fields,
    string Note);

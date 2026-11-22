using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using CSVM.Mech3;
using CSVM.Session.Objectives;

namespace CSVM.Tooling;

internal enum CampaignObjectiveFieldKind
{
    Name,
    Message,
    IdentityClass,
    Priority,
    Objective,
    OptionalObjective,
    DormantUntil,
    NonNegative,
}

internal readonly record struct CampaignObjectivePair(
    string Key,
    int Index,
    int ValueIndex,
    List<object?>? Values);

internal sealed record CampaignObjectiveField(
    string Label,
    ImmutableArray<int> Path,
    CampaignObjectiveFieldKind Kind);

internal sealed record CampaignObjectiveRow(
    string Label,
    IReadOnlyList<CampaignObjectiveField> Fields,
    string Note = "");

internal sealed record CampaignObjectiveDirective(
    string Key,
    int RawIndex,
    bool Selected,
    IReadOnlyList<CampaignObjectiveRow> Rows,
    string Note);

internal sealed record CampaignObjectiveEntry(
    int Number,
    int RawIndex,
    int DuplicateBlocks,
    IReadOnlyList<CampaignObjectiveDirective> Directives,
    string Graph);

internal sealed record CampaignObjectivePath(
    IReadOnlyList<string> Segments,
    IReadOnlyList<ImmutableArray<int>> Leaves);

/// <summary>Revision-bound views and leaf edits over the original objective reader.</summary>
internal sealed class CampaignMissionObjectives
{
    private static readonly HashSet<string> KnownDirectives = new(StringComparer.Ordinal)
    {
        "BEGIN_DORMANT", "TICK_DEPENDS_ON_OBJ", "SLEEP_ANIM", "HIDE_OBJ",
        "INACTIVE_COMPLETION_COUNT", "ANIM_STATE", "DANGER_ZONES_COMPLETED",
        "DANGER_ZONES_COMPLETION_COUNT", "DEDG", "TRAVELERS", "WAKEUP_ENEMIES",
        "WAKEUP_TURRETS", "WAKEUP_ZEP_TURRETS", "WAKEUP_SOUND_GROUP",
        "WAKEUP_GENERATOR", "WAKE_ANIM", "RESET_TIMER", "COMPLETED_SOUND_GROUP",
        "ADD_OTHER_TARGET", "REMOVE_OTHER_TARGET", "ADD_OBJECTIVE_TARGET",
        "REMOVE_OBJECTIVE_TARGET", "START_TAXI", "STOP_QUEUED_SOUNDS", "SET_AI_TEAM",
        "SET_AI_NET", "SET_AI_ATTACK_RADIUS", "COMPLETED_ZEPCANNONS",
        "COMPLETED_STOPPOINT", "SET_HELP_LABEL", "WARP_VEHICLE",
        "ADJUST_TIMER_WHEN_I_COMPLETE", "TIMER_ADJUST", "END_TIMER",
        "WAKE_OBJECTIVE_WHEN_I_COMPLETE", "WAKE_OBJECTIVE",
        "KILL_OBJECTIVE_WHEN_I_COMPLETE", "SLEEP_OBJECTIVE_WHEN_I_COMPLETE",
        "WAKE_OBJECTIVE_WHEN_I_SLEEP", "NAP_OBJECTIVE_WHEN_I_COMPLETE",
        "IDENTITY", "INSTANTWIN", "INSTANTLOSS", "WON", "LOST",
    };

    private static readonly HashSet<string> FileKeys = new(StringComparer.Ordinal)
    {
        "MISSION_TIMER", "PLAYER_INIT", "RESTORE_ANIMS", "EXECUTE_ANIMS", "INVALIDATE_ANIMS",
        "PRIMARY_COMPLETE_SOUND", "SECONDARY_COMPLETE_SOUND", "TERTIARY_COMPLETE_SOUND",
        "MISSION_WON_SOUND", "MISSION_LOST_SOUND", "OBJECTIVES_WON_SOUND", "OBJECTIVES_LOST_SOUND",
        "WIN_ANIM", "LOSS_ANIM", "NOLOSS", "READ_TIME", "REVIEW_DELAY", "OBJECTIVE_DELAY",
        "PLAY_LOSS_ANIM_WHEN_MAVERICK_ON_RUNWAY", "ANIMATION_DEFINITION_FILE",
    };

    private readonly CampaignMissionDocument _document;
    private readonly long _revision;

    private CampaignMissionObjectives(
        CampaignMissionDocument document,
        int fileIndex,
        string problem,
        IReadOnlyList<CampaignObjectiveEntry> entries)
    {
        _document = document;
        _revision = document.ChangeGeneration;
        FileIndex = fileIndex;
        Problem = problem;
        Entries = entries;
    }

    internal int FileIndex { get; }

    internal string Problem { get; }

    internal IReadOnlyList<CampaignObjectiveEntry> Entries { get; }

    internal bool IsCurrent => _document.ChangeGeneration == _revision;

    internal static CampaignMissionObjectives Bind(CampaignMissionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int fileIndex = document.FindFileIndex("objectives.json");
        if (fileIndex < 0)
        {
            return new(document, -1, "The mission has no objectives reader.", Array.Empty<CampaignObjectiveEntry>());
        }

        List<object?> root = document.Files[fileIndex].Root;
        if (root.Count == 0 || root[0] is not List<object?> body)
        {
            return new(document, fileIndex, "The objectives root is malformed; it remains available in Raw.",
                Array.Empty<CampaignObjectiveEntry>());
        }

        List<CampaignObjectivePair> pairs = Pairs(body);
        ObjectiveScript script = ObjectiveScript.Parse(root);
        var entries = new List<CampaignObjectiveEntry>();
        foreach (ObjectiveDef def in script.Objectives)
        {
            string key = ObjectiveKey(def.Number);
            CampaignObjectivePair selected = pairs.First(pair => pair.Key == key);
            var directives = selected.Values == null
                ? Array.Empty<CampaignObjectiveDirective>()
                : BindDirectives(selected.Values, selected.ValueIndex);
            entries.Add(new CampaignObjectiveEntry(
                def.Number,
                selected.Index,
                pairs.Count(pair => pair.Key == key) - 1,
                directives,
                Graph(def)));
        }

        return new(document, fileIndex,
            entries.Count == 0 ? "No contiguous OBJECTIVE1 block is selected by the runtime." : "",
            entries.AsReadOnly());
    }

    internal static List<CampaignObjectivePair> Pairs(List<object?> list)
    {
        var pairs = new List<CampaignObjectivePair>();
        for (int i = 0; i < list.Count;)
        {
            if (list[i] is not string key)
            {
                i++;
            }
            else if (i + 1 < list.Count && list[i + 1] is List<object?> values)
            {
                pairs.Add(new CampaignObjectivePair(key, i, i + 1, values));
                i += 2;
            }
            else
            {
                pairs.Add(new CampaignObjectivePair(key, i, -1, null));
                i++;
            }
        }

        return pairs;
    }

    internal static bool IsSelected(
        IReadOnlyList<CampaignObjectivePair> pairs,
        CampaignObjectivePair pair)
    {
        if (pair.Key == "WAKE_OBJECTIVE"
            && pairs.Any(candidate => candidate.Key == "WAKE_OBJECTIVE_WHEN_I_COMPLETE"))
        {
            return false;
        }

        if (pair.Key == "TIMER_ADJUST"
            && pairs.Any(candidate => candidate.Key == "ADJUST_TIMER_WHEN_I_COMPLETE"))
        {
            return false;
        }

        return pairs.First(candidate => candidate.Key == pair.Key).Index == pair.Index;
    }

    internal static bool TryObjectiveNumber(string key, out int number)
    {
        number = 0;
        return key.StartsWith("OBJECTIVE", StringComparison.Ordinal)
            && int.TryParse(key.AsSpan("OBJECTIVE".Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out number)
            && number > 0
            && key == ObjectiveKey(number);
    }

    internal static bool IsNumberedBlockCandidate(string key)
    {
        const string prefix = "OBJECTIVE";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || key.Length == prefix.Length)
        {
            return false;
        }

        for (int i = prefix.Length; i < key.Length; i++)
        {
            if (key[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsInactive(string key)
    {
        const string prefix = "INACTIVE";
        return key.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            && number >= 1 && number <= 100
            && key == prefix + number.ToString(CultureInfo.InvariantCulture);
    }

    internal static bool IsKnownDirective(string key) =>
        KnownDirectives.Contains(key) || IsInactive(key);

    internal static List<(string Name, ImmutableArray<int> Path)> Names(
        List<object?> values,
        ImmutableArray<int> path)
    {
        var names = new List<(string, ImmutableArray<int>)>();
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] is string name)
            {
                names.Add((name, path.Add(i)));
            }
            else if (values[i] is List<object?> inner)
            {
                for (int j = 0; j < inner.Count; j++)
                {
                    if (inner[j] is string nested)
                    {
                        names.Add((nested, path.Add(i).Add(j)));
                    }
                }
            }
        }

        return names;
    }

    internal static CampaignObjectivePath? Target(object? value, ImmutableArray<int> path)
    {
        List<(string Name, ImmutableArray<int> Path)> names = value switch
        {
            string name => new List<(string, ImmutableArray<int>)> { (name, path) },
            List<object?> inner => Names(inner, path),
            _ => new List<(string, ImmutableArray<int>)>(),
        };
        return names.Count == 0
            ? null
            : new CampaignObjectivePath(names.Select(name => name.Name).ToArray(),
                names.Select(name => name.Path).ToArray());
    }

    internal object Value(CampaignObjectiveField field)
    {
        EnsureCurrent();
        return _document.ValueAt(FileIndex, field.Path)
            ?? throw new InvalidOperationException("The selected objective leaf is no longer available.");
    }

    internal bool Apply(CampaignObjectiveRow row, IReadOnlyList<object> values)
    {
        EnsureCurrent();
        if (!Entries.SelectMany(entry => entry.Directives)
                .SelectMany(directive => directive.Rows)
                .Any(candidate => ReferenceEquals(candidate, row))
            || values.Count != row.Fields.Count)
        {
            throw new ArgumentException("The edited fields do not match this objective row.", nameof(values));
        }

        var oldValues = new object[row.Fields.Count];
        bool changed = false;
        for (int i = 0; i < row.Fields.Count; i++)
        {
            oldValues[i] = Value(row.Fields[i]);
            changed |= !SameValue(oldValues[i], values[i]);
        }

        if (!changed)
        {
            return false;
        }

        var edits = new List<(IReadOnlyList<int> Path, object Value)>();
        for (int i = 0; i < row.Fields.Count; i++)
        {
            Validate(row.Fields[i], oldValues[i], values[i], Entries.Count);
            edits.Add((row.Fields[i].Path, values[i]));
        }

        return _document.SetObjectiveLeaves(FileIndex, _revision, edits);
    }

    internal bool SwapNumbers(int first, int second)
    {
        EnsureCurrent();
        if (first < 1 || first > Entries.Count || second < 1 || second > Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(first), "Choose two selected objective numbers.");
        }

        if (first == second)
        {
            return false;
        }

        List<object?> root = _document.Files[FileIndex].Root;
        var body = (List<object?>)root[0]!;
        var edits = new List<(IReadOnlyList<int> Path, object Value)>();
        foreach (CampaignObjectivePair pair in Pairs(body))
        {
            if (TryObjectiveNumber(pair.Key, out int number))
            {
                if (number < 1 || number > Entries.Count)
                {
                    throw new InvalidOperationException("A numbered block is outside the contiguous runtime graph.");
                }

                int replacement = Swap(number, first, second);
                if (replacement != number)
                {
                    edits.Add((ImmutableArray.Create(0, pair.Index), ObjectiveKey(replacement)));
                }

                if (pair.Values != null)
                {
                    ValidateAndRemapLinks(pair.Values, pair.ValueIndex, first, second, edits);
                }
            }
            else if (!FileKeys.Contains(pair.Key))
            {
                throw new InvalidOperationException(
                    $"The file-level key {pair.Key} is not safe to renumber. Inspect it in Raw.");
            }
            else if (pair.Key.StartsWith("OBJECTIVE", StringComparison.OrdinalIgnoreCase)
                && ContainsIdentity(pair.Values))
            {
                throw new InvalidOperationException("A file-level key also contains briefing identities.");
            }
            else if (pair.Values != null && ContainsObjectiveBearingData(pair.Values))
            {
                throw new InvalidOperationException(
                    $"The file-level key {pair.Key} contains an ambiguous objective reference.");
            }
        }

        if (HasUnpairedValues(body))
        {
            throw new InvalidOperationException("The objective file has unpaired values; inspect it in Raw.");
        }

        return _document.SetObjectiveLeaves(FileIndex, _revision, edits);
    }

    private static CampaignObjectiveDirective[] BindDirectives(List<object?> block, int blockIndex)
    {
        List<CampaignObjectivePair> pairs = Pairs(block);
        var result = new List<CampaignObjectiveDirective>();
        foreach (CampaignObjectivePair pair in pairs)
        {
            bool selected = IsSelected(pairs, pair);
            IReadOnlyList<CampaignObjectiveRow> rows = selected && pair.Values != null
                ? BindRows(pair.Key, pair.Values, ImmutableArray.Create(0, blockIndex, pair.ValueIndex))
                : Array.Empty<CampaignObjectiveRow>();
            string note = !selected
                ? "Shadowed by the runtime's first matching directive or its preferred alias. Raw is preserved."
                : pair.Values == null
                    ? "Bare directive or null body. Raw is preserved."
                    : rows.Count == 0
                        ? "Unsupported or malformed shape. Inspect and edit it in Raw."
                        : "Only the displayed leaves are editable; other values remain raw.";
            result.Add(new CampaignObjectiveDirective(pair.Key, pair.Index, selected, rows, note));
        }

        return result.ToArray();
    }

    private static IReadOnlyList<CampaignObjectiveRow> BindRows(
        string key,
        List<object?> values,
        ImmutableArray<int> path)
    {
        var rows = new List<CampaignObjectiveRow>();
        var fields = new List<CampaignObjectiveField>();
        void StringField(int index, string label, CampaignObjectiveFieldKind kind = CampaignObjectiveFieldKind.Name)
        {
            if (index < values.Count && values[index] is string)
            {
                fields.Add(new CampaignObjectiveField(label, path.Add(index), kind));
            }
        }

        void FloatField(int index, string label, CampaignObjectiveFieldKind kind)
        {
            if (index < values.Count && values[index] is float number && float.IsFinite(number))
            {
                fields.Add(new CampaignObjectiveField(label, path.Add(index), kind));
            }
        }

        if (key == "IDENTITY" && values.Count >= 2 && values[0] is string
            && values[1] is float priority && float.IsFinite(priority))
        {
            StringField(0, "Display class", CampaignObjectiveFieldKind.IdentityClass);
            FloatField(1, "Display priority", CampaignObjectiveFieldKind.Priority);
            StringField(2, "Briefing message", CampaignObjectiveFieldKind.Message);
        }
        else if (key == "BEGIN_DORMANT")
        {
            FloatField(0, "Wake time", CampaignObjectiveFieldKind.DormantUntil);
            FloatField(1, "Awake duration", CampaignObjectiveFieldKind.NonNegative);
            FloatField(2, "Nap duration", CampaignObjectiveFieldKind.NonNegative);
            FloatField(3, "Deadline", CampaignObjectiveFieldKind.NonNegative);
        }
        else if (key is "TICK_DEPENDS_ON_OBJ" or "HIDE_OBJ")
        {
            FloatField(0, "Objective number (0 disables)", CampaignObjectiveFieldKind.OptionalObjective);
        }
        else if (key == "NAP_OBJECTIVE_WHEN_I_COMPLETE")
        {
            FloatField(0, "Objective number", CampaignObjectiveFieldKind.Objective);
            FloatField(1, "Nap seconds", CampaignObjectiveFieldKind.NonNegative);
        }
        else if (IsLinkList(key))
        {
            for (int i = 0; i < values.Count; i++)
            {
                FloatField(i, $"Objective [{i}]", CampaignObjectiveFieldKind.Objective);
            }
        }
        else if (IsInactive(key))
        {
            AddPathFields(fields, Names(values, path), "World path");
        }
        else if (IsTargetList(key))
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (Target(values[i], path.Add(i)) is { } target)
                {
                    var targetFields = new List<CampaignObjectiveField>();
                    AddPathFields(targetFields, target.Leaves, "Target");
                    rows.Add(new CampaignObjectiveRow($"Target [{i}]", targetFields.AsReadOnly()));
                }
            }
        }
        else if (key == "SET_HELP_LABEL" && values.Count >= 2 && values[^1] is string
            && Target(values[0], path.Add(0)) is { } target)
        {
            AddPathFields(fields, target.Leaves, "Target");
            StringField(values.Count - 1, "Help message", CampaignObjectiveFieldKind.Message);
        }

        if (fields.Count > 0)
        {
            rows.Add(new CampaignObjectiveRow(key, fields.AsReadOnly(),
                key == "IDENTITY"
                    ? "Priority groups runtime display and reward rows; it is not the graph number."
                    : ""));
        }

        return rows.AsReadOnly();
    }

    private static void AddPathFields(
        List<CampaignObjectiveField> fields,
        IEnumerable<(string Name, ImmutableArray<int> Path)> names,
        string label) =>
        AddPathFields(fields, names.Select(name => name.Path), label);

    private static void AddPathFields(
        List<CampaignObjectiveField> fields,
        IEnumerable<ImmutableArray<int>> paths,
        string label)
    {
        int i = 0;
        foreach (ImmutableArray<int> path in paths)
        {
            fields.Add(new CampaignObjectiveField($"{label} segment {++i}", path, CampaignObjectiveFieldKind.Name));
        }
    }

    private static bool IsTargetList(string key) =>
        key is "ADD_OTHER_TARGET" or "REMOVE_OTHER_TARGET" or "ADD_OBJECTIVE_TARGET" or "REMOVE_OBJECTIVE_TARGET";

    private static bool IsLinkList(string key) =>
        key is "WAKE_OBJECTIVE_WHEN_I_COMPLETE" or "WAKE_OBJECTIVE"
            or "KILL_OBJECTIVE_WHEN_I_COMPLETE" or "SLEEP_OBJECTIVE_WHEN_I_COMPLETE"
            or "WAKE_OBJECTIVE_WHEN_I_SLEEP";

    private static bool SameValue(object old, object value) =>
        (old is string text && value is string otherText && text == otherText)
        || (old is float number && value is float otherNumber
            && BitConverter.SingleToUInt32Bits(number) == BitConverter.SingleToUInt32Bits(otherNumber));

    private static void Validate(CampaignObjectiveField field, object old, object value, int count)
    {
        if (old is string && value is string text)
        {
            if ((field.Kind != CampaignObjectiveFieldKind.Message && string.IsNullOrWhiteSpace(text))
                || text != text.Trim()
                || text.Any(char.IsControl))
            {
                throw new ArgumentException($"{field.Label} must not contain control characters or surrounding whitespace.");
            }

            if (field.Kind == CampaignObjectiveFieldKind.IdentityClass && !IsIdentityClass(text))
            {
                throw new ArgumentException("Display class must be PRIMARY, SECONDARY, TERTIARY, or NONE.");
            }

            return;
        }

        if (old is not float || value is not float number || !float.IsFinite(number))
        {
            throw new ArgumentException($"{field.Label} must preserve its type and contain a finite value.");
        }

        bool valid = field.Kind switch
        {
            CampaignObjectiveFieldKind.Objective => number >= 1 && number <= count && number == MathF.Truncate(number),
            CampaignObjectiveFieldKind.OptionalObjective => number >= 0 && number <= count && number == MathF.Truncate(number),
            CampaignObjectiveFieldKind.Priority => number >= 0 && number < 32 && number == MathF.Truncate(number),
            CampaignObjectiveFieldKind.DormantUntil => number >= -1,
            CampaignObjectiveFieldKind.NonNegative => number >= 0,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException($"{field.Label} is outside the supported range.");
        }
    }

    private static bool HasUnpairedValues(List<object?> list)
    {
        for (int i = 0; i < list.Count;)
        {
            if (list[i] is string)
            {
                i += i + 1 < list.Count && list[i + 1] is List<object?> ? 2 : 1;
            }
            else if (list[i] == null)
            {
                i++;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIdentity(List<object?>? values) =>
        values != null && Pairs(values).Any(pair => pair.Key.Equals("IDENTITY", StringComparison.OrdinalIgnoreCase));

    private static bool IsValidIdentity(List<object?> values)
    {
        if (values.Count < 2 || values[0] is not string name || values[1] is not float priority)
        {
            return false;
        }

        return IsIdentityClass(name)
            && float.IsFinite(priority) && priority >= 0 && priority < 32 && priority == MathF.Truncate(priority)
            && (values.Count < 3 || values[2] is string);
    }

    private static bool IsIdentityClass(string value) =>
        value.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
        || value.Equals("SECONDARY", StringComparison.OrdinalIgnoreCase)
        || value.Equals("TERTIARY", StringComparison.OrdinalIgnoreCase)
        || value.Equals("NONE", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsObjectiveBearingData(List<object?> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] is List<object?> nested && ContainsObjectiveBearingData(nested))
            {
                return true;
            }

            if (values[i] is not string text)
            {
                continue;
            }

            bool numbered = text.StartsWith("OBJECTIVE", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(text.AsSpan("OBJECTIVE".Length), NumberStyles.None,
                    CultureInfo.InvariantCulture, out _);
            string canonical = text.ToUpperInvariant();
            bool directive = IsLinkList(canonical)
                || canonical is "TICK_DEPENDS_ON_OBJ" or "HIDE_OBJ" or "NAP_OBJECTIVE_WHEN_I_COMPLETE"
                || (canonical.Contains("OBJECTIVE", StringComparison.Ordinal)
                    && i + 1 < values.Count && values[i + 1] is List<object?>);
            if (numbered || directive)
            {
                return true;
            }
        }

        return false;
    }

    private static int Swap(int value, int first, int second) =>
        value == first ? second : value == second ? first : value;

    private static string ObjectiveKey(int number) =>
        "OBJECTIVE" + number.ToString(CultureInfo.InvariantCulture);

    private static string Graph(ObjectiveDef def)
    {
        var edges = new List<string>();
        void Add(string name, IEnumerable<int> targets)
        {
            string value = string.Join(", ", targets);
            if (value.Length > 0)
            {
                edges.Add($"{name}: {value}");
            }
        }

        if (def.TickDependsOn != 0)
        {
            edges.Add($"depends: {def.TickDependsOn}");
        }

        Add("wake on completion", def.WakeWhenComplete);
        Add("kill", def.KillWhenComplete);
        Add("sleep", def.SleepWhenComplete);
        Add("wake on sleep", def.WakeWhenSleep);
        if (def.NapWhenComplete is { } nap)
        {
            edges.Add($"nap: {nap.Target}");
        }

        if (def.HideObj is { } hide)
        {
            edges.Add($"hide: {hide}");
        }

        return edges.Count == 0 ? "No runtime objective links." : string.Join(" | ", edges);
    }

    private void ValidateAndRemapLinks(
        List<object?> block,
        int blockIndex,
        int first,
        int second,
        List<(IReadOnlyList<int> Path, object Value)> edits)
    {
        if (HasUnpairedValues(block))
        {
            throw new InvalidOperationException("An objective has unpaired values; inspect it in Raw.");
        }

        foreach (CampaignObjectivePair pair in Pairs(block))
        {
            if (!IsKnownDirective(pair.Key))
            {
                throw new InvalidOperationException(
                    $"The directive {pair.Key} is not known to be safe to renumber.");
            }

            if (pair.Key == "IDENTITY" && pair.Values != null && !IsValidIdentity(pair.Values))
            {
                throw new InvalidOperationException("An identity has a malformed display class, priority or message.");
            }

            bool all = IsLinkList(pair.Key);
            bool single = pair.Key is "TICK_DEPENDS_ON_OBJ" or "HIDE_OBJ" or "NAP_OBJECTIVE_WHEN_I_COMPLETE";
            if (!all && !single)
            {
                if (pair.Values != null && ContainsObjectiveBearingData(pair.Values))
                {
                    throw new InvalidOperationException(
                        $"The directive {pair.Key} contains an ambiguous nested objective reference.");
                }

                continue;
            }

            if (pair.Values == null)
            {
                continue;
            }

            if (single && (pair.Values.Count == 0
                || (pair.Key != "NAP_OBJECTIVE_WHEN_I_COMPLETE" && pair.Values.Count != 1)
                || (pair.Key == "NAP_OBJECTIVE_WHEN_I_COMPLETE"
                    && (pair.Values.Count > 2 || (pair.Values.Count == 2
                        && (pair.Values[1] is not float duration || !float.IsFinite(duration)))))))
            {
                throw new InvalidOperationException($"The {pair.Key} link has a malformed shape.");
            }

            int limit = all ? pair.Values.Count : 1;
            for (int i = 0; i < limit; i++)
            {
                if (pair.Values[i] is not float value || !float.IsFinite(value)
                    || value != MathF.Truncate(value)
                    || value < (pair.Key is "TICK_DEPENDS_ON_OBJ" or "HIDE_OBJ" ? 0 : 1)
                    || value > Entries.Count)
                {
                    throw new InvalidOperationException($"The {pair.Key} link is malformed or unresolved.");
                }

                int number = (int)value;
                int replacement = Swap(number, first, second);
                if (replacement != number)
                {
                    edits.Add((ImmutableArray.Create(0, blockIndex, pair.ValueIndex, i), (float)replacement));
                }
            }
        }
    }

    private void EnsureCurrent()
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("The objective binding is stale; select the record again.");
        }
    }
}

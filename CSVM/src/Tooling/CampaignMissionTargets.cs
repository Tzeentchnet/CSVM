using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CSVM.Mech3;

namespace CSVM.Tooling;

internal enum CampaignTargetOrigin
{
    Mission,
    Chapter,
    None,
    Unavailable,
    Invalid,
}

internal enum CampaignTargetLabel
{
    Description,
    Category,
    Help,
}

internal readonly record struct CampaignTargetState(
    string? Description,
    string? Category,
    string? Help,
    bool Objective,
    bool OtherTarget);

internal sealed record CampaignTargetNode(
    string Key,
    ImmutableArray<string> Segments,
    ImmutableArray<int> Path,
    ImmutableArray<ImmutableArray<int>> LeafPaths,
    int PairIndex,
    int NodeIndex,
    bool Selected,
    bool Effective);

internal sealed class CampaignTargetRow
{
    internal CampaignTargetRow(int index, List<object?> raw)
    {
        Index = index;
        Raw = raw;
    }

    internal int Index { get; }

    internal List<object?> Raw { get; }

    internal int DescriptionIndex { get; set; } = -1;

    internal int CategoryIndex { get; set; } = -1;

    internal int HelpIndex { get; set; } = -1;

    internal int NodesIndex { get; set; } = -1;

    internal bool Objective { get; set; }

    internal bool OtherTarget { get; set; }

    internal List<CampaignTargetNode> Nodes { get; } = new();

    internal bool HasSelectedNode => Nodes.Any(node => node.Selected);

    internal bool HasEffectiveNode => Nodes.Any(node => node.Effective);

    internal int LabelIndex(CampaignTargetLabel label) => label switch
    {
        CampaignTargetLabel.Description => DescriptionIndex,
        CampaignTargetLabel.Category => CategoryIndex,
        CampaignTargetLabel.Help => HelpIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(label)),
    };

    internal string? Label(CampaignTargetLabel label)
    {
        int index = LabelIndex(label);
        return index >= 0
            && Raw[index] is List<object?> { Count: > 1 } pair
            ? pair[1] as string
            : null;
    }

    internal bool CanEditLabel(CampaignTargetLabel label)
    {
        int index = LabelIndex(label);
        return index < 0
            || (Raw[index] is List<object?> { Count: > 1 } pair
                && pair[1] is string);
    }
}

/// <summary>Runtime-selected target records over one mission document and its read-only chapter.
/// A chapter table is writable only after an explicit mission-local copy.</summary>
internal sealed class CampaignMissionTargets
{
    private readonly CampaignMissionDocument _document;
    private readonly ZrdrSet _chapter;
    private readonly long _revision;
    private readonly List<object?>? _inheritedSnapshot;

    private CampaignMissionTargets(
        CampaignMissionDocument document,
        ZrdrSet chapter,
        CampaignTargetOrigin origin,
        int? fileIndex,
        ZrdrFile? file,
        string problem)
    {
        _document = document;
        _chapter = chapter;
        _revision = document.ChangeGeneration;
        Origin = origin;
        FileIndex = fileIndex;
        FileKey = file?.Key;
        Problem = problem;
        _inheritedSnapshot = origin == CampaignTargetOrigin.Chapter && file != null
            ? CampaignMissionDocument.Copy(file.Root)
            : null;
        var entries = new List<CampaignTargetEntry>();
        if (file != null)
        {
            foreach (CampaignTargetRow row in Scan(file.Root))
            {
                entries.Add(new CampaignTargetEntry(
                    document,
                    origin,
                    fileIndex,
                    _revision,
                    row));
            }
        }

        Entries = entries.AsReadOnly();
    }

    internal CampaignTargetOrigin Origin { get; }

    internal int? FileIndex { get; }

    internal string? FileKey { get; }

    internal string Problem { get; }

    internal IReadOnlyList<CampaignTargetEntry> Entries { get; }

    internal bool IsCurrent => _revision == _document.ChangeGeneration;

    internal bool CanCopy => Origin == CampaignTargetOrigin.Chapter;

    internal static CampaignMissionTargets Bind(
        CampaignMissionDocument document,
        ZrdrSet chapter,
        bool chapterAvailable)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(chapter);
        int index = document.FindFileIndex("targets.json");
        if (index >= 0)
        {
            ZrdrFile local = document.Files[index];
            return local.Root.Count == 0
                ? new CampaignMissionTargets(
                    document, chapter, CampaignTargetOrigin.Invalid, index, null,
                    "The mission targets reader is empty; MissionTargets rejects it.")
                : new CampaignMissionTargets(
                    document, chapter, CampaignTargetOrigin.Mission, index, local, "");
        }

        if (!chapterAvailable || !chapter.Exists)
        {
            return new CampaignMissionTargets(
                document, chapter, CampaignTargetOrigin.Unavailable, null, null,
                "The chapter reader scope is unavailable.");
        }

        ZrdrFile? inherited = ZrdrZbd.Find(chapter.Files, "targets.json");
        if (inherited == null)
        {
            return new CampaignMissionTargets(
                document, chapter, CampaignTargetOrigin.None, null, null,
                "Neither the mission nor the chapter has a targets reader.");
        }

        return inherited.Root.Count == 0
            ? new CampaignMissionTargets(
                document, chapter, CampaignTargetOrigin.Invalid, null, null,
                "The chapter targets reader is empty; MissionTargets rejects it.")
            : new CampaignMissionTargets(
                document, chapter, CampaignTargetOrigin.Chapter, null, inherited, "");
    }

    internal static IReadOnlyList<CampaignTargetRow> Scan(List<object?> root)
    {
        var rows = new List<CampaignTargetRow>();
        for (int rootIndex = 0; rootIndex < root.Count; rootIndex++)
        {
            if (root[rootIndex] is not List<object?> entry)
            {
                continue;
            }

            var row = new CampaignTargetRow(rootIndex, entry);
            for (int pairIndex = 0; pairIndex < entry.Count; pairIndex++)
            {
                if (entry[pairIndex] is not List<object?> { Count: > 0 } pair
                    || pair[0] is not string key)
                {
                    continue;
                }

                switch (key.ToLowerInvariant())
                {
                    case "description": row.DescriptionIndex = pairIndex; break;
                    case "category_label": row.CategoryIndex = pairIndex; break;
                    case "help_label": row.HelpIndex = pairIndex; break;
                    case "nodes": row.NodesIndex = pairIndex; break;
                    case "objective": row.Objective = true; break;
                    case "other_target": row.OtherTarget = true; break;
                }
            }

            for (int pairIndex = 0; pairIndex < entry.Count; pairIndex++)
            {
                if (entry[pairIndex] is not List<object?> { Count: > 1 } pair
                    || pair[0] is not string key
                    || !key.Equals("nodes", StringComparison.OrdinalIgnoreCase)
                    || pair[1] is not List<object?> nodes)
                {
                    continue;
                }

                for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    CampaignTargetNode? node = Node(
                        nodes[nodeIndex], rootIndex, pairIndex, nodeIndex,
                        pairIndex == row.NodesIndex);
                    if (node != null)
                    {
                        row.Nodes.Add(node);
                    }
                }
            }

            rows.Add(row);
        }

        var winners = new Dictionary<string, (CampaignTargetRow Row, int Index)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (CampaignTargetRow row in rows)
        {
            for (int i = 0; i < row.Nodes.Count; i++)
            {
                if (row.Nodes[i].Selected)
                {
                    winners[row.Nodes[i].Key] = (row, i);
                }
            }
        }

        foreach ((CampaignTargetRow row, int index) in winners.Values)
        {
            row.Nodes[index] = row.Nodes[index] with { Effective = true };
        }

        return rows;
    }

    internal int CopyToMission()
    {
        if (!CanCopy || _revision != _document.ChangeGeneration)
        {
            throw new InvalidOperationException(
                "Only the current inherited chapter table can be copied.");
        }

        return _document.CopyChapterTargets(_chapter, FileKey!, _inheritedSnapshot!);
    }

    private static CampaignTargetNode? Node(
        object? value,
        int rootIndex,
        int pairIndex,
        int nodeIndex,
        bool selected)
    {
        ImmutableArray<int> path =
            ImmutableArray.Create(rootIndex, pairIndex, 1, nodeIndex);
        if (value is string name)
        {
            return new CampaignTargetNode(
                name,
                name.Contains('/')
                    ? ImmutableArray.CreateRange(name.Split('/'))
                    : ImmutableArray<string>.Empty,
                path,
                ImmutableArray.Create(path),
                pairIndex,
                nodeIndex,
                selected,
                false);
        }

        if (value is not List<object?> { Count: > 0 } rawPath)
        {
            return null;
        }

        var segments = ImmutableArray.CreateBuilder<string>(rawPath.Count);
        var leaves =
            ImmutableArray.CreateBuilder<ImmutableArray<int>>(rawPath.Count);
        for (int i = 0; i < rawPath.Count; i++)
        {
            if (rawPath[i] is not string segment)
            {
                return null;
            }

            segments.Add(segment);
            leaves.Add(path.Add(i));
        }

        ImmutableArray<string> names = segments.MoveToImmutable();
        string key = string.Join("/", names);
        return new CampaignTargetNode(
            key,
            ImmutableArray.CreateRange(key.Split('/')),
            path,
            leaves.MoveToImmutable(),
            pairIndex,
            nodeIndex,
            selected,
            false);
    }
}

internal sealed class CampaignTargetEntry
{
    private readonly CampaignMissionDocument _document;
    private readonly CampaignTargetOrigin _origin;
    private readonly int? _fileIndex;
    private readonly long _revision;
    private readonly CampaignTargetRow _row;

    internal CampaignTargetEntry(
        CampaignMissionDocument document,
        CampaignTargetOrigin origin,
        int? fileIndex,
        long revision,
        CampaignTargetRow row)
    {
        _document = document;
        _origin = origin;
        _fileIndex = fileIndex;
        _revision = revision;
        _row = row;
    }

    internal int RawIndex => _row.Index;

    internal IReadOnlyList<CampaignTargetNode> Nodes => _row.Nodes;

    internal bool CanEdit =>
        _origin == CampaignTargetOrigin.Mission && _row.HasSelectedNode;

    internal bool HasEffectiveNode => _row.HasEffectiveNode;

    internal CampaignTargetState State =>
        new(
            _row.Label(CampaignTargetLabel.Description),
            _row.Label(CampaignTargetLabel.Category),
            _row.Label(CampaignTargetLabel.Help),
            _row.Objective,
            _row.OtherTarget);

    internal bool CanEditLabel(CampaignTargetLabel label) =>
        CanEdit && _row.CanEditLabel(label);

    internal bool LabelWellFormed(CampaignTargetLabel label) =>
        _row.CanEditLabel(label);

    internal ImmutableArray<int> LabelPath(CampaignTargetLabel label)
    {
        int index = _row.LabelIndex(label);
        return index >= 0 && _row.CanEditLabel(label)
            ? ImmutableArray.Create(_row.Index, index, 1)
            : ImmutableArray<int>.Empty;
    }

    internal bool Apply(CampaignTargetState state)
    {
        if (!CanEdit || _fileIndex is not int fileIndex)
        {
            throw new InvalidOperationException(
                "Only a mission-local target entry with a runtime node can be edited.");
        }

        if (_revision != _document.ChangeGeneration)
        {
            throw new InvalidOperationException(
                "The target binding is stale; select the entry again.");
        }

        CampaignTargetState current = State;
        ValidateLabel(CampaignTargetLabel.Description, current.Description, state.Description);
        ValidateLabel(CampaignTargetLabel.Category, current.Category, state.Category);
        ValidateLabel(CampaignTargetLabel.Help, current.Help, state.Help);
        ValidateFlag("objective", current.Objective, state.Objective);
        ValidateFlag("other_target", current.OtherTarget, state.OtherTarget);

        List<object?> replacement = CampaignMissionDocument.Copy(_row.Raw);
        SetLabel(replacement, CampaignTargetLabel.Description, state.Description);
        SetLabel(replacement, CampaignTargetLabel.Category, state.Category);
        SetLabel(replacement, CampaignTargetLabel.Help, state.Help);
        SetFlag(replacement, "objective", current.Objective, state.Objective);
        SetFlag(replacement, "other_target", current.OtherTarget, state.OtherTarget);
        return _document.ReplaceTargetEntry(
            fileIndex, _row.Index, _row.Raw, replacement, _revision);
    }

    private static string LabelKey(CampaignTargetLabel label) => label switch
    {
        CampaignTargetLabel.Description => "description",
        CampaignTargetLabel.Category => "category_label",
        CampaignTargetLabel.Help => "help_label",
        _ => throw new ArgumentOutOfRangeException(nameof(label)),
    };

    private static bool IsFlag(object? value, string key) =>
        value is List<object?> { Count: > 0 } pair
        && pair[0] is string name
        && name.Equals(key, StringComparison.OrdinalIgnoreCase);

    private static void SetFlag(
        List<object?> entry,
        string key,
        bool current,
        bool desired)
    {
        if (current == desired)
        {
            return;
        }

        if (desired)
        {
            entry.Add(new List<object?> { key });
            return;
        }

        for (int i = entry.Count - 1; i >= 0; i--)
        {
            if (IsFlag(entry[i], key))
            {
                entry.RemoveAt(i);
            }
        }
    }

    private void ValidateLabel(
        CampaignTargetLabel label,
        string? current,
        string? desired)
    {
        if (string.Equals(current, desired, StringComparison.Ordinal))
        {
            return;
        }

        if (!_row.CanEditLabel(label) || desired == null)
        {
            throw new InvalidOperationException(
                $"The selected {LabelKey(label)} value cannot be replaced safely.");
        }

        if (!string.Equals(desired, desired.Trim(), StringComparison.Ordinal)
            || desired.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The {LabelKey(label)} key cannot contain control characters or surrounding whitespace.");
        }
    }

    private void ValidateFlag(string key, bool current, bool desired)
    {
        if (!current || desired)
        {
            return;
        }

        foreach (object? item in _row.Raw)
        {
            if (IsFlag(item, key)
                && item is List<object?> pair
                && pair.Count > 1
                && (pair.Count != 2 || pair[1] != null))
            {
                throw new InvalidOperationException(
                    $"The {key} flag has extra data and cannot be removed safely.");
            }
        }
    }

    private void SetLabel(
        List<object?> replacement,
        CampaignTargetLabel label,
        string? desired)
    {
        int index = _row.LabelIndex(label);
        if (index < 0)
        {
            if (desired != null)
            {
                replacement.Add(new List<object?> { LabelKey(label), desired });
            }

            return;
        }

        if (_row.CanEditLabel(label) && desired != null)
        {
            ((List<object?>)replacement[index]!)[1] = desired;
        }
    }
}

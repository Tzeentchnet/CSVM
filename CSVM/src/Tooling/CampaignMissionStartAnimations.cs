using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CSVM.Tooling;

internal sealed record CampaignStartAnimationEntry(
    string Name,
    ImmutableArray<int> EntryPath,
    ImmutableArray<int> NamePath,
    List<object?> Raw);

internal sealed class CampaignMissionStartAnimations
{
    private readonly CampaignMissionDocument _document;
    private readonly long _revision;

    private CampaignMissionStartAnimations(
        CampaignMissionDocument document,
        int fileIndex,
        ImmutableArray<CampaignStartAnimationEntry> entries,
        string problem)
    {
        _document = document;
        _revision = document.ChangeGeneration;
        FileIndex = fileIndex;
        Entries = entries;
        Problem = problem;
    }

    internal int FileIndex { get; }
    internal ImmutableArray<CampaignStartAnimationEntry> Entries { get; }
    internal string Problem { get; }
    internal bool IsCurrent => _document.ChangeGeneration == _revision;

    internal static CampaignMissionStartAnimations Bind(CampaignMissionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int index = document.FindFileIndex("startanims.json");
        if (index < 0)
        {
            return new CampaignMissionStartAnimations(
                document, index, ImmutableArray<CampaignStartAnimationEntry>.Empty,
                "No mission startanims reader. The runtime has no start-animation list.");
        }

        List<object?> root = document.Files[index].Root;
        ImmutableArray<CampaignStartAnimationEntry> entries = Scan(root);
        string problem = root.Count == 0 || root[0] is not List<object?>
            ? "The selected reader has no runtime-readable root list; its raw data is unchanged."
            : entries.IsEmpty
                ? "No runtime-readable NEW_GAME_START entries. Other data remains available in Raw."
                : "Only existing names are editable. Unrecognized entries and all tails remain in Raw.";
        return new CampaignMissionStartAnimations(document, index, entries, problem);
    }

    internal static ImmutableArray<CampaignStartAnimationEntry> Scan(List<object?> root)
    {
        var entries = ImmutableArray.CreateBuilder<CampaignStartAnimationEntry>();
        if (root.Count == 0 || root[0] is not List<object?> outer)
        {
            return entries.ToImmutable();
        }

        for (int i = 0; i + 1 < outer.Count; i++)
        {
            if (outer[i] is not string key
                || !key.Equals("NEW_GAME_START", StringComparison.OrdinalIgnoreCase)
                || outer[i + 1] is not List<object?> group)
            {
                continue;
            }

            for (int j = 0; j < group.Count; j++)
            {
                if (group[j] is List<object?> { Count: > 0 } entry
                    && entry[0] is string name)
                {
                    entries.Add(new CampaignStartAnimationEntry(
                        name, ImmutableArray.Create(0, i + 1, j),
                        ImmutableArray.Create(0, i + 1, j, 0), entry));
                }
            }
        }

        return entries.ToImmutable();
    }

    internal bool Rename(int index, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if ((uint)index >= (uint)Entries.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        CampaignStartAnimationEntry entry = Entries[index];
        if (!string.Equals(entry.Name, name, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(name) || HasInvalidCharacters(name)))
        {
            throw new ArgumentException("An animation name must contain text and valid non-control Unicode.", nameof(name));
        }

        return _document.SetStartAnimationLeaves(
            FileIndex, _revision,
            new (IReadOnlyList<int> Path, object Value)[] { (entry.NamePath, name) });
    }

    internal bool Move(int from, int to)
    {
        if ((uint)from >= (uint)Entries.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }

        if ((uint)to >= (uint)Entries.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(to));
        }

        var order = new List<int>(Entries.Length);
        for (int i = 0; i < Entries.Length; i++)
        {
            order.Add(i);
        }

        int moving = order[from];
        order.RemoveAt(from);
        order.Insert(to, moving);
        return _document.ReorderStartAnimationEntries(FileIndex, _revision, Entries, order);
    }

    private static bool HasInvalidCharacters(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (char.IsControl(character) || char.IsLowSurrogate(character))
            {
                return true;
            }

            if (char.IsHighSurrogate(character))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    return true;
                }

                i++;
            }
        }

        return false;
    }
}

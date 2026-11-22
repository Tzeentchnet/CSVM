using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CSVM.Extraction;
using CSVM.Mech3;
using CSVM.Spec;

namespace CSVM.Tooling;

/// <summary>
/// One campaign mission's complete editable reader scope. It owns a deep copy of every
/// <see cref="ZrdrFile"/> in archive order, permits type-preserving scalar edits, and exports the
/// whole scope to the external modding layout. The raw list tree remains canonical.
/// Authoring rules: docs/formats/missions/campaign-authoring.md.
/// </summary>
public sealed class CampaignMissionDocument
{
    private static readonly DateTimeOffset ZipTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<ZrdrFile> _files;
    private readonly string? _sourceLocation;
    // A registrar can fail after publishing an action; that action must stay inert.
    private readonly HashSet<long> _abortedIdentities = new();
    private List<(int FileIndex, ImmutableArray<int> Path, object Value)>? _leafBatch;
    private bool _publishingEdit;
    private long _stateIdentity;
    private long _nextStateIdentity;
    private long _savedStateIdentity;

    private CampaignMissionDocument(
        CampaignMission mission,
        ZrdrSet scope,
        string? sourceLocation)
    {
        if (!scope.Exists)
        {
            throw new FileNotFoundException($"mission reader archive '{ArchivePath(mission)}' is absent");
        }

        Mission = mission;
        _files = new List<ZrdrFile>(scope.Files.Count);
        foreach (var file in scope.Files)
        {
            _files.Add(new ZrdrFile(file.Name, file.Rename, Copy(file.Root)));
        }

        _sourceLocation = sourceLocation;
    }

    internal event Action? Changed;

    internal event Action<CampaignMissionEdit>? EditCommitted;
    internal event Action? EditRolledBack;

    /// <summary>The campaign sequence row whose mission scope this document owns.</summary>
    public CampaignMission Mission { get; }

    /// <summary>Whether the document changed since it was loaded or last exported.</summary>
    public bool IsDirty => _stateIdentity != _savedStateIdentity;

    /// <summary>The current position in this document's edit history.</summary>
    public long Revision { get; private set; }

    /// <summary>The complete reader set in source order. Its trees are read-only by contract;
    /// edits go through <see cref="SetLeaf"/> or controlled typed operations.</summary>
    public IReadOnlyList<ZrdrFile> Files => _files;

    // Undo restores a revision, not the freshness of bindings made at that revision.
    internal long ChangeGeneration { get; private set; }

    /// <summary>The 24 campaign rows read from a user-owned game-file source.</summary>
    public static IReadOnlyList<CampaignMission> LoadMissions(IGameFileSource game)
    {
        var root = ZrdrSet.Load(game, GameData.RootZrdrPath);
        if (!root.Exists)
        {
            throw new FileNotFoundException(
                $"shared reader archive '{GameData.RootZrdrPath}' is absent");
        }

        var missions = CampaignSequence.Load(root);
        if (missions.Count != CampaignSequence.MissionCount)
        {
            throw new InvalidDataException(
                Invariant(
                    $"campaign sequence has {missions.Count} missions, expected {CampaignSequence.MissionCount}"));
        }

        return missions;
    }

    /// <summary>Loads one campaign mission's complete retail reader scope.</summary>
    public static CampaignMissionDocument Load(
        IGameFileSource game,
        CampaignMission mission)
    {
        string path = ArchivePath(mission);
        return new CampaignMissionDocument(
            mission,
            ZrdrSet.Load(game, path),
            game.Location);
    }

    /// <summary>Builds a document over an already decoded complete scope.</summary>
    public static CampaignMissionDocument FromScope(
        CampaignMission mission,
        ZrdrSet scope) =>
        new(mission, scope, sourceLocation: null);

    /// <summary>The retail path of one campaign mission's reader scope.</summary>
    public static string ArchivePath(CampaignMission mission) =>
        $"ZBD/{mission.ChapterFolder}/{mission.MissionFolder}/zrdr.zbd";

    /// <summary>Finds a reader by its archive or extracted-JSON spelling. Returns -1 when it is
    /// absent.</summary>
    public int FindFileIndex(string name)
    {
        ZrdrFile? found =
            ZrdrZbd.Find(_files, name);
        if (found == null)
        {
            return -1;
        }

        for (int i = 0; i < _files.Count; i++)
        {
            if (ReferenceEquals(_files[i], found))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns one value addressed by reader position and raw list indexes.</summary>
    public object? ValueAt(int fileIndex, IReadOnlyList<int> path)
    {
        ZrdrFile file = FileAt(fileIndex);
        object? value = file.Root;
        foreach (int index in path)
        {
            if (value is not List<object?> list)
            {
                throw new InvalidOperationException(
                    Invariant(
                        $"'{file.Key}' path enters a scalar before index {index}"));
            }

            if ((uint)index >= (uint)list.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(path),
                    Invariant(
                        $"'{file.Key}' path index {index} is outside a {list.Count}-item list"));
            }

            value = list[index];
        }

        return value;
    }

    /// <summary>
    /// Replaces an existing string or float leaf with the same type. Returns whether its value
    /// changed. Structural edits, number-to-string conversions and objective block renumbering
    /// are rejected; objective number swaps use the validated typed operation.
    /// </summary>
    public bool SetLeaf(int fileIndex, IReadOnlyList<int> path, object value)
    {
        if (path.Count == 0)
        {
            throw new ArgumentException("a leaf path cannot be empty", nameof(path));
        }

        ZrdrFile file = FileAt(fileIndex);
        List<object?> parent = file.Root;
        for (int depth = 0; depth < path.Count - 1; depth++)
        {
            int index = path[depth];
            if ((uint)index >= (uint)parent.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(path),
                    Invariant(
                        $"'{file.Key}' path index {index} is outside a {parent.Count}-item list"));
            }

            parent = parent[index] as List<object?>
                ?? throw new InvalidOperationException(
                    Invariant(
                        $"'{file.Key}' path enters a scalar at depth {depth}"));
        }

        int leafIndex = path[^1];
        if ((uint)leafIndex >= (uint)parent.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(path),
                Invariant(
                    $"'{file.Key}' leaf index {leafIndex} is outside a {parent.Count}-item list"));
        }

        object? old = parent[leafIndex];
        bool sameType = (old is string && value is string)
            || (old is float && value is float);
        if (!sameType)
        {
            throw new InvalidOperationException(
                $"'{file.Key}' leaf edits must preserve string or float type");
        }

        if (value is float number && !float.IsFinite(number))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "ZRDR JSON cannot represent a non-finite float");
        }

        bool changed = !SameScalar(old!, value);
        if (changed && fileIndex == FindFileIndex("objectives.json")
            && path.Count == 2 && path[0] == 0
            && old is string oldKey && value is string newKey
            && (CampaignMissionObjectives.IsNumberedBlockCandidate(oldKey)
                || CampaignMissionObjectives.IsNumberedBlockCandidate(newKey)))
        {
            throw new InvalidOperationException(
                "Objective block numbers cannot be edited in Raw; use a validated number swap.");
        }

        var edit = (fileIndex, ImmutableArray.CreateRange(path), value);
        if (_leafBatch != null)
        {
            _leafBatch.Add(edit);
            return changed;
        }

        return changed && CommitLeaves(new[] { edit }, "Edit raw value");
    }

    /// <summary>
    /// Writes the complete scope beneath <paramref name="modFolder"/> as the mission's
    /// <c>zrdr.zip</c>. The output may not be inside the retail source or project checkout.
    /// </summary>
    public string Export(string modFolder, string? projectRoot = null)
    {
        if (string.IsNullOrWhiteSpace(modFolder))
        {
            throw new ArgumentException("a modding folder is required", nameof(modFolder));
        }

        string root = Path.GetFullPath(modFolder);
        EnsureOutside(root, _sourceLocation, "retail source");
        EnsureOutside(root, projectRoot, "project checkout");

        string output = Path.Combine(
            root,
            ModdingFolder.OutputOf(
                    ArchivePath(Mission))
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        string missionDir = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException(
                $"mission output '{output}' has no directory");
        string unpacked = Path.ChangeExtension(output, null);
        if (Directory.Exists(unpacked))
        {
            throw new IOException(
                $"'{unpacked}' already owns this scope; remove it or choose another modding folder");
        }
        Directory.CreateDirectory(missionDir);
        string temporary = Path.Combine(
            missionDir,
            $".zrdr-{Guid.NewGuid():N}.tmp");

        try
        {
            WriteZip(temporary);
            VerifyZip(temporary);
            File.Move(temporary, output, overwrite: true);
            _savedStateIdentity = _stateIdentity;
            return output;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    internal static CampaignMissionDocument FromScope(
        CampaignMission mission,
        ZrdrSet scope,
        string sourceLocation) =>
        new(mission, scope, sourceLocation);

    internal static List<object?> Copy(List<object?> source)
    {
        var copy = new List<object?>(source.Count);
        foreach (object? value in source)
        {
            copy.Add(value is List<object?> nested ? Copy(nested) : value);
        }

        return copy;
    }

    internal bool BatchLeaves(string name, Func<bool> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (_leafBatch != null)
        {
            throw new InvalidOperationException("Nested campaign leaf edits are not supported.");
        }

        var pending = new List<(int FileIndex, ImmutableArray<int> Path, object Value)>();
        _leafBatch = pending;
        try
        {
            edit();
        }
        finally
        {
            _leafBatch = null;
        }

        return CommitLeaves(pending, name);
    }

    internal bool SetZeppelinLeaves(
        int fileIndex,
        long expectedRevision,
        IReadOnlyList<(IReadOnlyList<int> Path, object Value)> edits) =>
        SetTypedLeaves(fileIndex, expectedRevision, edits, "zeppelins.json", "zeppelin");

    internal bool SetGeneratorLeaves(
        int fileIndex,
        long expectedRevision,
        IReadOnlyList<(IReadOnlyList<int> Path, object Value)> edits) =>
        SetTypedLeaves(fileIndex, expectedRevision, edits, "egen.json", "generator");

    internal bool SetRosterLeaves(
        int fileIndex,
        long expectedRevision,
        IReadOnlyList<(IReadOnlyList<int> Path, object Value)> edits) =>
        SetTypedLeaves(fileIndex, expectedRevision, edits, "aiv.json", "roster");

    internal bool SetObjectiveLeaves(
        int fileIndex,
        long expectedRevision,
        IReadOnlyList<(IReadOnlyList<int> Path, object Value)> edits) =>
        SetTypedLeaves(fileIndex, expectedRevision, edits, "objectives.json", "objective");

    internal bool SetStartAnimationLeaves(
        int fileIndex,
        long expectedRevision,
        IReadOnlyList<(IReadOnlyList<int> Path, object Value)> edits) =>
        SetTypedLeaves(fileIndex, expectedRevision, edits, "startanims.json", "start-animation");

    internal bool ReorderStartAnimationEntries(
        int fileIndex,
        long expectedRevision,
        IReadOnlyList<CampaignStartAnimationEntry> entries,
        IReadOnlyList<int> order)
    {
        if (fileIndex != FindFileIndex("startanims.json")
            || expectedRevision != ChangeGeneration)
        {
            throw new InvalidOperationException("The start-animation binding is stale; select the entry again.");
        }

        ZrdrFile file = FileAt(fileIndex);
        if (entries.Count != order.Count
            || file.Root.Count == 0
            || file.Root[0] is not List<object?> outer)
        {
            throw new InvalidOperationException("The start-animation slots are no longer valid.");
        }

        var slots = new List<(List<object?> Parent, int Index, List<object?> Entry)>();
        var seenSlots = new HashSet<(List<object?> Parent, int Index)>();
        var seenOrder = new HashSet<int>();
        bool changed = false;
        for (int i = 0; i < entries.Count; i++)
        {
            CampaignStartAnimationEntry entry = entries[i];
            if (entry.EntryPath.Length != 3 || entry.EntryPath[0] != 0)
            {
                throw new InvalidOperationException("The start-animation slots are no longer valid.");
            }

            int group = entry.EntryPath[1];
            int index = entry.EntryPath[2];
            if (group <= 0 || group >= outer.Count
                || outer[group - 1] is not string key
                || !key.Equals("NEW_GAME_START", StringComparison.OrdinalIgnoreCase)
                || outer[group] is not List<object?> parent
                || (uint)index >= (uint)parent.Count
                || parent[index] is not List<object?> { Count: > 0 } raw
                || raw[0] is not string
                || !ReferenceEquals(raw, entry.Raw)
                || !seenSlots.Add((parent, index)))
            {
                throw new InvalidOperationException("The start-animation slots are no longer valid.");
            }

            if ((uint)order[i] >= (uint)entries.Count || !seenOrder.Add(order[i]))
            {
                throw new ArgumentException("The start-animation order must be a permutation.", nameof(order));
            }

            slots.Add((parent, index, raw));
            changed |= order[i] != i;
        }

        if (!changed)
        {
            return false;
        }

        var changes = ImmutableArray.CreateBuilder<CampaignRawChange>(slots.Count);
        var snapshots = new CampaignRawValue[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            snapshots[i] = CampaignRawValue.Capture(slots[i].Entry);
        }

        for (int i = 0; i < slots.Count; i++)
        {
            changes.Add(new CampaignRawChange(
                fileIndex, entries[i].EntryPath, snapshots[i], snapshots[order[i]]));
        }

        return CommitEdit(
            "Move start animation",
            CampaignMissionEditKind.Permutation,
            changes.MoveToImmutable(),
            ImmutableArray.CreateRange(order));
    }

    internal int CreateDangerZoneReader(long expectedRevision)
    {
        if (expectedRevision != ChangeGeneration || FindFileIndex("dzones.json") >= 0)
        {
            throw new InvalidOperationException("The danger-zone binding is stale or already has a mission reader.");
        }

        int index = _files.Count;
        CommitReader("Create danger-zone reader", new ZrdrFile("dzones.zrd", null, new List<object?>()));
        return index;
    }

    internal bool ReplaceDangerZoneRoot(
        int fileIndex,
        long expectedRevision,
        List<object?> expected,
        List<object?> replacement)
    {
        ZrdrFile file = FileAt(fileIndex);
        if (fileIndex != FindFileIndex("dzones.json")
            || expectedRevision != ChangeGeneration
            || !ReferenceEquals(file.Root, expected))
        {
            throw new InvalidOperationException("The danger-zone binding is stale; select the zone again.");
        }

        if (SameList(expected, replacement))
        {
            return false;
        }

        return CommitEdit(
            "Edit danger zones",
            CampaignMissionEditKind.Slots,
            ImmutableArray.Create(new CampaignRawChange(
                fileIndex, ImmutableArray<int>.Empty,
                CampaignRawValue.Capture(expected), CampaignRawValue.Capture(replacement))));
    }

    internal bool ReplaceTargetEntry(
        int fileIndex,
        int rootIndex,
        List<object?> expected,
        List<object?> replacement,
        long revision)
    {
        ZrdrFile file = FileAt(fileIndex);
        if (fileIndex != FindFileIndex("targets.json")
            || revision != ChangeGeneration
            || (uint)rootIndex >= (uint)file.Root.Count
            || !ReferenceEquals(file.Root[rootIndex], expected))
        {
            throw new InvalidOperationException(
                "The target binding is stale; select the entry again.");
        }

        if (SameList(expected, replacement))
        {
            return false;
        }

        return CommitEdit(
            "Edit target",
            CampaignMissionEditKind.Slots,
            ImmutableArray.Create(new CampaignRawChange(
                fileIndex, ImmutableArray.Create(rootIndex),
                CampaignRawValue.Capture(expected), CampaignRawValue.Capture(replacement))));
    }

    internal int CopyChapterTargets(
        ZrdrSet chapter,
        string expectedKey,
        List<object?> expectedRoot)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        if (FindFileIndex("targets.json") >= 0)
        {
            throw new InvalidOperationException(
                "The mission already has a targets reader.");
        }

        ZrdrFile source = ZrdrZbd.Find(chapter.Files, "targets.json")
            ?? throw new InvalidOperationException(
                "The chapter has no targets reader to copy.");
        if (!chapter.Exists || source.Root.Count == 0)
        {
            throw new InvalidOperationException(
                "The chapter targets reader is unavailable or empty.");
        }

        if (!string.Equals(source.Key, expectedKey, StringComparison.Ordinal)
            || !SameList(source.Root, expectedRoot))
        {
            throw new InvalidOperationException(
                "The chapter targets changed; select the entry again.");
        }

        var copy = new ZrdrFile(
            source.Name,
            source.Rename,
            Copy(source.Root));
        int index = _files.Count;
        CommitReader("Copy chapter targets", copy);
        return index;
    }

    internal bool Replay(CampaignMissionEdit edit, bool forward)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (_publishingEdit || _abortedIdentities.Contains(edit.After.Identity)
            || !ApplyTransition(edit, forward))
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    internal bool IsAborted(CampaignMissionEdit edit) =>
        ReferenceEquals(edit.Document, this) && _abortedIdentities.Contains(edit.After.Identity);

    internal bool RejectUnpublished(IReadOnlyList<CampaignMissionEdit> edits)
    {
        if (edits.Count == 0)
        {
            return true;
        }

        foreach (CampaignMissionEdit edit in edits)
        {
            if (!ReferenceEquals(edit.Document, this))
            {
                return false;
            }
        }

        foreach (CampaignMissionEdit edit in edits)
        {
            _abortedIdentities.Add(edit.After.Identity);
        }

        if (_publishingEdit || _leafBatch != null)
        {
            return false;
        }

        // Check the whole suffix first so stale raw data cannot cause a partial rollback.
        CampaignMissionDocument check = HistoryCheckCopy();
        for (int i = edits.Count - 1; i >= 0; i--)
        {
            if (!check.ApplyTransition(edits[i] with { Document = check }, forward: false))
            {
                return false;
            }
        }

        for (int i = edits.Count - 1; i >= 0; i--)
        {
            if (!ApplyTransition(edits[i], forward: false))
            {
                throw new InvalidOperationException("The unpublished edit rollback no longer matches its document.");
            }
        }

        Changed?.Invoke();
        EditRolledBack?.Invoke();
        return true;
    }

    internal bool CanRejectThenReplay(
        IReadOnlyList<CampaignMissionEdit> edits,
        CampaignMissionEdit replay,
        bool forward)
    {
        if (edits.Count == 0 || _publishingEdit || _leafBatch != null
            || !ReferenceEquals(replay.Document, this) || IsAborted(replay))
        {
            return false;
        }

        CampaignMissionDocument check = HistoryCheckCopy();
        for (int i = edits.Count - 1; i >= 0; i--)
        {
            CampaignMissionEdit edit = edits[i];
            if (!ReferenceEquals(edit.Document, this) || IsAborted(edit)
                || edit.After.Identity == replay.After.Identity
                || !check.ApplyTransition(edit with { Document = check }, forward: false))
            {
                return false;
            }
        }

        return check.ApplyTransition(replay with { Document = check }, forward);
    }

    internal bool RejectThenReplay(
        IReadOnlyList<CampaignMissionEdit> edits,
        CampaignMissionEdit replay,
        bool forward)
    {
        if (!CanRejectThenReplay(edits, replay, forward))
        {
            return false;
        }

        foreach (CampaignMissionEdit edit in edits)
        {
            _abortedIdentities.Add(edit.After.Identity);
        }

        for (int i = edits.Count - 1; i >= 0; i--)
        {
            if (!ApplyTransition(edits[i], forward: false))
            {
                throw new InvalidOperationException("The unpublished edit rollback no longer matches its document.");
            }
        }

        if (!ApplyTransition(replay, forward))
        {
            throw new InvalidOperationException("The history replay no longer matches its document.");
        }

        Changed?.Invoke();
        EditRolledBack?.Invoke();
        return true;
    }

    private static void EnsureOutside(
        string outputRoot,
        string? forbiddenRoot,
        string label)
    {
        if (string.IsNullOrWhiteSpace(forbiddenRoot)
            || !Directory.Exists(forbiddenRoot))
        {
            return;
        }

        string forbidden = Path.GetFullPath(forbiddenRoot);
        string relative = Path.GetRelativePath(forbidden, outputRoot);
        bool outside = relative == ".."
            || relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative);
        if (!outside)
        {
            throw new InvalidOperationException(
                $"the modding folder cannot be inside the {label} '{forbidden}'");
        }
    }

    private static string Invariant(FormattableString value) =>
        FormattableString.Invariant(value);

    private bool SetTypedLeaves(
        int fileIndex,
        long expectedRevision,
        IReadOnlyList<(IReadOnlyList<int> Path, object Value)> edits,
        string reader,
        string subject)
    {
        if (fileIndex != FindFileIndex(reader)
            || expectedRevision != ChangeGeneration)
        {
            throw new InvalidOperationException(
                $"The {subject} binding is stale; select the record again.");
        }

        ZrdrFile file = FileAt(fileIndex);
        var pending = ImmutableArray.CreateBuilder<CampaignRawChange>();
        var seen = new HashSet<(List<object?> Parent, int Index)>();
        foreach ((IReadOnlyList<int> path, object value) in edits)
        {
            if (path.Count == 0)
            {
                throw new ArgumentException("A leaf path cannot be empty.", nameof(edits));
            }

            List<object?> parent = file.Root;
            for (int depth = 0; depth < path.Count - 1; depth++)
            {
                int index = path[depth];
                if ((uint)index >= (uint)parent.Count
                    || parent[index] is not List<object?> child)
                {
                    throw new InvalidOperationException($"The {subject} leaf path is no longer valid.");
                }

                parent = child;
            }

            int leaf = path[^1];
            if ((uint)leaf >= (uint)parent.Count
                || !seen.Add((parent, leaf)))
            {
                throw new InvalidOperationException($"The {subject} leaf path is invalid or duplicated.");
            }

            object? old = parent[leaf];
            if (!((old is string && value is string)
                || (old is float && value is float)))
            {
                throw new InvalidOperationException($"{subject} edits must preserve leaf types.");
            }

            if (value is float number && !float.IsFinite(number))
            {
                throw new ArgumentOutOfRangeException(nameof(edits), "Values must be finite.");
            }

            if (!SameScalar(old!, value))
            {
                pending.Add(new CampaignRawChange(
                    fileIndex, ImmutableArray.CreateRange(path),
                    CampaignRawValue.Capture(old), CampaignRawValue.Capture(value)));
            }
        }

        if (pending.Count == 0)
        {
            return false;
        }

        return CommitEdit($"Edit {subject}", CampaignMissionEditKind.Slots, pending.ToImmutable());
    }

    private CampaignMissionDocument HistoryCheckCopy() =>
        new(Mission, new ZrdrSet("unpublished edits", _files), _sourceLocation)
        {
            Revision = this.Revision,
            ChangeGeneration = this.ChangeGeneration,
            _stateIdentity = this._stateIdentity,
        };

    private bool CommitLeaves(
        IReadOnlyList<(int FileIndex, ImmutableArray<int> Path, object Value)> edits,
        string name)
    {
        var changes = ImmutableArray.CreateBuilder<CampaignRawChange>();
        var seen = new HashSet<(List<object?> Parent, int Index)>();
        foreach (var (fileIndex, path, value) in edits)
        {
            if (!TryResolveSlot(fileIndex, path, out RawSlot slot)
                || slot.Parent == null
                || !seen.Add((slot.Parent, slot.Index))
                || !((slot.Value is string && value is string)
                    || (slot.Value is float && value is float)))
            {
                throw new InvalidOperationException("The campaign leaf batch is stale or invalid.");
            }

            if (value is float number && !float.IsFinite(number))
            {
                throw new ArgumentOutOfRangeException(nameof(edits), "Values must be finite.");
            }

            if (!SameScalar(slot.Value!, value))
            {
                changes.Add(new CampaignRawChange(
                    fileIndex, path, CampaignRawValue.Capture(slot.Value), CampaignRawValue.Capture(value)));
            }
        }

        return changes.Count != 0
            && CommitEdit(name, CampaignMissionEditKind.Slots, changes.ToImmutable());
    }

    private void CommitReader(string name, ZrdrFile file)
    {
        CampaignReaderSnapshot reader = CampaignReaderSnapshot.Capture(file);
        var change = new CampaignRawChange(
            _files.Count, ImmutableArray<int>.Empty, CampaignRawValue.Missing, reader.Root);
        CommitEdit(
            name, CampaignMissionEditKind.AddedReader, ImmutableArray.Create(change),
            addedReader: reader);
    }

    private bool CommitEdit(
        string name,
        CampaignMissionEditKind kind,
        ImmutableArray<CampaignRawChange> changes,
        ImmutableArray<int> permutation = default,
        CampaignReaderSnapshot? addedReader = null)
    {
        if (_leafBatch != null || _publishingEdit)
        {
            throw new InvalidOperationException("A campaign edit is already being prepared.");
        }

        long identity = checked(_nextStateIdentity + 1);
        var before = new CampaignEditState(Revision, _stateIdentity, IsDirty);
        var after = new CampaignEditState(checked(Revision + 1), identity, true);
        var edit = new CampaignMissionEdit(
            this, name, kind, before, after, changes, permutation, addedReader);
        if (!ApplyTransition(edit, forward: true))
        {
            throw new InvalidOperationException("The campaign edit no longer matches its document.");
        }

        _nextStateIdentity = identity;
        _publishingEdit = true;
        try
        {
            EditCommitted?.Invoke(edit);
        }
        catch
        {
            _abortedIdentities.Add(identity);
            ApplyTransition(edit, forward: false);
            EditRolledBack?.Invoke();
            throw;
        }
        finally
        {
            _publishingEdit = false;
        }

        Changed?.Invoke();
        return true;
    }

    private bool ApplyTransition(CampaignMissionEdit edit, bool forward)
    {
        CampaignEditState expected = forward ? edit.Before : edit.After;
        CampaignEditState destination = forward ? edit.After : edit.Before;
        if (!ReferenceEquals(edit.Document, this)
            || _leafBatch != null
            || _stateIdentity != expected.Identity
            || Revision != expected.Revision)
        {
            return false;
        }

        long generation = checked(ChangeGeneration + 1);
        if (edit.Kind == CampaignMissionEditKind.AddedReader)
        {
            if (!ApplyReaderTransition(edit, forward))
            {
                return false;
            }
        }
        else if (!ApplySlotTransition(edit, forward))
        {
            return false;
        }

        _stateIdentity = destination.Identity;
        Revision = destination.Revision;
        ChangeGeneration = generation;
        return true;
    }

    private bool ApplyReaderTransition(CampaignMissionEdit edit, bool forward)
    {
        if (edit.Changes.Length != 1 || edit.AddedReader is not { } reader)
        {
            return false;
        }

        int index = edit.Changes[0].FileIndex;
        if (forward)
        {
            if (index != _files.Count)
            {
                return false;
            }

            _files.Add(reader.Restore());
            return true;
        }

        if (index != _files.Count - 1 || !reader.Matches(_files[index]))
        {
            return false;
        }

        _files.RemoveAt(index);
        return true;
    }

    private bool ApplySlotTransition(CampaignMissionEdit edit, bool forward)
    {
        var slots = new List<RawSlot>(edit.Changes.Length);
        var seen = new HashSet<(int FileIndex, List<object?>? Parent, int Index)>();
        foreach (CampaignRawChange change in edit.Changes)
        {
            CampaignRawValue expected = forward ? change.Before : change.After;
            if (!TryResolveSlot(change.FileIndex, change.Path, out RawSlot slot)
                || !seen.Add((change.FileIndex, slot.Parent, slot.Index))
                || !expected.Matches(slot.Value))
            {
                return false;
            }

            slots.Add(slot);
        }

        var values = new object?[slots.Count];
        if (edit.Kind == CampaignMissionEditKind.Permutation)
        {
            if (edit.Permutation.Length != slots.Count)
            {
                return false;
            }

            var inverse = new int[slots.Count];
            var occupied = new bool[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                int source = edit.Permutation[i];
                if ((uint)source >= (uint)slots.Count || occupied[source] || slots[i].Parent == null)
                {
                    return false;
                }

                occupied[source] = true;
                inverse[source] = i;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                values[i] = slots[forward ? edit.Permutation[i] : inverse[i]].Value;
                CampaignRawValue target = forward ? edit.Changes[i].After : edit.Changes[i].Before;
                if (!target.Matches(values[i]))
                {
                    return false;
                }
            }
        }
        else if (edit.Kind == CampaignMissionEditKind.Slots)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                CampaignRawValue target = forward ? edit.Changes[i].After : edit.Changes[i].Before;
                values[i] = target.Restore();
                if (slots[i].Parent == null && values[i] is not List<object?>)
                {
                    return false;
                }
            }
        }
        else
        {
            return false;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            RawSlot slot = slots[i];
            if (slot.Parent == null)
            {
                ZrdrFile file = _files[slot.FileIndex];
                _files[slot.FileIndex] = file with { Root = (List<object?>)values[i]! };
            }
            else
            {
                slot.Parent[slot.Index] = values[i];
            }
        }

        return true;
    }

    private bool TryResolveSlot(int fileIndex, IReadOnlyList<int> path, out RawSlot slot)
    {
        slot = default;
        if ((uint)fileIndex >= (uint)_files.Count)
        {
            return false;
        }

        List<object?> root = _files[fileIndex].Root;
        if (path.Count == 0)
        {
            slot = new RawSlot(fileIndex, null, -1, root);
            return true;
        }

        List<object?> parent = root;
        for (int i = 0; i < path.Count - 1; i++)
        {
            int index = path[i];
            if ((uint)index >= (uint)parent.Count || parent[index] is not List<object?> child)
            {
                return false;
            }

            parent = child;
        }

        int leaf = path[^1];
        if ((uint)leaf >= (uint)parent.Count)
        {
            return false;
        }

        slot = new RawSlot(fileIndex, parent, leaf, parent[leaf]);
        return true;
    }

    private void WriteZip(string path)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: false);

        foreach (var file in _files)
        {
            string entryName = file.Key + ".json";
            if (file.Key.Length == 0
                || file.Key.Contains('/')
                || file.Key.Contains('\\')
                || !names.Add(entryName))
            {
                throw new InvalidDataException(
                    $"reader name '{file.Key}' cannot be written as a unique ZIP entry");
            }

            var entry = archive.CreateEntry(
                entryName,
                CompressionLevel.Optimal);
            entry.LastWriteTime = ZipTimestamp;
            using var entryStream = entry.Open();
            using var json = new Utf8JsonWriter(
                entryStream,
                new JsonWriterOptions { Indented = true });
            if (file.Root.Count == 0)
            {
                json.WriteNullValue();
            }
            else
            {
                WriteList(json, file.Root, file.Key);
            }
        }
    }

    private void VerifyZip(string path)
    {
        ZrdrSet reopened = ZrdrSet.FromExtracted(path);
        if (!reopened.Exists || reopened.Files.Count != _files.Count)
        {
            throw new InvalidDataException(
                Invariant(
                    $"export reopened with {reopened.Files.Count} readers, expected {_files.Count}"));
        }

        for (int i = 0; i < _files.Count; i++)
        {
            ZrdrFile expected = _files[i];
            ZrdrFile actual = reopened.Files[i];
            if (!string.Equals(expected.Key, actual.Key, StringComparison.Ordinal)
                || !SameList(expected.Root, actual.Root))
            {
                throw new InvalidDataException(
                    Invariant(
                        $"export round-trip changed reader {i} '{expected.Key}'"));
            }
        }
    }

    private void WriteList(
        Utf8JsonWriter json,
        List<object?> list,
        string reader)
    {
        json.WriteStartArray();
        foreach (object? value in list)
        {
            switch (value)
            {
                case null:
                    json.WriteNullValue();
                    break;
                case string text:
                    json.WriteStringValue(text);
                    break;
                case float number when float.IsFinite(number):
                    json.WriteNumberValue(number);
                    break;
                case float:
                    throw new InvalidDataException(
                        $"reader '{reader}' contains a non-finite float");
                case List<object?> nested:
                    WriteList(json, nested, reader);
                    break;
                default:
                    throw new InvalidDataException(
                        $"reader '{reader}' contains unsupported value type '{value.GetType().Name}'");
            }
        }

        json.WriteEndArray();
    }

    private bool SameList(
        List<object?> expected,
        List<object?> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            object? a = expected[i];
            object? b = actual[i];
            bool same = (a, b) switch
            {
                (null, null) => true,
                (string left, string right) =>
                    string.Equals(left, right, StringComparison.Ordinal),
                (float left, float right) =>
                    BitConverter.SingleToUInt32Bits(left)
                    == BitConverter.SingleToUInt32Bits(right),
                (bool left, bool right) => left == right,
                (List<object?> left, List<object?> right) =>
                    SameList(left, right),
                _ => false,
            };
            if (!same)
            {
                return false;
            }
        }

        return true;
    }

    private bool SameScalar(object left, object right) =>
        (left, right) switch
        {
            (string a, string b) =>
                string.Equals(a, b, StringComparison.Ordinal),
            (float a, float b) =>
                BitConverter.SingleToUInt32Bits(a)
                == BitConverter.SingleToUInt32Bits(b),
            _ => false,
        };

    private ZrdrFile FileAt(int index) =>
        (uint)index < (uint)_files.Count
            ? _files[index]
            : throw new ArgumentOutOfRangeException(
                nameof(index),
                Invariant(
                    $"reader index {index} is outside the {_files.Count}-reader scope"));

    private readonly record struct RawSlot(int FileIndex, List<object?>? Parent, int Index, object? Value);
}

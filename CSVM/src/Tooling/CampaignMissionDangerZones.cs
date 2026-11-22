using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Session.Campaign;
using CSVM.Session.Objectives;
using Godot;

namespace CSVM.Tooling;

internal enum CampaignDangerOverrideKind
{
    Disable,
    NoSnapshot,
    ObjectiveNumber,
}

internal readonly record struct CampaignDangerZoneState(
    bool Enabled,
    bool SnapshotAllowed,
    float? ObjectiveSlot);

internal sealed record CampaignDangerOccurrence(
    CampaignDangerOverrideKind Kind,
    string Name,
    float? Number,
    ImmutableArray<int> NamePath,
    ImmutableArray<int> NumberPath,
    bool Selected,
    bool Effective);

internal sealed record CampaignDangerZoneEntry(
    string Name,
    CampaignDangerZoneState State,
    ImmutableArray<int> ObjectiveBlocks,
    ImmutableArray<int> SlotPath,
    bool? HasGates,
    CampaignDangerRibbon? Ribbon);

internal sealed record CampaignDangerRibbon(
    string Name,
    int SourceIndex,
    int Difficulty,
    int Lanes,
    ImmutableArray<Vector3> Points);

/// <summary>Read-only chapter ribbon samples and gate verdicts for mission authoring.</summary>
internal sealed class CampaignDangerZoneWorld
{
    private readonly GameZ? _world;
    private readonly ImmutableDictionary<int, CampaignDangerRibbon> _ribbons;

    private CampaignDangerZoneWorld(
        GameZ? world,
        ImmutableArray<string> names,
        ImmutableDictionary<int, CampaignDangerRibbon> ribbons,
        string problem)
    {
        _world = world;
        Names = names;
        _ribbons = ribbons;
        Problem = problem;
    }

    internal ImmutableArray<string> Names { get; }

    internal string Problem { get; }

    internal static CampaignDangerZoneWorld Read(CampaignDependency<GameZ?> dependency)
    {
        GameZ? world = dependency.Availability.State == CampaignDependencyState.Ready
            ? dependency.Value
            : null;
        var names = ImmutableArray.CreateBuilder<string>();
        var ribbons = ImmutableDictionary.CreateBuilder<int, CampaignDangerRibbon>();
        if (world == null)
        {
            return new(null, names.ToImmutable(), ribbons.ToImmutable(), "The chapter world is unavailable.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int sourceIndex = 0; sourceIndex < world.Nodes.Count; sourceIndex++)
        {
            GameZNode node = world.Nodes[sourceIndex];
            if (TryRibbonIndex(node.Name, out _)
                && seen.Add(node.Name))
            {
                names.Add(node.Name);
            }
        }

        try
        {
            DangerZoneRibbons? loaded = DangerZoneRibbons.Load(world);
            if (loaded != null)
            {
                var sources = new Dictionary<int, int>();
                for (int sourceIndex = 0; sourceIndex < world.Nodes.Count; sourceIndex++)
                {
                    GameZNode node = world.Nodes[sourceIndex];
                    if (!DangerZoneRibbons.TryIndexOf(node.Name, out int index)
                        || node.MeshIndex < 0 || node.MeshIndex >= world.Meshes.Count)
                    {
                        continue;
                    }

                    GameZMesh mesh = world.Meshes[node.MeshIndex];
                    GameZPolygon? route = DangerZoneRibbons.RoutePolygon(mesh);
                    if (route == null)
                    {
                        continue;
                    }

                    int valid = 0;
                    foreach (int vertex in route.VertexIndices)
                    {
                        if ((uint)vertex < (uint)mesh.Vertices.Count)
                        {
                            valid++;
                        }
                    }

                    if (valid >= 2)
                    {
                        sources[index] = sourceIndex;
                    }
                }

                foreach (DangerZoneRibbon ribbon in loaded.All)
                {
                    var points = ImmutableArray.CreateBuilder<Vector3>();
                    for (int segment = 0; segment < ribbon.Segments.Count; segment++)
                    {
                        if (segment == 0)
                        {
                            points.Add(ribbon.PointAt(segment, 0f));
                        }

                        for (int sample = 1; sample <= 8; sample++)
                        {
                            points.Add(ribbon.PointAt(
                                segment, ribbon.Segments[segment].Length * sample / 8f));
                        }
                    }

                    ribbons[ribbon.Index] = new CampaignDangerRibbon(
                        ribbon.Name, sources[ribbon.Index], ribbon.Difficulty, ribbon.Lanes.Count, points.ToImmutable());
                }
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or IndexOutOfRangeException)
        {
            return new(world, names.ToImmutable(), ribbons.ToImmutable(),
                $"The chapter ribbon loader could not build its geometry: {error.Message}");
        }

        return new(world, names.ToImmutable(), ribbons.ToImmutable(), "");
    }

    internal bool? HasGates(string name)
    {
        if (_world == null)
        {
            return null;
        }

        try
        {
            return CampaignDangerZones.HasGateGeometry(_world, name);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or IndexOutOfRangeException)
        {
            return null;
        }
    }

    internal CampaignDangerRibbon? Ribbon(string name) =>
        TryRibbonIndex(name, out int index)
            && _ribbons.TryGetValue(index, out CampaignDangerRibbon? ribbon)
                ? ribbon
                : null;

    private static bool TryRibbonIndex(string name, out int index)
    {
        const string prefix = "dzpath";
        index = -1;
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(
                name.AsSpan(prefix.Length),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out index);
    }
}

/// <summary>Mission-only danger-zone overrides, with exact reader paths and atomic row edits.</summary>
internal sealed class CampaignMissionDangerZones
{
    private readonly CampaignMissionDocument _document;
    private readonly List<object?>? _root;
    private readonly long _revision;

    private CampaignMissionDangerZones(
        CampaignMissionDocument document,
        int fileIndex,
        List<object?>? root,
        ImmutableArray<CampaignDangerZoneEntry> entries)
    {
        _document = document;
        _revision = document.ChangeGeneration;
        FileIndex = fileIndex;
        _root = root;
        Entries = entries;
    }

    internal int FileIndex { get; }

    internal bool IsCurrent => _revision == _document.ChangeGeneration;

    internal bool CanCreate => FileIndex < 0 && IsCurrent;

    internal bool CanEdit => FileIndex >= 0 && IsCurrent;

    internal ImmutableArray<CampaignDangerZoneEntry> Entries { get; }

    internal static CampaignMissionDangerZones Bind(
        CampaignMissionDocument document,
        CampaignDangerZoneWorld world)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(world);
        int fileIndex = document.FindFileIndex("dzones.json");
        List<object?>? root = fileIndex >= 0 ? document.Files[fileIndex].Root : null;
        ImmutableArray<CampaignDangerOccurrence> occurrences = root == null
            ? ImmutableArray<CampaignDangerOccurrence>.Empty
            : Scan(root);
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var noSnapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slots = new Dictionary<string, CampaignDangerOccurrence>(StringComparer.OrdinalIgnoreCase);
        var objectiveBlocks = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        void Include(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
            {
                names.Add(name);
            }
        }

        foreach (string name in world.Names)
        {
            Include(name);
        }

        ObjectiveScript script = ObjectiveScript.Load(new ZrdrSet("mission", document.Files));
        foreach (ObjectiveDef objective in script.Objectives)
        {
            foreach (string name in objective.DangerZones)
            {
                Include(name);
                if (!objectiveBlocks.TryGetValue(name, out List<int>? blocks))
                {
                    blocks = new List<int>();
                    objectiveBlocks.Add(name, blocks);
                }

                if (!blocks.Contains(objective.Number))
                {
                    blocks.Add(objective.Number);
                }
            }
        }

        foreach (CampaignDangerOccurrence occurrence in occurrences)
        {
            if (!occurrence.Effective)
            {
                continue;
            }

            Include(occurrence.Name);
            switch (occurrence.Kind)
            {
                case CampaignDangerOverrideKind.Disable:
                    disabled.Add(occurrence.Name);
                    break;
                case CampaignDangerOverrideKind.NoSnapshot:
                    noSnapshot.Add(occurrence.Name);
                    break;
                case CampaignDangerOverrideKind.ObjectiveNumber:
                    slots[occurrence.Name] = occurrence;
                    break;
            }
        }

        var entries = ImmutableArray.CreateBuilder<CampaignDangerZoneEntry>();
        foreach (string name in names)
        {
            slots.TryGetValue(name, out CampaignDangerOccurrence? slot);
            objectiveBlocks.TryGetValue(name, out List<int>? blocks);
            entries.Add(new CampaignDangerZoneEntry(
                name,
                new CampaignDangerZoneState(
                    !disabled.Contains(name), !noSnapshot.Contains(name), slot?.Number),
                blocks == null ? ImmutableArray<int>.Empty : ImmutableArray.CreateRange(blocks),
                slot?.NumberPath ?? ImmutableArray<int>.Empty,
                world.HasGates(name),
                world.Ribbon(name)));
        }

        return new CampaignMissionDangerZones(document, fileIndex, root, entries.ToImmutable());
    }

    internal static float? ParseSlot(string text, float? current)
    {
        ArgumentNullException.ThrowIfNull(text);
        text = text.Trim();
        if (current is float existing
            && string.Equals(text, existing.ToString("R", CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return existing;
        }

        if (text.Length == 0)
        {
            return null;
        }

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            || !float.IsFinite(value))
        {
            throw new FormatException("The slot must be a finite invariant-culture number.");
        }

        if (!SameNumber(current, value)
            && (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double exact)
                || exact != value))
        {
            throw new FormatException("The slot cannot be represented exactly as a reader float.");
        }

        return value;
    }

    internal static ImmutableArray<CampaignDangerOccurrence> Scan(List<object?> root)
    {
        var found = ImmutableArray.CreateBuilder<CampaignDangerOccurrence>();
        for (int i = 0; i < root.Count;)
        {
            if (root[i] is not string key)
            {
                i++;
                continue;
            }

            bool hasList = i + 1 < root.Count && root[i + 1] is List<object?>;
            int valueIndex = hasList ? i + 1 : -1;
            if (hasList && root[valueIndex] is List<object?> values
                && Kind(key) is CampaignDangerOverrideKind kind)
            {
                bool selected = CampaignRawList.LastListValueIndex(root, key) == valueIndex;
                for (int j = 0; j < values.Count; j++)
                {
                    if (kind != CampaignDangerOverrideKind.ObjectiveNumber
                        && values[j] is string name)
                    {
                        found.Add(new CampaignDangerOccurrence(
                            kind, name, null, ImmutableArray.Create(valueIndex, j),
                            ImmutableArray<int>.Empty, selected, selected));
                    }
                    else if (kind == CampaignDangerOverrideKind.ObjectiveNumber
                        && values[j] is List<object?> { Count: >= 2 } pair
                        && pair[0] is string zone && pair[1] is float number)
                    {
                        bool effective = selected && IsLastSlot(values, j, zone);
                        found.Add(new CampaignDangerOccurrence(
                            kind, zone, number, ImmutableArray.Create(valueIndex, j, 0),
                            ImmutableArray.Create(valueIndex, j, 1), selected, effective));
                    }
                }
            }

            i += hasList ? 2 : 1;
        }

        return found.ToImmutable();
    }

    internal int CreateMissionReader()
    {
        if (!CanCreate)
        {
            throw new InvalidOperationException("Only a current mission without danger-zone overrides can create a reader.");
        }

        return _document.CreateDangerZoneReader(_revision);
    }

    internal bool Apply(CampaignDangerZoneEntry entry, CampaignDangerZoneState desired)
    {
        if (!CanEdit || _root == null)
        {
            throw new InvalidOperationException("Create a mission-local danger-zone reader before editing overrides.");
        }

        bool belongs = false;
        foreach (CampaignDangerZoneEntry candidate in Entries)
        {
            belongs |= ReferenceEquals(entry, candidate);
        }

        if (!belongs)
        {
            throw new ArgumentException("The selected danger zone does not belong to this binding.", nameof(entry));
        }

        CampaignDangerZoneState current = entry.State;
        bool slotChanged = !SameNumber(current.ObjectiveSlot, desired.ObjectiveSlot);
        if (slotChanged)
        {
            if (desired.ObjectiveSlot is not float number)
            {
                throw new ArgumentException("Use -1 to disable an existing objective slot without discarding its raw pair.");
            }

            if (!float.IsFinite(number) || number != MathF.Truncate(number)
                || number < -1f || number >= 2147483648f)
            {
                throw new ArgumentException("The objective slot must be -1 or a nonnegative 32-bit integer.");
            }
        }

        if (!slotChanged
            && current.Enabled == desired.Enabled
            && current.SnapshotAllowed == desired.SnapshotAllowed)
        {
            return false;
        }

        List<object?> replacement = CampaignMissionDocument.Copy(_root);
        SetMembership(replacement, "disable", entry.Name, !desired.Enabled);
        SetMembership(replacement, "nosnapshot", entry.Name, !desired.SnapshotAllowed);
        if (slotChanged)
        {
            SetSlot(replacement, entry, desired.ObjectiveSlot!.Value);
        }

        return _document.ReplaceDangerZoneRoot(FileIndex, _revision, _root, replacement);
    }

    private static CampaignDangerOverrideKind? Kind(string key)
    {
        if (key.Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            return CampaignDangerOverrideKind.Disable;
        }

        if (key.Equals("nosnapshot", StringComparison.OrdinalIgnoreCase))
        {
            return CampaignDangerOverrideKind.NoSnapshot;
        }

        return key.Equals("objective_numbers", StringComparison.OrdinalIgnoreCase)
            ? CampaignDangerOverrideKind.ObjectiveNumber
            : null;
    }

    private static bool IsLastSlot(List<object?> values, int index, string name)
    {
        for (int i = index + 1; i < values.Count; i++)
        {
            if (values[i] is List<object?> { Count: >= 2 } pair
                && pair[0] is string later && pair[1] is float
                && name.Equals(later, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static void SetMembership(List<object?> root, string key, string name, bool contains)
    {
        int index = CampaignRawList.LastListValueIndex(root, key);
        if (index < 0)
        {
            if (contains)
            {
                root.Add(key);
                root.Add(new List<object?> { name });
            }

            return;
        }

        var values = (List<object?>)root[index]!;
        bool existing = false;
        foreach (object? item in values)
        {
            existing |= item is string text && text.Equals(name, StringComparison.OrdinalIgnoreCase);
        }

        if (contains && !existing)
        {
            values.Add(name);
        }
        else if (!contains && existing)
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                if (values[i] is string text && text.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    values.RemoveAt(i);
                }
            }
        }
    }

    private static void SetSlot(List<object?> root, CampaignDangerZoneEntry entry, float number)
    {
        if (!entry.SlotPath.IsDefaultOrEmpty)
        {
            var values = (List<object?>)root[entry.SlotPath[0]]!;
            var pair = (List<object?>)values[entry.SlotPath[1]]!;
            pair[entry.SlotPath[2]] = number;
            return;
        }

        int index = CampaignRawList.LastListValueIndex(root, "objective_numbers");
        if (index < 0)
        {
            root.Add("objective_numbers");
            root.Add(new List<object?> { new List<object?> { entry.Name, number } });
        }
        else
        {
            ((List<object?>)root[index]!).Add(new List<object?> { entry.Name, number });
        }
    }

    private static bool SameNumber(float? left, float? right) =>
        left.HasValue == right.HasValue
        && (!left.HasValue || BitConverter.SingleToUInt32Bits(left.Value)
            == BitConverter.SingleToUInt32Bits(right!.Value));
}

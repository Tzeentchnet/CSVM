using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CSVM.Mech3;
using CSVM.Session.Objectives;
using CSVM.Session.Roster;
using Godot;

namespace CSVM.Tooling;

[Flags]
internal enum CampaignPreviewLayers
{
    None = 0,
    Player = 1,
    Roster = 2,
    Targets = 4,
    Generators = 8,
    Zeppelins = 16,
    Danger = 32,
    All = Player | Roster | Targets | Generators | Zeppelins | Danger,
}

internal interface ICampaignPreviewWorld
{
    bool TryPose(string name, out Transform3D pose);

    bool TryTarget(IReadOnlyList<string> path, out Vector3 position);

    bool TryGenerator(
        string host,
        string? origin,
        bool zeppelin,
        out Transform3D hostPose,
        out Vector3 offset,
        out string note);

    void PlaceZeppelins(IReadOnlyList<CampaignPreviewItem> zeppelins);
}

internal sealed record CampaignPreviewSource(
    ReferenceScope Scope,
    string Container,
    int? FileIndex,
    ImmutableArray<int> Path);

internal sealed record CampaignPreviewItem(
    CampaignPreviewLayers Layer,
    string Name,
    CampaignPreviewSource Source,
    CampaignPreviewSource? RelatedSource,
    Transform3D Pose,
    ImmutableArray<Vector3> Points,
    bool Enabled,
    string Note,
    ImmutableArray<CampaignPreviewSource> AdditionalSources = default);

internal sealed record CampaignPreviewSelection(
    CampaignPreviewLayers Layer,
    string Name,
    CampaignPreviewSource Source,
    CampaignPreviewSource? RelatedSource,
    ImmutableArray<CampaignPreviewSource> AdditionalSources);

internal static class CampaignPreviewTransforms
{
    internal static Transform3D Start(float x, float y, float z, float yawDeg) =>
        new(new Basis(Vector3.Up, Mathf.DegToRad(yawDeg)), new Vector3(x, y, z));

    internal static Transform3D Zeppelin(
        Vector3 position, float yawDeg, float pitchDeg) =>
        new(
            Basis.FromEuler(
                new Vector3(Mathf.DegToRad(pitchDeg), Mathf.DegToRad(yawDeg), 0f),
                EulerOrder.Yxz),
            position);

    internal static Transform3D Generator(
        Transform3D host, Vector3 offset, bool surface) =>
        new(
            host.Basis,
            (host * offset) + (surface ? Vector3.Up * AiGeneratorRuntime.SurfaceLaunchLift : Vector3.Zero));
}

/// <summary>Revision-bound, disposable mission overlay projections and exact source selections.</summary>
internal sealed class CampaignMissionPreview : IDisposable
{
    private static readonly string[] Readers =
    {
        "objectives.json", "aiv.json", "targets.json", "egen.json", "zeppelins.json", "dzones.json",
    };

    private readonly CampaignMissionDocument _document;
    private readonly ZrdrSet _chapter;
    private readonly bool _chapterAvailable;
    private readonly CampaignDangerZoneWorld _danger;
    private readonly ICampaignPreviewWorld _world;
    private readonly string _worldPath;
    private readonly Dictionary<string, ReaderSnapshot> _readers = new(StringComparer.Ordinal);
    private readonly Dictionary<CampaignPreviewLayers, IReadOnlyList<CampaignPreviewItem>> _items = new();
    private readonly Dictionary<CampaignPreviewLayers, string> _status = new();
    private CampaignPreviewLayers _dirty = CampaignPreviewLayers.All;
    private bool _disposed;

    internal CampaignMissionPreview(
        CampaignMissionDocument document,
        ZrdrSet chapter,
        bool chapterAvailable,
        CampaignDangerZoneWorld danger,
        ICampaignPreviewWorld world,
        string worldPath)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
        _chapterAvailable = chapterAvailable;
        _danger = danger ?? throw new ArgumentNullException(nameof(danger));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _worldPath = worldPath ?? throw new ArgumentNullException(nameof(worldPath));
        foreach (string reader in Readers)
        {
            _readers.Add(reader, Capture(reader));
        }

        _document.Changed += DocumentChanged;
    }

    internal CampaignPreviewSelection? Selection { get; private set; }

    internal CampaignPreviewLayers Dirty => _dirty;

    internal string LocationStatus =>
        "Named location records have no established CSVM point consumer; no location handles are inferred.";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _document.Changed -= DocumentChanged;
        _disposed = true;
        Selection = null;
        _items.Clear();
        _readers.Clear();
    }

    internal static bool SameSource(CampaignPreviewSource a, CampaignPreviewSource b) =>
        a.Scope == b.Scope && a.FileIndex == b.FileIndex
        && string.Equals(a.Container, b.Container, StringComparison.Ordinal)
        && a.Path.SequenceEqual(b.Path);

    internal IReadOnlyList<CampaignPreviewItem> Items(CampaignPreviewLayers layer) =>
        _items.TryGetValue(layer, out IReadOnlyList<CampaignPreviewItem>? items)
            ? items
            : Array.Empty<CampaignPreviewItem>();

    internal string Status(CampaignPreviewLayers layer) =>
        _status.GetValueOrDefault(layer, "");

    internal bool Select(CampaignPreviewItem item)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CampaignPreviewItem? current = Items(item.Layer).FirstOrDefault(candidate =>
            SameSource(candidate.Source, item.Source));
        if (current == null)
        {
            return false;
        }

        Selection = SelectionOf(current);
        return true;
    }

    internal CampaignPreviewLayers Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CampaignPreviewLayers requested = _dirty;
        _dirty = CampaignPreviewLayers.None;
        CampaignPreviewLayers changed = CampaignPreviewLayers.None;

        if (requested.HasFlag(CampaignPreviewLayers.Zeppelins))
        {
            var (items, status) = Zeppelins();
            if (Replace(CampaignPreviewLayers.Zeppelins, items, status))
            {
                _world.PlaceZeppelins(items);
                changed |= CampaignPreviewLayers.Zeppelins;
            }
        }

        if (requested.HasFlag(CampaignPreviewLayers.Player))
        {
            var (items, status) = Player();
            if (Replace(CampaignPreviewLayers.Player, items, status))
            {
                changed |= CampaignPreviewLayers.Player;
            }
        }

        if (requested.HasFlag(CampaignPreviewLayers.Roster))
        {
            var (items, status) = Roster();
            if (Replace(CampaignPreviewLayers.Roster, items, status))
            {
                changed |= CampaignPreviewLayers.Roster;
            }
        }

        if (requested.HasFlag(CampaignPreviewLayers.Targets))
        {
            var (items, status) = Targets();
            if (Replace(CampaignPreviewLayers.Targets, items, status))
            {
                changed |= CampaignPreviewLayers.Targets;
            }
        }

        if (requested.HasFlag(CampaignPreviewLayers.Generators))
        {
            var (items, status) = Generators();
            if (Replace(CampaignPreviewLayers.Generators, items, status))
            {
                changed |= CampaignPreviewLayers.Generators;
            }
        }

        if (requested.HasFlag(CampaignPreviewLayers.Danger))
        {
            var (items, status) = Danger();
            if (Replace(CampaignPreviewLayers.Danger, items, status))
            {
                changed |= CampaignPreviewLayers.Danger;
            }
        }

        if (Selection is { } selected)
        {
            CampaignPreviewItem? current = Items(selected.Layer).FirstOrDefault(item =>
                SameSource(item.Source, selected.Source));
            Selection = current == null ? null : SelectionOf(current);
        }

        return changed;
    }

    private static CampaignPreviewSelection SelectionOf(CampaignPreviewItem item) =>
        new(item.Layer, item.Name, item.Source, item.RelatedSource, item.AdditionalSources);

    private static CampaignPreviewItem Item(
        CampaignPreviewLayers layer,
        string name,
        CampaignPreviewSource source,
        Transform3D pose,
        string note) =>
        new(layer, name, source, null, pose, ImmutableArray<Vector3>.Empty, true, note);

    private static string Omitted(string problem, int count, string subject) =>
        count == 0 ? problem : string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{problem} {count} {subject} omitted.").Trim();

    private static bool SameItem(CampaignPreviewItem a, CampaignPreviewItem b) =>
        a.Layer == b.Layer && a.Name == b.Name && SameSource(a.Source, b.Source)
        && ((a.RelatedSource == null && b.RelatedSource == null)
            || (a.RelatedSource != null && b.RelatedSource != null
                && SameSource(a.RelatedSource, b.RelatedSource)))
        && a.Pose == b.Pose && a.Points.SequenceEqual(b.Points)
        && a.Enabled == b.Enabled && a.Note == b.Note
        && SameSources(a.AdditionalSources, b.AdditionalSources);

    private static bool SameSources(
        ImmutableArray<CampaignPreviewSource> a, ImmutableArray<CampaignPreviewSource> b)
    {
        if (a.IsDefaultOrEmpty || b.IsDefaultOrEmpty)
        {
            return a.IsDefaultOrEmpty && b.IsDefaultOrEmpty;
        }

        return a.Length == b.Length && a.Zip(b, SameSource).All(same => same);
    }

    private static bool SameReader(ReaderSnapshot a, ReaderSnapshot b) =>
        a.Index == b.Index && a.Name == b.Name && SameValue(a.Root, b.Root);

    private static bool SameValue(object? a, object? b)
    {
        if (a is List<object?> left && b is List<object?> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!SameValue(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return (a, b) switch
        {
            (float x, float y) => BitConverter.SingleToUInt32Bits(x) == BitConverter.SingleToUInt32Bits(y),
            (string x, string y) => string.Equals(x, y, StringComparison.Ordinal),
            (null, null) => true,
            _ => false,
        };
    }

    private static bool SamePathShape(
        List<object?>? before, List<object?>? after, ImmutableArray<int> path)
    {
        object? left = before;
        object? right = after;
        foreach (int index in path)
        {
            if (left is not List<object?> a || right is not List<object?> b
                || a.Count != b.Count || (uint)index >= (uint)a.Count)
            {
                return false;
            }

            left = a[index];
            right = b[index];
        }

        return (left is List<object?>) == (right is List<object?>);
    }

    private (IReadOnlyList<CampaignPreviewItem>, string) Player()
    {
        if (!CampaignPlayerInit.TryBind(_document, out CampaignPlayerInit? start, out string problem))
        {
            return (Array.Empty<CampaignPreviewItem>(), problem);
        }

        CampaignPlayerInitState state = start.State;
        var source = MissionSource(start.FileIndex, start.PositionPath);
        return (new[]
        {
            Item(CampaignPreviewLayers.Player, "player", source,
                CampaignPreviewTransforms.Start(state.X, state.Y, state.Z, state.YawDeg),
                "PLAYER_INIT position and yaw; pitch and roll are not applied by CSVM."),
        }, "");
    }

    private (IReadOnlyList<CampaignPreviewItem>, string) Roster()
    {
        if (!CampaignRosterStarts.TryBind(_document, out CampaignRosterStarts? roster, out string problem))
        {
            return (Array.Empty<CampaignPreviewItem>(), problem);
        }

        var items = new List<CampaignPreviewItem>();
        foreach (CampaignRosterStart entry in roster.Entries)
        {
            if (entry.IsPlayer || !entry.Enabled)
            {
                continue;
            }

            CampaignRosterStartState state = entry.State;
            Transform3D pose = CampaignPreviewTransforms.Start(state.X, state.Y, state.Z, state.YawDeg);
            string note = "Authored roster start; vehicle and net eligibility are not simulated.";
            if (_world.TryPose(entry.Name, out Transform3D worldPose))
            {
                pose = worldPose;
                note = "Matching built world node takes precedence; startup animation is not played.";
            }

            items.Add(Item(
                CampaignPreviewLayers.Roster,
                entry.Name,
                MissionSource(roster.FileIndex, ImmutableArray.Create(entry.RawIndex, 1, 1)),
                pose,
                note));
        }

        return (items, items.Count == 0 ? "No enabled non-player roster starts are available." : "");
    }

    private (IReadOnlyList<CampaignPreviewItem>, string) Zeppelins()
    {
        CampaignMissionZeppelins binding = CampaignMissionZeppelins.Bind(_document);
        var items = new List<CampaignPreviewItem>();
        int omitted = 0;
        foreach (CampaignZeppelinEntry entry in binding.Entries)
        {
            if (entry.Problem.Length > 0
                || _document.ValueAt(binding.FileIndex, new[] { 0, entry.RawIndex }) is not List<object?> record)
            {
                omitted++;
                continue;
            }

            ZrdrDict values = ZrdrDict.FromAlternating(record);
            string? name = values.Str("node");
            int positionIndex = CampaignRawList.LastListValueIndex(record, "position");
            if (name == null || positionIndex < 0
                || record[positionIndex] is not List<object?> position
                || !_world.TryPose(name, out _))
            {
                omitted++;
                continue;
            }

            float yaw = values.Float("yaw");
            float pitch = values.Float("pitch");
            if (!float.IsFinite(yaw) || !float.IsFinite(pitch))
            {
                omitted++;
                continue;
            }

            var pose = CampaignPreviewTransforms.Zeppelin(
                new Vector3((float)position[0]!, (float)position[1]!, (float)position[2]!),
                yaw, pitch);
            items.Add(Item(
                CampaignPreviewLayers.Zeppelins,
                name,
                MissionSource(binding.FileIndex, ImmutableArray.Create(0, entry.RawIndex, positionIndex)),
                pose,
                "Authored hull pose after setup; a startup animation can override it."));
        }

        return (items, Omitted(binding.Problem, omitted, "malformed or unresolved zeppelin records"));
    }

    private (IReadOnlyList<CampaignPreviewItem>, string) Generators()
    {
        CampaignMissionGenerators binding = CampaignMissionGenerators.Bind(_document);
        var items = new List<CampaignPreviewItem>();
        int omitted = 0;
        foreach (CampaignGeneratorEntry entry in binding.Entries)
        {
            if (entry.Problem.Length > 0
                || !_world.TryGenerator(entry.Host, entry.Origin, entry.IsZeppelin,
                    out Transform3D host, out Vector3 offset, out string note))
            {
                omitted++;
                continue;
            }

            CampaignGeneratorField? origin = entry.Sections
                .SelectMany(section => section.Rows)
                .SelectMany(row => row.Fields)
                .FirstOrDefault(field => field.Key == "origin");
            CampaignPreviewSource? related = origin == null
                ? null
                : MissionSource(binding.FileIndex, origin.Path);
            items.Add(new CampaignPreviewItem(
                CampaignPreviewLayers.Generators,
                entry.Host,
                MissionSource(binding.FileIndex, entry.HostPath),
                related,
                CampaignPreviewTransforms.Generator(host, offset, !entry.IsZeppelin),
                ImmutableArray<Vector3>.Empty,
                true,
                note));
        }

        return (items, Omitted(binding.Problem, omitted, "malformed or unresolved generator hosts/paths"));
    }

    private (IReadOnlyList<CampaignPreviewItem>, string) Targets()
    {
        CampaignMissionTargets binding = CampaignMissionTargets.Bind(_document, _chapter, _chapterAvailable);
        ObjectiveScript? script = null;
        CampaignMissionObjectives? objectives = null;
        string scriptProblem = "";
        try
        {
            script = ObjectiveScript.Load(new ZrdrSet("mission", _document.Files));
            objectives = CampaignMissionObjectives.Bind(_document);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or IndexOutOfRangeException or System.IO.InvalidDataException)
        {
            scriptProblem = $"Objective point lookup unavailable: {error.Message}";
        }

        var items = new List<CampaignPreviewItem>();
        int omitted = 0;
        foreach (CampaignTargetEntry entry in binding.Entries)
        {
            foreach (CampaignTargetNode node in entry.Nodes)
            {
                if (!node.Effective)
                {
                    continue;
                }

                string[] path = node.Key.Split('/');
                Vector3 position;
                CampaignPreviewSource? related = null;
                string note = "Built world site's mesh-bounds centre after mission setup.";
                if (ObjectiveSites.PointFor(script, node.Key) is { } point)
                {
                    if (!point.IsFinite())
                    {
                        omitted++;
                        continue;
                    }

                    position = point;
                    related = PointSource(script!, objectives, node.Key);
                    note = "Objective TRAVELERS point takes precedence over the world site.";
                }
                else if (!_world.TryTarget(path, out position))
                {
                    CampaignPreviewItem? actor = Actor(node.Key);
                    if (actor == null)
                    {
                        omitted++;
                        continue;
                    }

                    position = actor.Pose.Origin;
                    note = "Roster or player start approximation; the live actor is not staged.";
                }

                var source = TargetSource(binding, node.Path);
                CampaignTargetState state = entry.State;
                items.Add(new CampaignPreviewItem(
                    CampaignPreviewLayers.Targets,
                    node.Key,
                    source,
                    related,
                    new Transform3D(Basis.Identity, position),
                    ImmutableArray<Vector3>.Empty,
                    state.Objective || state.OtherTarget,
                    note));
            }
        }

        return (items, Omitted(
            string.Join(" ", new[] { binding.Problem, scriptProblem }.Where(text => text.Length > 0)),
            omitted,
            "unresolved target paths"));
    }

    private (IReadOnlyList<CampaignPreviewItem>, string) Danger()
    {
        try
        {
            CampaignMissionDangerZones binding = CampaignMissionDangerZones.Bind(_document, _danger);
            var items = new List<CampaignPreviewItem>();
            var seen = new HashSet<int>();
            int malformed = 0;
            ImmutableArray<CampaignDangerOccurrence> occurrences = binding.FileIndex < 0
                ? ImmutableArray<CampaignDangerOccurrence>.Empty
                : CampaignMissionDangerZones.Scan(_document.Files[binding.FileIndex].Root);
            foreach (CampaignDangerZoneEntry entry in binding.Entries)
            {
                CampaignDangerRibbon? ribbon = entry.Ribbon;
                if (ribbon == null || !entry.Name.Equals(ribbon.Name, StringComparison.OrdinalIgnoreCase)
                    || seen.Contains(ribbon.SourceIndex))
                {
                    continue;
                }

                if (ribbon.Points.Length < 2 || ribbon.Points.Any(point => !point.IsFinite()))
                {
                    malformed++;
                    continue;
                }

                seen.Add(ribbon.SourceIndex);
                var relatedSources = new List<CampaignPreviewSource>();
                if (binding.FileIndex >= 0)
                {
                    if (!entry.SlotPath.IsDefaultOrEmpty)
                    {
                        relatedSources.Add(MissionSource(binding.FileIndex, entry.SlotPath));
                    }

                    foreach (CampaignDangerOccurrence occurrence in occurrences)
                    {
                        if (occurrence.Effective
                            && occurrence.Kind != CampaignDangerOverrideKind.ObjectiveNumber
                            && occurrence.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            relatedSources.Add(MissionSource(binding.FileIndex, occurrence.NamePath));
                        }
                    }
                }

                items.Add(new CampaignPreviewItem(
                    CampaignPreviewLayers.Danger,
                    ribbon.Name,
                    new CampaignPreviewSource(
                        ReferenceScope.World, _worldPath, null, ImmutableArray.Create(ribbon.SourceIndex)),
                    relatedSources.Count == 0 ? null : relatedSources[0],
                    new Transform3D(Basis.Identity, ribbon.Points[0]),
                    ribbon.Points,
                    entry.State.Enabled,
                    "Chapter source ribbon before setup; its geometry is read-only.",
                    relatedSources.Skip(1).ToImmutableArray()));
            }

            string problem = _danger.Problem.Length > 0
                ? _danger.Problem
                : items.Count == 0 ? "No usable chapter ribbons are available." : "";
            return (items, Omitted(problem, malformed, "malformed ribbon geometries"));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or IndexOutOfRangeException or System.IO.InvalidDataException)
        {
            return (Array.Empty<CampaignPreviewItem>(), $"Danger-zone projection unavailable: {error.Message}");
        }
    }

    private CampaignPreviewItem? Actor(string name)
    {
        foreach (CampaignPreviewLayers layer in new[] { CampaignPreviewLayers.Player, CampaignPreviewLayers.Roster })
        {
            foreach (CampaignPreviewItem item in Items(layer))
            {
                if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
        }

        return null;
    }

    private CampaignPreviewSource? PointSource(
        ObjectiveScript script, CampaignMissionObjectives? binding, string target)
    {
        if (binding == null || binding.FileIndex < 0)
        {
            return null;
        }

        var single = new ObjectiveScript();
        foreach (ObjectiveDef objective in script.Objectives)
        {
            single.Objectives.Clear();
            single.Objectives.Add(objective);
            if (ObjectiveSites.PointFor(single, target) == null)
            {
                continue;
            }

            CampaignObjectiveEntry? entry = binding.Entries.FirstOrDefault(candidate =>
                candidate.Number == objective.Number);
            CampaignObjectiveDirective? directive = entry?.Directives.FirstOrDefault(candidate =>
                candidate.Selected && candidate.Key == "TRAVELERS");
            if (entry == null || directive == null)
            {
                return null;
            }

            var path = ImmutableArray.Create(0, entry.RawIndex + 1, directive.RawIndex + 1, 2);
            return _document.ValueAt(binding.FileIndex, path) is List<object?> { Count: >= 3 }
                ? MissionSource(binding.FileIndex, path)
                : null;
        }

        return null;
    }

    private CampaignPreviewSource MissionSource(int index, ImmutableArray<int> path) =>
        new(ReferenceScope.Mission, _document.Files[index].Key, index, path);

    private CampaignPreviewSource TargetSource(CampaignMissionTargets binding, ImmutableArray<int> path)
    {
        if (binding.Origin == CampaignTargetOrigin.Mission && binding.FileIndex is int index)
        {
            return MissionSource(index, path);
        }

        int chapterIndex = -1;
        for (int i = 0; i < _chapter.Files.Count; i++)
        {
            if (string.Equals(_chapter.Files[i].Key, binding.FileKey, StringComparison.Ordinal))
            {
                chapterIndex = i;
                break;
            }
        }

        return new CampaignPreviewSource(
            ReferenceScope.Chapter, binding.FileKey ?? "targets.zrd",
            chapterIndex >= 0 ? chapterIndex : null, path);
    }

    private bool Replace(
        CampaignPreviewLayers layer,
        IReadOnlyList<CampaignPreviewItem> items,
        string status)
    {
        _status[layer] = status;
        bool changed = !_items.TryGetValue(layer, out IReadOnlyList<CampaignPreviewItem>? previous)
            || previous.Count != items.Count;
        if (!changed)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!SameItem(previous![i], items[i]))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            _items[layer] = items;
        }

        return changed;
    }

    private void DocumentChanged()
    {
        foreach (string reader in Readers)
        {
            ReaderSnapshot before = _readers[reader];
            ReaderSnapshot after = Capture(reader);
            if (SameReader(before, after))
            {
                continue;
            }

            if (Selection is { Source.Scope: ReferenceScope.Mission } selected
                && selected.Source.FileIndex == before.Index
                && !SamePathShape(before.Root, after.Root, selected.Source.Path))
            {
                Selection = null;
            }

            _readers[reader] = after;
            _dirty |= reader switch
            {
                "objectives.json" => CampaignPreviewLayers.Player | CampaignPreviewLayers.Targets | CampaignPreviewLayers.Danger,
                "aiv.json" => CampaignPreviewLayers.Roster | CampaignPreviewLayers.Targets,
                "targets.json" => CampaignPreviewLayers.Targets,
                "egen.json" => CampaignPreviewLayers.Generators,
                "zeppelins.json" => CampaignPreviewLayers.Zeppelins | CampaignPreviewLayers.Generators
                    | CampaignPreviewLayers.Targets | CampaignPreviewLayers.Roster,
                "dzones.json" => CampaignPreviewLayers.Danger,
                _ => CampaignPreviewLayers.None,
            };
        }
    }

    private ReaderSnapshot Capture(string name)
    {
        int index = _document.FindFileIndex(name);
        return index < 0
            ? new ReaderSnapshot(index, null, null)
            : new ReaderSnapshot(index, _document.Files[index].Key, CampaignMissionDocument.Copy(_document.Files[index].Root));
    }

    private sealed record ReaderSnapshot(int Index, string? Name, List<object?>? Root);
}

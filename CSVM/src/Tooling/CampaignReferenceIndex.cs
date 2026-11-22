using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using CSVM.Flight.Weapons;
using CSVM.Mech3;
using CSVM.Mech3.Anim;
using CSVM.Session.Campaign;
using CSVM.Session.Objectives;

namespace CSVM.Tooling;

internal sealed class CampaignReferenceIndex
{
    private readonly CampaignMissionDependencyBundle _bundle;
    private readonly GameZ? _world;
    private readonly NameResolver<GameZNode>? _worldNames;
    private readonly VehicleDefs? _vehicles;
    private readonly IReadOnlyList<AiNet>? _nets;
    private readonly HashSet<string>? _messages;
    private readonly HashSet<string>? _sounds;
    private readonly HashSet<string>? _targets;
    private readonly HashSet<string> _rosterNodes;
    private readonly HashSet<string> _templateCandidates;
    private readonly HashSet<string> _templates;
    private readonly HashSet<string> _unverifiedTemplates;
    private readonly HashSet<string> _animations;
    private readonly HashSet<string> _readers;
    private readonly HashSet<string> _generators;
    private readonly HashSet<string> _objectiveDangerZones;
    private readonly HashSet<int> _objectives;
    private CampaignDangerZoneWorld? _dangerWorld;

    private CampaignReferenceIndex(
        ZrdrSet editableMission,
        CampaignMissionDependencyBundle bundle,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        _bundle = bundle;
        AnimationInspection = CampaignAnimationInspection.Read(editableMission, bundle);
        _world = Ready(bundle.World) ? bundle.World.Value : null;
        if (_world != null)
        {
            _worldNames = new NameResolver<GameZNode>();
            foreach (GameZNode node in _world.Nodes)
            {
                _worldNames.Add(node, node.Name, _world.ParentOf(node));
            }
        }

        AddDependencyIssues(bundle, issues);

        if (Ready(bundle.SharedReaders))
        {
            ZrdrSet shared = bundle.SharedReaders.Value;
            if (shared.HasFile("vehicle.json"))
            {
                try
                {
                    _vehicles = VehicleDefs.Load(shared);
                }
                catch (Exception error) when (IsReaderError(error))
                {
                    issues.Add(ParseIssue("vehicle-index", error));
                }
            }

            if (!shared.HasFile("sounds.json"))
            {
                _sounds = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                try
                {
                    var sounds = new HashSet<string>(
                        SoundDefs.Load(shared).Keys,
                        StringComparer.OrdinalIgnoreCase);
                    sounds.UnionWith(SoundDefs.LoadGroups(shared).Keys);
                    _sounds = sounds;
                }
                catch (Exception error) when (IsReaderError(error))
                {
                    issues.Add(ParseIssue("sound-index", error));
                }
            }
        }

        if (Ready(bundle.ChapterReaders))
        {
            try
            {
                _nets = AiNets.Load(bundle.ChapterReaders.Value);
            }
            catch (Exception error) when (IsReaderError(error))
            {
                issues.Add(ParseIssue("chapter-net-index", error));
            }
        }

        if (Ready(bundle.MessageTable))
        {
            _messages = new HashSet<string>(
                bundle.MessageTable.Value.Entries.Keys,
                StringComparer.OrdinalIgnoreCase);
        }

        if (editableMission.HasFile("targets.json")
            || Ready(bundle.ChapterReaders))
        {
            try
            {
                MissionTargets targets = MissionTargets.Load(
                    editableMission,
                    Ready(bundle.ChapterReaders)
                        ? bundle.ChapterReaders.Value
                        : ZrdrSet.None);
                _targets = new HashSet<string>(
                    targets.ByNode.Keys,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception error) when (IsReaderError(error))
            {
                issues.Add(ParseIssue("target-index", error));
            }
        }

        _rosterNodes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        _templateCandidates = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        _templates = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        _unverifiedTemplates = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        _animations = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        _readers = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        _generators = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        _objectiveDangerZones = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        _objectives = new HashSet<int>();
        IndexGenerators(editableMission);
        IndexGeneratorTemplates(editableMission, issues);
        foreach (ObjectiveDef objective in ObjectiveScript.Load(editableMission).Objectives)
        {
            _objectives.Add(objective.Number);
            _objectiveDangerZones.UnionWith(objective.DangerZones);
        }

        IndexScope(editableMission, missionScope: true);
        IndexScope(bundle.SharedReaders.Value, missionScope: false);
        IndexScope(bundle.ChapterReaders.Value, missionScope: false);
        IndexCompiled(bundle.ChapterAnimations.Value);
        IndexCompiled(bundle.MissionAnimations.Value);
    }

    internal CampaignAnimationInspection AnimationInspection { get; }

    internal static CampaignReferenceIndex Build(
        ZrdrSet editableMission,
        CampaignMissionDependencyBundle bundle,
        ImmutableArray<MissionReference> references,
        out ImmutableArray<ReferenceIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(editableMission);
        ArgumentNullException.ThrowIfNull(bundle);
        _ = references;

        var issueBuilder = ImmutableArray.CreateBuilder<ReferenceIssue>();
        var index =
            new CampaignReferenceIndex(editableMission, bundle, issueBuilder);
        issues = issueBuilder.ToImmutable();
        return index;
    }

    internal CheckedReference Resolve(MissionReference reference)
    {
        if (reference.Context.DefinitionKey == "suppressed")
        {
            return Checked(
                reference,
                ReferenceState.Suppressed,
                "A different duplicate is selected by the runtime.");
        }

        if (reference.Context.DefinitionKey == "danger-disabled")
        {
            return Checked(
                reference,
                ReferenceState.Suppressed,
                "The mission disables this objective's zone before player gates are armed, so that zone cannot complete.");
        }

        if (reference.Source.Context == CampaignReferenceExtractor.UnmatchedZeppelinGasbag)
        {
            return Unverified(reference, "No selected healthy zone currently consumes this gasbag animation.");
        }

        if (reference.Rule == ReferenceRule.Unverified)
        {
            return Unverified(
                reference,
                "The runtime consumer requires live staging or an undecoded payload.");
        }

        return reference.Rule switch
        {
            ReferenceRule.MessageKey => Lookup(
                reference, _messages, "message key"),
            ReferenceRule.VehicleBlock => ResolveVehicle(reference),
            ReferenceRule.TemplateName => ResolveTemplate(reference),
            ReferenceRule.NetId => ResolveNetId(reference),
            ReferenceRule.NetName => ResolveNetName(reference),
            ReferenceRule.NetStopPoint => ResolveNetStop(reference),
            ReferenceRule.EffectiveAnimation => ResolveAnimation(reference),
            ReferenceRule.StartAnimation => ResolveStartAnimation(reference),
            ReferenceRule.SoundDef or ReferenceRule.SoundCue
                or ReferenceRule.QueuedCue => Lookup(
                    reference, _sounds, "sound definition or group"),
            ReferenceRule.TargetSite => ResolveTarget(reference),
            ReferenceRule.TargetNode => ResolveTargetNode(reference),
            ReferenceRule.GlobalFirst
                or ReferenceRule.GlobalPattern => ResolveWorld(
                    reference, allowPattern: true),
            ReferenceRule.ScopedPath => ResolveWorldPath(reference),
            ReferenceRule.RuntimeActor => ResolveRuntimeActor(reference),
            ReferenceRule.GeneratorName => Lookup(
                reference, _generators, "generator"),
            ReferenceRule.GeneratorHost => ResolveGeneratorHost(reference),
            ReferenceRule.GeneratorChild => ResolveGeneratorChild(reference),
            ReferenceRule.GeneratorNet => ResolveGeneratorNet(reference),
            ReferenceRule.ObjectiveNumber => ResolveObjective(reference),
            ReferenceRule.DangerGeometry => ResolveDangerGeometry(reference),
            ReferenceRule.DangerOverridePath => ResolveDangerOverridePath(reference),
            ReferenceRule.DangerSlot => ResolveDangerSlot(reference),
            ReferenceRule.ListedReader => Lookup(
                reference, _readers, "reader path"),
            ReferenceRule.PickupNode => ResolvePickup(reference),
            ReferenceRule.ScriptedPath
                or ReferenceRule.AccentId
                or ReferenceRule.ManeuverName
                or ReferenceRule.WeatherZone
                or ReferenceRule.BriefingIndex
                or ReferenceRule.LocalSequence
                or ReferenceRule.ScriptSlot => Unverified(
                    reference,
                    "The static bundle cannot prove this live or asset-backed reference."),
            _ => Unverified(
                reference,
                "No field-specific resolver is registered."),
        };
    }

    private static bool Ready<T>(CampaignDependency<T> dependency) =>
        dependency.Availability.State == CampaignDependencyState.Ready;

    private static void AddDependencyIssues(
        CampaignMissionDependencyBundle bundle,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        foreach (CampaignDependencyAvailability availability
            in bundle.Availabilities)
        {
            if (availability.State == CampaignDependencyState.Ready
                || (availability.Optional
                    && availability.State
                        == CampaignDependencyState.Missing))
            {
                continue;
            }

            bool blocks = !availability.Optional
                && availability.State
                    is CampaignDependencyState.Missing
                        or CampaignDependencyState.Unavailable;
            issues.Add(
                new ReferenceIssue(
                    "dependency-unavailable",
                    $"{availability.Path}: {availability.State}.",
                    blocks
                        ? ReferenceSeverity.Error
                        : ReferenceSeverity.Warning,
                    null,
                    blocks));
        }
    }

    private static bool AnimationUnavailable(CampaignDependencyState state) =>
        state is not (CampaignDependencyState.Ready or CampaignDependencyState.Missing);

    private static CheckedReference Lookup(
        MissionReference reference,
        HashSet<string>? index,
        string label)
    {
        if (index == null)
        {
            return Unverified(
                reference,
                $"The {label} index is unavailable.");
        }

        string value = ReferenceName(reference);
        return index.Contains(value)
            ? Resolved(reference)
            : Missing(reference, label);
    }

    private static string ReferenceName(MissionReference reference)
    {
        if (reference.Value.Name is { } name)
        {
            return name;
        }

        return reference.Value.Segments.IsDefaultOrEmpty
            ? string.Empty
            : string.Join("/", reference.Value.Segments);
    }

    private static CheckedReference Resolved(
        MissionReference reference) =>
        Checked(reference, ReferenceState.Resolved, null);

    private static CheckedReference Missing(
        MissionReference reference,
        string label) =>
        Checked(
            reference,
            ReferenceState.Missing,
            $"Unresolved {label} '{ReferenceName(reference)}'.");

    private static CheckedReference Unverified(
        MissionReference reference,
        string detail) =>
        Checked(reference, ReferenceState.Unverified, detail);

    private static CheckedReference Checked(
        MissionReference reference,
        ReferenceState state,
        string? detail) =>
        new(reference, state, detail);

    private static ReferenceIssue ParseIssue(
        string code,
        Exception error) =>
        new(
            code,
            error.Message,
            ReferenceSeverity.Error,
            null,
            true);

    private static bool IsReaderError(Exception error) =>
        error is InvalidDataException or ArgumentException
            or InvalidOperationException or IndexOutOfRangeException
            or KeyNotFoundException;

    private static void AddStrings(
        List<object?> values,
        HashSet<string> target)
    {
        foreach (object? value in values)
        {
            if (value is string text && text.Length != 0)
            {
                target.Add(text);
            }
            else if (value is List<object?> child)
            {
                AddStrings(child, target);
            }
        }
    }

    private static string Stem(string path)
    {
        string normalized = path.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        string name = slash >= 0
            ? normalized[(slash + 1)..]
            : normalized;
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".json".Length];
        }

        if (name.EndsWith(".zrd", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".zrd".Length];
        }

        return name;
    }

    private void IndexScope(ZrdrSet scope, bool missionScope)
    {
        if (!scope.Exists)
        {
            return;
        }

        foreach (ZrdrFile file in scope.Files)
        {
            _readers.Add(file.Name);
            _readers.Add(file.Key);
            _readers.Add(Stem(file.Name));
            _readers.Add(Stem(file.Key));
            string reader = Stem(file.Key).ToLowerInvariant();
            if (reader is "zeppelins" or "egen")
            {
                continue;
            }

            if (missionScope && reader == "aiv"
                && ReferenceEquals(file, ZrdrZbd.Find(scope.Files, "aiv.json")))
            {
                for (int i = 1; i < file.Root.Count; i++)
                {
                    if (file.Root[i] is List<object?> { Count: >= 2 } block
                        && block[0] is string name
                        && block[1] is List<object?>)
                    {
                        _rosterNodes.Add(name);
                    }
                }
            }

            IndexPairs(file.Root);
        }
    }

    private void IndexPairs(List<object?> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is List<object?> child)
            {
                IndexPairs(child);
            }
        }

        for (int i = 0; i + 1 < list.Count; i++)
        {
            if (list[i] is not string key
                || list[i + 1] is not List<object?> values)
            {
                continue;
            }

            string upper = key.ToUpperInvariant();
            if (upper is "ANIM_NAME" or "ANIMATION_NAME")
            {
                AddStrings(values, _animations);
            }
        }
    }

    private void IndexGenerators(ZrdrSet mission)
    {
        ZrdrFile? file = ZrdrZbd.Find(mission.Files, "egen.json");
        if (file == null || file.Root.Count == 0 || file.Root[0] is not List<object?> records)
        {
            return;
        }

        foreach (object? value in records)
        {
            if (value is List<object?> record
                && CampaignMissionGenerators.HasRequiredFields(record)
                && CampaignMissionGenerators.SelectedString(record, "node") is string host)
            {
                _generators.Add(host);
            }
        }
    }

    private void IndexGeneratorTemplates(
        ZrdrSet mission,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        if (!mission.HasFile("aiv.json"))
        {
            return;
        }

        bool planningFailed = false;
        foreach ((string parameter, string name, List<object?> fields) in AiSkills.LoadGeneratorRoster(mission))
        {
            _templateCandidates.Add(parameter);
            if (_vehicles == null || _nets == null)
            {
                _unverifiedTemplates.Add(parameter);
                continue;
            }

            try
            {
                if (CampaignRosterPlan.BuildGeneratorTemplate(name, fields, _vehicles, _nets) != null)
                {
                    _templates.Add(parameter);
                }
            }
            catch (Exception error) when (IsReaderError(error))
            {
                planningFailed = true;
                issues.Add(ParseIssue("generator-template-index", error));
            }
        }

        if (planningFailed)
        {
            _unverifiedTemplates.UnionWith(_templateCandidates);
        }
    }

    private void IndexCompiled(AnimArchive? archive)
    {
        if (archive == null)
        {
            return;
        }

        foreach (AnimDefinition definition in archive.Defs)
        {
            if (!string.IsNullOrWhiteSpace(definition.AnimName))
            {
                _animations.Add(definition.AnimName);
            }
        }
    }

    private CheckedReference ResolveVehicle(MissionReference reference)
    {
        string? name = reference.Value.Name;
        if (_vehicles == null)
        {
            return Unverified(
                reference,
                "The shared vehicle definitions are unavailable.");
        }

        return !string.IsNullOrWhiteSpace(name)
            && _vehicles.DefForBlock(name) != null
            ? Resolved(reference)
            : Missing(reference, "vehicle block");
    }

    private CheckedReference ResolveTemplate(MissionReference reference)
    {
        string name = ReferenceName(reference);
        if (_templateCandidates.Contains(name) && _unverifiedTemplates.Contains(name))
        {
            return Unverified(reference, "The roster label exists, but its vehicle plan cannot be checked.");
        }

        if (_templates.Contains(name))
        {
            return Resolved(reference);
        }

        return Missing(reference, "generator template");
    }

    private CheckedReference ResolveGeneratorHost(MissionReference reference)
    {
        if (_world == null || _worldNames == null)
        {
            return Unverified(reference, "The chapter world is unavailable.");
        }

        List<GameZNode> hosts = _worldNames.FindAll(ReferenceName(reference), null);
        if (hosts.Count == 0)
        {
            return Missing(reference, "generator host");
        }

        return hosts.Count != 1 || RequiresStaging(hosts[0])
            ? Unverified(reference, "The live generator host binding requires runtime staging.")
            : Resolved(reference);
    }

    private CheckedReference ResolveGeneratorChild(MissionReference reference)
    {
        if (_world == null || _worldNames == null)
        {
            return Unverified(reference, "The chapter world is unavailable.");
        }

        if (reference.Context.RelatedName is not string hostName)
        {
            return Unverified(reference, "The selected generator host is malformed.");
        }

        List<GameZNode> hosts = _worldNames.FindAll(hostName, null);
        if (hosts.Count != 1 || RequiresStaging(hosts[0]))
        {
            return Unverified(reference, "The selected host cannot be checked in a single live subtree.");
        }

        List<GameZNode> children = _worldNames.FindAll(ReferenceName(reference), hosts[0]);
        if (children.Count == 0)
        {
            return Missing(reference, "node under generator host");
        }

        return children.Count != 1 || RequiresStaging(children[0])
            ? Unverified(reference, "The live host-relative node binding requires runtime staging.")
            : Resolved(reference);
    }

    private CheckedReference ResolveGeneratorNet(MissionReference reference)
    {
        if (_nets == null)
        {
            return Unverified(reference, "The chapter AI-net scope is unavailable.");
        }

        return AiNets.ByName(_nets, ReferenceName(reference)) != null
            ? Resolved(reference)
            : Missing(reference, "chapter AI net name");
    }

    private CheckedReference ResolveNetId(MissionReference reference)
    {
        if (_nets == null)
        {
            return Unverified(
                reference,
                "The chapter AI-net scope is unavailable.");
        }

        if (reference.Value.Number is not long number
            || number < int.MinValue
            || number > int.MaxValue)
        {
            return Missing(reference, "AI net ID");
        }

        AiNet? net = AiNets.ById(_nets, (int)number);
        return net != null && net.Nodes.Count != 0
            ? Checked(reference, ReferenceState.Resolved,
                string.IsNullOrEmpty(net.Name)
                    ? "The chapter net has no indexed name."
                    : $"Chapter net '{net.Name}'.")
            : Missing(reference, "AI net ID");
    }

    private CheckedReference ResolveNetName(MissionReference reference)
    {
        if (_nets == null)
        {
            return Unverified(
                reference,
                "The chapter AI-net scope is unavailable.");
        }

        string? name = reference.Value.Name;
        AiNet? net = string.IsNullOrWhiteSpace(name)
            ? null
            : AiNets.ByName(_nets, name);
        return net != null && net.Nodes.Count != 0
            ? Resolved(reference)
            : Missing(reference, "AI net name");
    }

    private CheckedReference ResolveNetStop(MissionReference reference)
    {
        if (reference.Kind == ReferenceKind.AiNet)
        {
            return ResolveNetName(reference);
        }

        return Unverified(
            reference,
            "A stop point must be checked with its selected AI net.");
    }

    private CheckedReference ResolveTarget(MissionReference reference)
    {
        string key = ReferenceName(reference);
        if (_targets == null)
        {
            return Unverified(
                reference,
                "The selected mission/chapter target table is unavailable.");
        }

        if (_targets.Contains(key))
        {
            return Resolved(reference);
        }

        CheckedReference world = ResolveTargetNode(reference);
        return world.State == ReferenceState.Resolved
            ? world
            : world.State == ReferenceState.Unverified
                ? world
            : reference.Context.NeedsRuntimeStaging
                ? Unverified(
                    reference,
                    "The target may be supplied by runtime staging.")
                : Missing(reference, "target");
    }

    private CheckedReference ResolveTargetNode(MissionReference reference)
    {
        if (_world == null || _worldNames == null)
        {
            return Unverified(reference, "The chapter world is unavailable.");
        }

        ImmutableArray<string> segments = reference.Value.Segments.IsDefaultOrEmpty
            ? ImmutableArray.Create(ReferenceName(reference))
            : reference.Value.Segments;
        GameZNode? scope = null;
        bool staged = false;
        foreach (string segment in segments)
        {
            List<GameZNode> found = _worldNames.FindAll(segment, scope);
            if (found.Count == 0)
            {
                return staged || _rosterNodes.Contains(segments[0])
                    ? Unverified(
                        reference,
                        "The target may require a staged world or roster binding.")
                    : Missing(reference, "world target");
            }

            scope = found[0];
            staged |= RequiresStaging(scope);
        }

        return staged
            ? Unverified(reference, "The matching world subtree requires runtime staging.")
            : Resolved(reference);
    }

    private bool RequiresStaging(GameZNode node)
    {
        GameZ world = _world!;
        GameZNode root = node;
        while (world.ParentOf(root) is { } parent)
        {
            root = parent;
        }

        return world.IsLibraryRoot(root) || world.IsStagedProp(root);
    }

    private CheckedReference ResolveWorld(
        MissionReference reference,
        bool allowPattern)
    {
        if (_world == null)
        {
            return Unverified(
                reference,
                "The chapter world is unavailable.");
        }

        string name = ReferenceName(reference);
        if (_world.FindByName(name) != null)
        {
            return Resolved(reference);
        }

        if (name.EndsWith(".flt", StringComparison.OrdinalIgnoreCase)
            && _world.FindByName(name[..^4]) != null)
        {
            return Resolved(reference);
        }

        if (allowPattern
            && (name.IndexOf('*') >= 0
                || name.IndexOf('#') >= 0))
        {
            return Unverified(
                reference,
                "Runtime wildcard matching requires the live resolver.");
        }

        return Missing(reference, "world node");
    }

    private CheckedReference ResolveWorldPath(MissionReference reference)
    {
        if (_world == null)
        {
            return Unverified(
                reference,
                "The chapter world is unavailable.");
        }

        ImmutableArray<string> segments = reference.Value.Segments;
        if (segments.IsDefaultOrEmpty)
        {
            return ResolveWorld(reference, allowPattern: true);
        }

        GameZNode? node = _world.FindByName(segments[0]);
        if (node == null)
        {
            return Missing(reference, "world path");
        }

        for (int i = 1; i < segments.Length; i++)
        {
            string segment = segments[i];
            node = _world.ChildrenOf(node).Find(
                child => string.Equals(
                    child.Name,
                    segment,
                    StringComparison.OrdinalIgnoreCase));
            if (node == null)
            {
                return Missing(reference, "world path");
            }
        }

        return Resolved(reference);
    }

    private CheckedReference ResolveRuntimeActor(
        MissionReference reference)
    {
        CheckedReference world = ResolveWorld(
            reference,
            allowPattern: true);
        return world.State == ReferenceState.Resolved
            ? world
            : Unverified(
                reference,
                "The actor may be created or renamed during mission staging.");
    }

    private CheckedReference ResolveAnimation(MissionReference reference)
    {
        CheckedReference match = Lookup(reference, _animations, "effective animation");
        if (match.State == ReferenceState.Missing
            && (AnimationUnavailable(_bundle.ChapterAnimations.Availability.State)
                || AnimationUnavailable(_bundle.MissionAnimations.Availability.State)))
        {
            return Unverified(reference, "An animation archive is unavailable, so its definitions cannot be checked.");
        }

        if (match.State == ReferenceState.Missing
            && reference.Source.Context == "generator:default-door")
        {
            return Unverified(reference, "The default door definition is absent; the runtime continues without it.");
        }

        if (match.State == ReferenceState.Resolved
            && reference.Source.Context is { } context
            && context.StartsWith("generator:", StringComparison.Ordinal))
        {
            return Unverified(reference, "The definition exists; its host-scoped live binding is not checked.");
        }

        return match;
    }

    private CheckedReference ResolveStartAnimation(MissionReference reference)
    {
        if (AnimationInspection.Program is not AnimProgram program)
        {
            return Unverified(reference, AnimationInspection.UnavailableReason ?? "The animation program is unavailable.");
        }

        int count = program.ByAnimName(ReferenceName(reference)).Count;
        return count == 0
            ? Missing(reference, "effective start animation")
            : Checked(reference, ReferenceState.Resolved,
                $"{count} definition(s) are visible in the compiled-first animation program; live binding is not checked.");
    }

    private CheckedReference ResolveObjective(
        MissionReference reference)
    {
        if (reference.Value.Number is not long value
            || value < int.MinValue
            || value > int.MaxValue)
        {
            return Missing(reference, "objective number");
        }

        return _objectives.Contains((int)value)
            ? Resolved(reference)
            : Missing(reference, "objective number");
    }

    private CheckedReference ResolveDangerGeometry(MissionReference reference)
    {
        if (_world == null)
        {
            return Unverified(reference, "The selected chapter world is unavailable.");
        }

        string name = ReferenceName(reference);
        if (_world.FindByName(name) == null)
        {
            return Checked(
                reference,
                ReferenceState.Missing,
                $"'{name}' has no node in the selected chapter gamez; other scopes cannot supply its gates.");
        }

        try
        {
            return CampaignDangerZones.HasGateGeometry(_world, name)
                ? Resolved(reference)
                : Checked(
                    reference,
                    ReferenceState.Missing,
                    $"The first chapter-world node named '{name}' has no usable gate pair.");
        }
        catch (Exception error) when (IsReaderError(error))
        {
            return Unverified(reference, $"The chapter gate geometry could not be inspected: {error.Message}");
        }
    }

    private CheckedReference ResolveDangerOverridePath(MissionReference reference)
    {
        if (_objectiveDangerZones.Contains(ReferenceName(reference)))
        {
            return Checked(
                reference,
                ReferenceState.Resolved,
                "A selected objective consumes this disable before player gates are armed.");
        }

        CheckedReference gate = ResolveDangerGeometry(reference);
        if (gate.State != ReferenceState.Missing || _world == null)
        {
            return gate;
        }

        _dangerWorld ??= CampaignDangerZoneWorld.Read(_bundle.World);
        CampaignDangerRibbon? ribbon = _dangerWorld.Ribbon(ReferenceName(reference));
        if (ribbon != null
            && string.Equals(ribbon.Name, ReferenceName(reference), StringComparison.OrdinalIgnoreCase))
        {
            return Checked(reference, ReferenceState.Resolved, "The selected AI ribbon consumes this disable; its player gates are unavailable.");
        }

        return _dangerWorld.Problem.Length == 0
            ? gate
            : Unverified(reference, $"The selected AI ribbon could not be checked: {_dangerWorld.Problem}");
    }

    private CheckedReference ResolveDangerSlot(MissionReference reference)
    {
        long slot = reference.Value.Number ?? -1;
        string detail = slot switch
        {
            < 0 => "A negative slot does not record completion or stage a snapshot.",
            < 10 => "A snapshot can be staged, but the commit sweep covers only slots 10 through 31.",
            < 18 => "A snapshot can be committed; this slot does not light a debrief row.",
            <= 30 => "This slot can commit a snapshot and light its debrief row.",
            31 => "This slot can commit a snapshot but does not light a debrief row.",
            _ => "A snapshot can be staged, but neither the commit sweep nor the debrief mask covers this slot.",
        };
        return Checked(reference, ReferenceState.Resolved, detail);
    }

    private CheckedReference ResolvePickup(MissionReference reference)
    {
        CheckedReference world = ResolveWorld(
            reference,
            allowPattern: false);
        return world.State == ReferenceState.Resolved
            ? world
            : Unverified(
                reference,
                "The pickup host may be created during mission staging.");
    }
}

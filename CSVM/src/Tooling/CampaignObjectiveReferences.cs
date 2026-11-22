using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace CSVM.Tooling;

/// <summary>Objective and briefing references at their original leaf paths.</summary>
internal static class CampaignObjectiveReferences
{
    internal static void Extract(
        List<object?> root,
        string container,
        int fileIndex,
        bool selectedReader,
        ImmutableArray<MissionReference>.Builder references,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        if (root.Count == 0 || root[0] is not List<object?> body)
        {
            if (selectedReader)
            {
                issues.Add(new ReferenceIssue(
                    "malformed-objectives-root",
                    $"{container}: the runtime expects an objective list at root index 0.",
                    ReferenceSeverity.Warning,
                    Source(container, fileIndex, ImmutableArray<int>.Empty),
                    false));
            }

            return;
        }

        List<CampaignObjectivePair> top = CampaignMissionObjectives.Pairs(body);
        var firstBlocks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (CampaignObjectivePair pair in top)
        {
            firstBlocks.TryAdd(pair.Key, pair.Index);
        }

        int contiguous = 0;
        while (firstBlocks.ContainsKey("OBJECTIVE" + (contiguous + 1).ToString(CultureInfo.InvariantCulture)))
        {
            contiguous++;
        }

        foreach (CampaignObjectivePair block in top)
        {
            bool canonical = CampaignMissionObjectives.TryObjectiveNumber(block.Key, out int number);
            bool active = selectedReader && canonical && number <= contiguous && firstBlocks[block.Key] == block.Index;
            if (selectedReader && canonical && number > contiguous)
            {
                issues.Add(new ReferenceIssue(
                    "objective-after-gap",
                    $"{block.Key} follows a missing objective number and is not run by ObjectiveScript.",
                    ReferenceSeverity.Warning,
                    Source(container, fileIndex, ImmutableArray.Create(0, block.Index)),
                    false));
            }
            else if (selectedReader && canonical && firstBlocks[block.Key] != block.Index)
            {
                issues.Add(new ReferenceIssue(
                    "duplicate-objective",
                    $"{block.Key} is shadowed at runtime; briefing identities can still be read.",
                    ReferenceSeverity.Info,
                    Source(container, fileIndex, ImmutableArray.Create(0, block.Index)),
                    false));
            }

            if (!block.Key.StartsWith("OBJECTIVE", StringComparison.OrdinalIgnoreCase) || block.Values == null)
            {
                ExtractFileSound(block, top, selectedReader, container, fileIndex, references);
                continue;
            }

            List<CampaignObjectivePair> directives = CampaignMissionObjectives.Pairs(block.Values);
            foreach (CampaignObjectivePair directive in directives)
            {
                if (directive.Values == null)
                {
                    continue;
                }

                bool selected = active && CampaignMissionObjectives.IsSelected(directives, directive);
                ImmutableArray<int> path = ImmutableArray.Create(0, block.ValueIndex, directive.ValueIndex);
                ExtractDirective(
                    directive.Key,
                    directive.Values,
                    path,
                    selected,
                    selectedReader,
                    container,
                    fileIndex,
                    references);
            }

            ExtractFileSound(block, top, selectedReader, container, fileIndex, references);
        }
    }

    private static void ExtractFileSound(
        CampaignObjectivePair pair,
        IReadOnlyList<CampaignObjectivePair> top,
        bool selectedReader,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (pair.Key is not ("PRIMARY_COMPLETE_SOUND" or "SECONDARY_COMPLETE_SOUND"
            or "TERTIARY_COMPLETE_SOUND" or "MISSION_WON_SOUND" or "MISSION_LOST_SOUND"
            or "OBJECTIVES_WON_SOUND" or "OBJECTIVES_LOST_SOUND")
            || pair.Values == null)
        {
            return;
        }

        bool selected = selectedReader && top.First(item => item.Key == pair.Key).Index == pair.Index;
        NameAt(pair.Values, 0, ReferenceKind.Sound, ReferenceRule.SoundCue,
            ImmutableArray.Create(0, pair.ValueIndex), selected, container, fileIndex, references);
    }

    private static void ExtractDirective(
        string key,
        List<object?> values,
        ImmutableArray<int> path,
        bool selected,
        bool selectedReader,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (key.Equals("IDENTITY", StringComparison.OrdinalIgnoreCase))
        {
            bool briefing = selectedReader && values.Count >= 3 && values[1] is float
                && values[2] is string { Length: > 0 };
            bool runtime = selected && key == "IDENTITY" && values.Count >= 2;
            if (briefing || runtime)
            {
                NameAt(values, 2, ReferenceKind.Message, ReferenceRule.MessageKey,
                    path, true, container, fileIndex, references);
            }
            else
            {
                NameAt(values, 2, ReferenceKind.Message, ReferenceRule.MessageKey,
                    path, false, container, fileIndex, references);
            }

            return;
        }

        if (CampaignMissionObjectives.IsInactive(key))
        {
            List<(string Name, ImmutableArray<int> Path)> names = CampaignMissionObjectives.Names(values, path);
            if (names.Count > 0)
            {
                AddPath(new CampaignObjectivePath(
                    names.Select(item => item.Name).ToArray(),
                    names.Select(item => item.Path).ToArray()),
                    ReferenceKind.WorldPath, ReferenceRule.TargetNode,
                    selected, container, fileIndex, references);
            }

            return;
        }

        if (key is "ADD_OBJECTIVE_TARGET" or "REMOVE_OBJECTIVE_TARGET"
            or "ADD_OTHER_TARGET" or "REMOVE_OTHER_TARGET")
        {
            for (int i = 0; i < values.Count; i++)
            {
                AddTarget(values[i], path.Add(i), selected, container, fileIndex, references);
            }

            return;
        }

        if (key == "SET_HELP_LABEL")
        {
            if (values.Count >= 2 && values[^1] is string
                && CampaignMissionObjectives.Target(values[0], path.Add(0)) is { } target)
            {
                AddTarget(target, selected, container, fileIndex, references);
                NameAt(values, values.Count - 1, ReferenceKind.Message, ReferenceRule.MessageKey,
                    path, selected, container, fileIndex, references);
            }

            return;
        }

        if (key is "WAKE_OBJECTIVE_WHEN_I_COMPLETE" or "WAKE_OBJECTIVE"
            or "KILL_OBJECTIVE_WHEN_I_COMPLETE" or "SLEEP_OBJECTIVE_WHEN_I_COMPLETE"
            or "WAKE_OBJECTIVE_WHEN_I_SLEEP")
        {
            for (int i = 0; i < values.Count; i++)
            {
                NumberAt(values, i, path, selected, false, container, fileIndex, references);
            }

            return;
        }

        if (key is "TICK_DEPENDS_ON_OBJ" or "HIDE_OBJ" or "NAP_OBJECTIVE_WHEN_I_COMPLETE")
        {
            NumberAt(values, 0, path, selected, key is "TICK_DEPENDS_ON_OBJ" or "HIDE_OBJ",
                container, fileIndex, references);
            return;
        }

        if (key is "WAKEUP_ENEMIES" or "WAKEUP_TURRETS" or "WAKEUP_ZEP_TURRETS"
            or "START_TAXI" or "STOP_QUEUED_SOUNDS")
        {
            (ReferenceKind kind, ReferenceRule rule, bool staging) = key switch
            {
                "WAKEUP_TURRETS" => (ReferenceKind.WorldNode, ReferenceRule.GlobalPattern, true),
                "WAKEUP_ZEP_TURRETS" => (ReferenceKind.WorldNode, ReferenceRule.GlobalPattern, true),
                "START_TAXI" => (ReferenceKind.ScriptedPath, ReferenceRule.ScriptedPath, true),
                "STOP_QUEUED_SOUNDS" => (ReferenceKind.Sound, ReferenceRule.QueuedCue, false),
                _ => (ReferenceKind.Actor, ReferenceRule.RuntimeActor, true),
            };
            foreach ((string name, ImmutableArray<int> leaf) in CampaignMissionObjectives.Names(values, path))
            {
                AddName(name, kind, rule, leaf, selected, container, fileIndex, references, staging);
            }

            return;
        }

        if (key is "WAKEUP_SOUND_GROUP" or "COMPLETED_SOUND_GROUP" or "SLEEP_ANIM" or "WAKE_ANIM")
        {
            bool animation = key is "SLEEP_ANIM" or "WAKE_ANIM";
            NameAt(values, 0, animation ? ReferenceKind.Animation : ReferenceKind.Sound,
                animation ? ReferenceRule.EffectiveAnimation : ReferenceRule.SoundCue,
                path, selected, container, fileIndex, references);
            if (key == "WAKE_ANIM")
            {
                NameAt(values, 1, ReferenceKind.WorldNode, ReferenceRule.GlobalPattern,
                    path, selected, container, fileIndex, references, true);
            }

            return;
        }

        if (key == "TRAVELERS" && values.Count >= 4)
        {
            NameAt(values, 0, ReferenceKind.Actor, ReferenceRule.RuntimeActor,
                path, selected, container, fileIndex, references, true);
            NameAt(values, 2, ReferenceKind.Actor, ReferenceRule.RuntimeActor,
                path, selected, container, fileIndex, references, true);
            return;
        }

        if (key is "SET_AI_TEAM" or "SET_AI_NET" or "SET_AI_ATTACK_RADIUS" or "COMPLETED_ZEPCANNONS"
            or "COMPLETED_STOPPOINT")
        {
            ExtractPairs(key, values, path, selected, container, fileIndex, references);
            return;
        }

        if (key == "ANIM_STATE")
        {
            ExtractAnimations(values, path, selected, container, fileIndex, references);
            return;
        }

        if (key == "WARP_VEHICLE" && values.Count >= 2 && values[0] is string)
        {
            bool hasPoint = values.Skip(1).Any(value => value is List<object?> { Count: >= 4 });
            if (!hasPoint)
            {
                return;
            }

            NameAt(values, 0, ReferenceKind.Actor, ReferenceRule.RuntimeActor,
                path, selected, container, fileIndex, references, true);
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] is List<object?> { Count: >= 4 } point)
                {
                    NameAt(point, 4, ReferenceKind.ScriptedPath, ReferenceRule.ScriptedPath,
                        path.Add(i), selected, container, fileIndex, references, true);
                }
            }
        }
    }

    private static void ExtractPairs(
        string key,
        List<object?> values,
        ImmutableArray<int> path,
        bool selected,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] is not List<object?> entry
                || entry.Count < (key == "COMPLETED_STOPPOINT" ? 3 : 2)
                || entry[0] is not string)
            {
                continue;
            }

            ImmutableArray<int> entryPath = path.Add(i);
            bool stop = key == "COMPLETED_STOPPOINT";
            NameAt(entry, 0, stop ? ReferenceKind.AiNet : ReferenceKind.Actor,
                stop ? ReferenceRule.NetStopPoint : ReferenceRule.RuntimeActor,
                entryPath, selected, container, fileIndex, references, !stop);
            if (key == "SET_AI_NET")
            {
                NameAt(entry, 1, ReferenceKind.AiNet, ReferenceRule.NetName,
                    entryPath, selected, container, fileIndex, references);
            }

            if (stop && entry[1] is float number && float.IsFinite(number))
            {
                AddNumber((int)number, ReferenceKind.StopPoint, ReferenceRule.NetStopPoint,
                    entryPath.Add(1), selected, container, fileIndex, references);
            }
        }
    }

    private static void ExtractAnimations(
        List<object?> values,
        ImmutableArray<int> path,
        bool selected,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        foreach (CampaignObjectivePair anim in CampaignMissionObjectives.Pairs(values))
        {
            if (anim.Key != "ANIM" || anim.Values == null)
            {
                continue;
            }

            List<CampaignObjectivePair> fields = CampaignMissionObjectives.Pairs(anim.Values);
            foreach (CampaignObjectivePair field in fields)
            {
                if (field.Key == "NAME" && field.Values != null)
                {
                    bool first = fields.First(candidate => candidate.Key == "NAME").Index == field.Index;
                    NameAt(field.Values, 0, ReferenceKind.Animation, ReferenceRule.EffectiveAnimation,
                        path.Add(anim.ValueIndex).Add(field.ValueIndex), selected && first,
                        container, fileIndex, references);
                }
            }
        }
    }

    private static void AddTarget(
        object? value,
        ImmutableArray<int> path,
        bool selected,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (CampaignMissionObjectives.Target(value, path) is { } target)
        {
            AddTarget(target, selected, container, fileIndex, references);
        }
    }

    private static void AddTarget(
        CampaignObjectivePath target,
        bool selected,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        var resolved = target with { Segments = string.Join("/", target.Segments).Split('/') };
        AddPath(resolved, ReferenceKind.Target, ReferenceRule.TargetSite,
            selected, container, fileIndex, references);
    }

    private static void AddPath(
        CampaignObjectivePath path,
        ReferenceKind kind,
        ReferenceRule rule,
        bool selected,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        references.Add(new MissionReference(
            kind,
            rule,
            new ReferenceValue(null, null, path.Segments.ToImmutableArray()),
            new ReferenceSource(ReferenceScope.Mission, container, fileIndex, path.Leaves[0],
                path.Leaves.ToImmutableArray(), null, false),
            Context(selected)));
    }

    private static void NameAt(
        List<object?> values,
        int index,
        ReferenceKind kind,
        ReferenceRule rule,
        ImmutableArray<int> path,
        bool selected,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references,
        bool staging = false)
    {
        if ((uint)index < (uint)values.Count && values[index] is string value)
        {
            AddName(value, kind, rule, path.Add(index), selected, container, fileIndex, references, staging);
        }
    }

    private static void AddName(
        string name,
        ReferenceKind kind,
        ReferenceRule rule,
        ImmutableArray<int> path,
        bool selected,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references,
        bool staging = false)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            references.Add(new MissionReference(kind, rule,
                new ReferenceValue(name, null, ImmutableArray<string>.Empty),
                Source(container, fileIndex, path),
                Context(selected, staging)));
        }
    }

    private static void NumberAt(
        List<object?> values,
        int index,
        ImmutableArray<int> path,
        bool selected,
        bool skipNonPositive,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        if ((uint)index < (uint)values.Count && values[index] is float number
            && float.IsFinite(number) && number >= int.MinValue && number < 2147483648f)
        {
            int converted = (int)number;
            if (!skipNonPositive || converted > 0)
            {
                AddNumber(converted, ReferenceKind.Objective, ReferenceRule.ObjectiveNumber,
                    path.Add(index), selected, container, fileIndex, references);
            }
        }
    }

    private static void AddNumber(
        int number,
        ReferenceKind kind,
        ReferenceRule rule,
        ImmutableArray<int> path,
        bool selected,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references) =>
        references.Add(new MissionReference(kind, rule,
            new ReferenceValue(null, number, ImmutableArray<string>.Empty),
            Source(container, fileIndex, path),
            Context(selected)));

    private static ReferenceSource Source(string container, int fileIndex, ImmutableArray<int> path) =>
        new(ReferenceScope.Mission, container, fileIndex, path,
            path.IsDefaultOrEmpty ? ImmutableArray<ImmutableArray<int>>.Empty : ImmutableArray.Create(path),
            null, false);

    private static ReferenceContext Context(bool selected, bool staging = false) =>
        new(ImmutableArray<string>.Empty, null, null, null, selected ? null : "suppressed", staging);
}

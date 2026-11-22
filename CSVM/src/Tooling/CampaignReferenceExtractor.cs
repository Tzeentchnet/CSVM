using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using CSVM.Mech3;
using CSVM.Session.Campaign;

namespace CSVM.Tooling;

internal static class CampaignReferenceExtractor
{
    internal const string UnmatchedZeppelinGasbag = "zeppelin:unmatched-gasbag";

    internal static ReferenceExtraction Extract(
        ZrdrSet editableMission,
        CampaignMissionDependencyBundle bundle)
    {
        var references = ImmutableArray.CreateBuilder<MissionReference>();
        var issues = ImmutableArray.CreateBuilder<ReferenceIssue>();
        ZrdrFile? selectedRoster = ZrdrZbd.Find(
            editableMission.Files, "aiv.json");
        ZrdrFile? selectedTargets = ZrdrZbd.Find(
            editableMission.Files, "targets.json");
        ZrdrFile? selectedZeppelins = ZrdrZbd.Find(
            editableMission.Files, "zeppelins.json");
        ZrdrFile? selectedGenerators = ZrdrZbd.Find(
            editableMission.Files, "egen.json");
        ZrdrFile? selectedObjectives = ZrdrZbd.Find(
            editableMission.Files, "objectives.json");
        ZrdrFile? selectedDangerZones = ZrdrZbd.Find(
            editableMission.Files, "dzones.json");
        ZrdrFile? selectedStartAnimations = ZrdrZbd.Find(
            editableMission.Files, "startanims.json");
        HashSet<string> disabledDangerZones = CampaignDangerZones.ReadDisabled(editableMission);
        for (int fileIndex = 0;
            fileIndex < editableMission.Files.Count;
            fileIndex++)
        {
            ZrdrFile file = editableMission.Files[fileIndex];
            string reader = ReaderName(file.Key);
            try
            {
                switch (reader)
                {
                    case "aiv":
                        if (ReferenceEquals(file, selectedRoster))
                        {
                            ExtractRoster(
                                file.Root,
                                fileIndex,
                                file.Key,
                                references,
                                issues);
                        }

                        break;
                    case "pickups":
                        ExtractPickups(
                            file.Root,
                            fileIndex,
                            file.Key,
                            ImmutableArray<int>.Empty,
                            references);
                        break;
                    case "targets":
                        if (ReferenceEquals(file, selectedTargets))
                        {
                            ExtractTargets(
                                file.Root,
                                file.Key,
                                fileIndex,
                                ReferenceScope.Mission,
                                references);
                        }

                        break;
                    case "zeppelins":
                        if (ReferenceEquals(file, selectedZeppelins))
                        {
                            ExtractZeppelins(
                                file.Root,
                                file.Key,
                                fileIndex,
                                references,
                                issues);
                        }

                        break;
                    case "egen":
                        if (ReferenceEquals(file, selectedGenerators))
                        {
                            ExtractGenerators(
                                file.Root,
                                file.Key,
                                fileIndex,
                                references,
                                issues);
                        }

                        break;
                    case "objectives":
                        CampaignObjectiveReferences.Extract(
                            file.Root,
                            file.Key,
                            fileIndex,
                            ReferenceEquals(file, selectedObjectives),
                            references,
                            issues);
                        if (ReferenceEquals(file, selectedObjectives))
                        {
                            ExtractObjectiveGenerators(
                                file.Root,
                                file.Key,
                                fileIndex,
                                references);
                        }

                        ExtractObjectiveDangerZones(
                            file.Root,
                            file.Key,
                            fileIndex,
                            ReferenceEquals(file, selectedObjectives),
                            disabledDangerZones,
                            references);
                        break;
                    case "dzones":
                        ExtractDangerOverrides(
                            file.Root,
                            file.Key,
                            fileIndex,
                            ReferenceEquals(file, selectedDangerZones),
                            references);
                        break;
                    case "startanims":
                        if (ReferenceEquals(file, selectedStartAnimations))
                        {
                            foreach (CampaignStartAnimationEntry entry in CampaignMissionStartAnimations.Scan(file.Root))
                            {
                                AddName(references, ReferenceKind.Animation, ReferenceRule.StartAnimation,
                                    entry.Name, file.Key, fileIndex, entry.NamePath, allowEmpty: true);
                            }
                        }

                        break;
                }

                if (reader is not ("targets" or "zeppelins" or "egen"))
                {
                    ExtractPairs(
                        file.Root,
                        fileIndex,
                        file.Key,
                        reader,
                        ImmutableArray<int>.Empty,
                        references);
                }
            }
            catch (Exception error) when (
                error is ArgumentException or InvalidOperationException
                    or IndexOutOfRangeException)
            {
                issues.Add(
                    new ReferenceIssue(
                        "malformed-reader",
                        $"{file.Key}: {error.Message}",
                        ReferenceSeverity.Error,
                        Source(
                            file.Key,
                            fileIndex,
                            ImmutableArray<int>.Empty),
                        true));
            }
        }

        if (!editableMission.HasFile("targets.json")
            && bundle.ChapterReaders.Availability.State
                == CampaignDependencyState.Ready
            && ZrdrZbd.Find(
                bundle.ChapterReaders.Value.Files,
                "targets.json") is { Root.Count: > 0 } inherited)
        {
            ExtractTargets(
                inherited.Root,
                inherited.Key,
                null,
                ReferenceScope.Chapter,
                references);
        }

        ExtractCompiled(
            bundle.ChapterAnimations,
            ReferenceScope.ChapterCompiled,
            references,
            issues);
        ExtractCompiled(
            bundle.MissionAnimations,
            ReferenceScope.MissionCompiled,
            references,
            issues);
        ExtractSetup(bundle, references, issues);
        AddCoverageIssues(editableMission, issues);
        return new ReferenceExtraction(
            references.ToImmutable(),
            issues.ToImmutable());
    }

    private static void ExtractTargets(
        List<object?> root,
        string container,
        int? fileIndex,
        ReferenceScope scope,
        ImmutableArray<MissionReference>.Builder references)
    {
        foreach (CampaignTargetRow row in CampaignMissionTargets.Scan(root))
        {
            for (int pairIndex = 0; pairIndex < row.Raw.Count; pairIndex++)
            {
                if (row.Raw[pairIndex] is not List<object?> { Count: > 1 } pair
                    || pair[0] is not string key
                    || pair[1] is not string value
                    || value.Length == 0)
                {
                    continue;
                }

                CampaignTargetLabel? label = key.ToLowerInvariant() switch
                {
                    "description" => CampaignTargetLabel.Description,
                    "category_label" => CampaignTargetLabel.Category,
                    "help_label" => CampaignTargetLabel.Help,
                    _ => null,
                };
                if (label is not CampaignTargetLabel field)
                {
                    continue;
                }

                ImmutableArray<int> path = Path(row.Index, pairIndex, 1);
                references.Add(new MissionReference(
                    ReferenceKind.Message,
                    ReferenceRule.MessageKey,
                    new ReferenceValue(value, null, ImmutableArray<string>.Empty),
                    new ReferenceSource(
                        scope,
                        container,
                        fileIndex,
                        path,
                        ImmutableArray.Create(path),
                        $"target:{row.Index}:{field}",
                        false),
                    Context(row.HasEffectiveNode && row.LabelIndex(field) == pairIndex)));
            }

            foreach (CampaignTargetNode node in row.Nodes)
            {
                references.Add(new MissionReference(
                    ReferenceKind.WorldPath,
                    ReferenceRule.TargetNode,
                    new ReferenceValue(
                        node.Segments.IsDefaultOrEmpty ? node.Key : null,
                        null,
                        node.Segments),
                    new ReferenceSource(
                        scope,
                        container,
                        fileIndex,
                        node.Path,
                        node.LeafPaths,
                        $"target:{row.Index}:node:{node.NodeIndex}",
                        false),
                    Context(node.Effective)));
            }
        }
    }

    private static void ExtractRoster(
        List<object?> root,
        int fileIndex,
        string container,
        ImmutableArray<MissionReference>.Builder references,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        for (int row = 1; row < root.Count; row++)
        {
            if (root[row] is not List<object?> block
                || block.Count < 2
                || block[0] is not string name
                || block[1] is not List<object?> fields)
            {
                issues.Add(
                    new ReferenceIssue(
                        "malformed-roster-row",
                        $"{container} row {row} is not a name/fields pair.",
                        ReferenceSeverity.Error,
                        Source(container, fileIndex, Path(row)),
                        true));
                continue;
            }

            if (!string.Equals(
                    name,
                    "player",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddName(
                    references,
                    ReferenceKind.Vehicle,
                    ReferenceRule.VehicleBlock,
                    name,
                    container,
                    fileIndex,
                    Path(row, 0));
            }

            ExtractNetIds(
                fields,
                container,
                fileIndex,
                Path(row, 1),
                references);
            AddFieldName(
                fields,
                6,
                ReferenceKind.Target,
                ReferenceRule.RuntimeActor,
                container,
                fileIndex,
                Path(row, 1),
                references,
                needsRuntime: true);
            AddFieldName(
                fields,
                20,
                ReferenceKind.Message,
                ReferenceRule.MessageKey,
                container,
                fileIndex,
                Path(row, 1),
                references);

            bool objective = AiSkills.RosterObjectiveTarget(fields);
            if (objective)
            {
                AddFieldName(
                    fields,
                    38,
                    ReferenceKind.Message,
                    ReferenceRule.MessageKey,
                    container,
                    fileIndex,
                    Path(row, 1),
                    references);
                AddFieldName(
                    fields,
                    39,
                    ReferenceKind.Message,
                    ReferenceRule.MessageKey,
                    container,
                    fileIndex,
                    Path(row, 1),
                    references);
            }

            AddFieldName(
                fields,
                40,
                ReferenceKind.ScriptedPath,
                ReferenceRule.ScriptedPath,
                container,
                fileIndex,
                Path(row, 1),
                references,
                needsRuntime: true);
            AddFieldNumber(
                fields,
                32,
                ReferenceKind.Maneuver,
                ReferenceRule.ManeuverName,
                container,
                fileIndex,
                Path(row, 1),
                references);
            AddFieldNumber(
                fields,
                65,
                ReferenceKind.Accent,
                ReferenceRule.AccentId,
                container,
                fileIndex,
                Path(row, 1),
                references,
                skipNegative: true);

        }
    }

    private static void ExtractNetIds(
        List<object?> fields,
        string container,
        int fileIndex,
        ImmutableArray<int> fieldsPath,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (fields.Count == 0)
        {
            return;
        }

        if (fields[0] is float scalar)
        {
            AddRosterNetId(scalar, container, fileIndex, Append(fieldsPath, 0), references);
            return;
        }

        if (fields[0] is not List<object?> ids)
        {
            return;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] is float id)
            {
                AddRosterNetId(id, container, fileIndex, Append(fieldsPath, 0, i), references);
            }
        }
    }

    private static void AddRosterNetId(
        float value,
        string container,
        int fileIndex,
        ImmutableArray<int> path,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (float.IsFinite(value) && value >= 0f)
        {
            AddNumber(references, ReferenceKind.AiNet, ReferenceRule.NetId,
                unchecked((int)value), container, fileIndex, path);
        }
    }

    private static void ExtractDangerOverrides(
        List<object?> root,
        string container,
        int fileIndex,
        bool selectedReader,
        ImmutableArray<MissionReference>.Builder references)
    {
        foreach (CampaignDangerOccurrence occurrence in CampaignMissionDangerZones.Scan(root))
        {
            bool selected = selectedReader && occurrence.Effective;
            string context = $"danger:override:{occurrence.Kind}";
            AddName(
                references,
                ReferenceKind.DangerZone,
                occurrence.Kind == CampaignDangerOverrideKind.Disable
                    ? ReferenceRule.DangerOverridePath
                    : ReferenceRule.DangerGeometry,
                occurrence.Name,
                container,
                fileIndex,
                occurrence.NamePath,
                selected,
                context: context);
            if (occurrence.Kind == CampaignDangerOverrideKind.ObjectiveNumber
                && occurrence.Number is float number)
            {
                references.Add(new MissionReference(
                    ReferenceKind.DangerZone,
                    ReferenceRule.DangerSlot,
                    new ReferenceValue(null, unchecked((int)number), ImmutableArray<string>.Empty),
                    Source(container, fileIndex, occurrence.NumberPath) with { Context = "danger:slot" },
                    Context(selected) with { RelatedName = occurrence.Name }));
            }
        }
    }

    private static void ExtractObjectiveDangerZones(
        List<object?> root,
        string container,
        int fileIndex,
        bool selectedReader,
        IReadOnlySet<string> disabled,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (root.Count == 0 || root[0] is not List<object?> body)
        {
            return;
        }

        List<(string Key, List<object?>? Values, int Index)> blocks = DangerObjectivePairs(body);
        var firstBlocks = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < blocks.Count; i++)
        {
            firstBlocks.TryAdd(blocks[i].Key, i);
        }

        int contiguous = 0;
        while (firstBlocks.ContainsKey("OBJECTIVE" + (contiguous + 1).ToString(CultureInfo.InvariantCulture)))
        {
            contiguous++;
        }

        for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            (string key, List<object?>? directives, int valueIndex) = blocks[blockIndex];
            const string prefix = "OBJECTIVE";
            if (!key.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(key.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                || number <= 0
                || key != prefix + number.ToString(CultureInfo.InvariantCulture)
                || directives == null)
            {
                continue;
            }

            bool selectedBlock = selectedReader && number <= contiguous && firstBlocks[key] == blockIndex;
            bool seenDirective = false;
            foreach ((string directive, List<object?>? values, int directiveIndex) in DangerObjectivePairs(directives))
            {
                if (directive != "DANGER_ZONES_COMPLETED")
                {
                    continue;
                }

                bool selected = selectedBlock && !seenDirective;
                seenDirective = true;
                if (values == null)
                {
                    continue;
                }

                ImmutableArray<int> path = Path(0, valueIndex, directiveIndex);
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] is string name)
                    {
                        AddDangerObjectiveName(name, Append(path, i), number, selected);
                    }
                    else if (values[i] is List<object?> inner)
                    {
                        for (int j = 0; j < inner.Count; j++)
                        {
                            if (inner[j] is string nested)
                            {
                                AddDangerObjectiveName(nested, Append(path, i, j), number, selected);
                            }
                        }
                    }
                }
            }
        }

        void AddDangerObjectiveName(string name, ImmutableArray<int> path, int number, bool selected) =>
            AddName(
                references,
                ReferenceKind.DangerZone,
                ReferenceRule.DangerGeometry,
                name,
                container,
                fileIndex,
                path,
                selected,
                context: $"danger:objective:{number.ToString(CultureInfo.InvariantCulture)}",
                suppression: disabled.Contains(name) ? "danger-disabled" : null);
    }

    private static List<(string Key, List<object?>? Values, int Index)> DangerObjectivePairs(List<object?> list)
    {
        var pairs = new List<(string, List<object?>?, int)>();
        for (int i = 0; i < list.Count;)
        {
            if (list[i] is not string key)
            {
                i++;
            }
            else if (i + 1 < list.Count && list[i + 1] is List<object?> value)
            {
                pairs.Add((key, value, i + 1));
                i += 2;
            }
            else
            {
                pairs.Add((key, null, -1));
                i++;
            }
        }

        return pairs;
    }

    private static void ExtractPairs(
        List<object?> list,
        int fileIndex,
        string container,
        string reader,
        ImmutableArray<int> path,
        ImmutableArray<MissionReference>.Builder references)
    {
        bool firstWins = reader == "objectives";
        var first = new HashSet<string>(
            firstWins
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not List<object?> child)
            {
                continue;
            }

            ExtractPairs(
                child,
                fileIndex,
                container,
                reader,
                Append(path, i),
                references);
        }

        for (int i = 0; i + 1 < list.Count; i++)
        {
            if (list[i] is not string key
                || list[i + 1] is not List<object?> values)
            {
                continue;
            }

            bool selected = true;
            if (firstWins)
            {
                selected = first.Add(key);
            }
            ExtractPair(
                key,
                values,
                container,
                fileIndex,
                Append(path, i + 1),
                selected,
                references);
        }
    }

    private static void ExtractPair(
        string key,
        List<object?> values,
        string container,
        int fileIndex,
        ImmutableArray<int> valuePath,
        bool selected,
        ImmutableArray<MissionReference>.Builder references)
    {
        string upper = key.ToUpperInvariant();
        if (upper == "ANIMATION_DEFINITION_FILE")
        {
            AddStrings(
                values,
                ReferenceKind.ReaderPath,
                ReferenceRule.ListedReader,
                container,
                fileIndex,
                valuePath,
                selected,
                references);
            return;
        }

    }

    private static void ExtractGenerators(
        List<object?> root,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        if (root.Count == 0)
        {
            issues.Add(new ReferenceIssue(
                "malformed-generator-reader",
                $"{container} is empty; EnemyGenerators.Load rejects it.",
                ReferenceSeverity.Error,
                Source(container, fileIndex, ImmutableArray<int>.Empty),
                true));
            return;
        }

        if (root[0] is not List<object?> records)
        {
            return;
        }

        for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            if (records[recordIndex] is not List<object?> record)
            {
                continue;
            }

            bool valid = CampaignMissionGenerators.HasRequiredFields(record);
            if (!valid)
            {
                issues.Add(new ReferenceIssue(
                    "malformed-generator-record",
                    string.Create(CultureInfo.InvariantCulture,
                        $"{container} record {recordIndex} requires a node string and a vehicle key."),
                    ReferenceSeverity.Error,
                    Source(container, fileIndex, Path(0, recordIndex)),
                    true));
            }

            if (valid && !HasGeneratorNetName(record))
            {
                issues.Add(new ReferenceIssue(
                    "generator-no-net-names",
                    $"{container} record {recordIndex} has no direct net names; the runtime drops it.",
                    ReferenceSeverity.Warning,
                    Source(container, fileIndex, Path(0, recordIndex)),
                    false));
            }

            string? host = CampaignMissionGenerators.SelectedString(record, "node");
            bool zeppelin = CampaignRawList.LastListValueIndex(record, "zeppelin") != -1;
            foreach ((string key, int index, List<object?>? values) in AlternatingPairs(record))
            {
                if (values == null)
                {
                    continue;
                }

                bool selected = CampaignRawList.LastListValueIndex(record, key) == index;
                ImmutableArray<int> path = Path(0, recordIndex, index);
                switch (key.ToUpperInvariant())
                {
                    case "NODE":
                        AddSlot(values, 0, ReferenceKind.WorldNode, ReferenceRule.GeneratorHost,
                            path, selected, "generator:host");
                        break;
                    case "ORIGIN":
                        AddSlot(values, 0, ReferenceKind.WorldNode,
                            zeppelin ? ReferenceRule.GeneratorChild : ReferenceRule.Unverified,
                            path, selected, "generator:origin", host);
                        break;
                    case "HEALTHY":
                        AddSlot(values, 0, ReferenceKind.WorldNode, ReferenceRule.Unverified,
                            path, selected, "generator:healthy", host);
                        break;
                    case "OPEN_ANIM":
                    case "CLOSE_ANIM":
                        AddSlot(values, 0, ReferenceKind.Animation, ReferenceRule.EffectiveAnimation,
                            path, selected, "generator:door", host);
                        break;
                    case "VEHICLE":
                        ExtractGeneratorVehicle(values, container, fileIndex, path, selected, host, references);
                        break;
                }
            }

            int hostIndex = CampaignMissionGenerators.SelectedStringIndex(record, "node");
            if (hostIndex < 0 || host == null)
            {
                continue;
            }

            ImmutableArray<int> hostPath = Path(0, recordIndex, hostIndex, 0);
            if (CampaignMissionGenerators.SelectedString(record, "open_anim") == null)
            {
                AddGeneratorName(references, ReferenceKind.Animation, ReferenceRule.EffectiveAnimation,
                    EnemyGenerators.DefaultDoorAnim(host), container, fileIndex, hostPath,
                    true, host, "generator:default-door", derived: true);
            }

            if (!zeppelin)
            {
                for (int i = 0; i < 2; i++)
                {
                    AddGeneratorName(references, ReferenceKind.WorldNode, ReferenceRule.GeneratorChild,
                        EnemyGenerators.LaunchPathNode(host, i), container, fileIndex, hostPath,
                        true, host, string.Create(CultureInfo.InvariantCulture, $"generator:takeoff:{i}"),
                        derived: true);
                }
            }

            void AddSlot(
                List<object?> values,
                int slot,
                ReferenceKind kind,
                ReferenceRule rule,
                ImmutableArray<int> path,
                bool selected,
                string context,
                string? anchor = null)
            {
                if ((uint)slot < (uint)values.Count && values[slot] is string name)
                {
                    AddGeneratorName(references, kind, rule, name, container, fileIndex,
                        Append(path, slot), selected, anchor, context);
                }
            }
        }
    }

    private static bool HasGeneratorNetName(List<object?> record)
    {
        int vehicleIndex = CampaignRawList.LastListValueIndex(record, "vehicle");
        if (vehicleIndex < 0 || record[vehicleIndex] is not List<object?> vehicle)
        {
            return false;
        }

        int netsIndex = CampaignRawList.LastListValueIndex(vehicle, "nets");
        if (netsIndex < 0 || vehicle[netsIndex] is not List<object?> nets)
        {
            return false;
        }

        foreach (object? item in nets)
        {
            if (item is string)
            {
                return true;
            }
        }

        return false;
    }

    private static void ExtractGeneratorVehicle(
        List<object?> vehicle,
        string container,
        int fileIndex,
        ImmutableArray<int> path,
        bool selectedVehicle,
        string? host,
        ImmutableArray<MissionReference>.Builder references)
    {
        foreach ((string key, int index, List<object?>? values) in AlternatingPairs(vehicle))
        {
            if (values == null)
            {
                continue;
            }

            bool selected = selectedVehicle && CampaignRawList.LastListValueIndex(vehicle, key) == index;
            ImmutableArray<int> valuePath = Append(path, index);
            if (key.Equals("params", StringComparison.OrdinalIgnoreCase)
                && values.Count > 0 && values[0] is string parameter)
            {
                AddGeneratorName(references, ReferenceKind.Template, ReferenceRule.TemplateName,
                    parameter, container, fileIndex, Append(valuePath, 0),
                    selected, host, "generator:template");
            }
            else if (key.Equals("nets", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] is string net)
                    {
                        AddGeneratorName(references, ReferenceKind.AiNet, ReferenceRule.GeneratorNet,
                            net, container, fileIndex, Append(valuePath, i),
                            selected, host, "generator:net");
                    }
                }
            }
        }
    }

    private static void ExtractObjectiveGenerators(
        List<object?> root,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (root.Count == 0 || root[0] is not List<object?> body)
        {
            return;
        }

        var top = new List<(string Key, int Index, List<object?>? Values)>(AlternatingPairs(body));
        for (int number = 1; number <= body.Count; number++)
        {
            string objective = string.Create(CultureInfo.InvariantCulture, $"OBJECTIVE{number}");
            int first = top.FindIndex(pair => pair.Key == objective);
            if (first < 0)
            {
                break;
            }

            for (int occurrence = first; occurrence < top.Count; occurrence++)
            {
                (string key, int index, List<object?>? block) = top[occurrence];
                if (key != objective || block == null)
                {
                    continue;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach ((string directive, int valueIndex, List<object?>? values) in AlternatingPairs(block))
                {
                    bool selected = occurrence == first && seen.Add(directive);
                    if (values == null)
                    {
                        continue;
                    }

                    int slot = directive switch
                    {
                        "WAKEUP_GENERATOR" => 0,
                        "DEDG" when values.Count >= 2 => 2,
                        _ => -1,
                    };
                    if (slot >= 0 && slot < values.Count && values[slot] is string name)
                    {
                        AddGeneratorName(references, ReferenceKind.Generator, ReferenceRule.GeneratorName,
                            name, container, fileIndex, Path(0, index, valueIndex, slot),
                            selected, null, $"{objective}:{directive}");
                    }
                }
            }
        }
    }

    private static IEnumerable<(string Key, int Index, List<object?>? Values)> AlternatingPairs(
        List<object?> values)
    {
        int i = 0;
        while (i < values.Count)
        {
            if (values[i] is not string key)
            {
                i++;
                continue;
            }

            if (i + 1 < values.Count && values[i + 1] is List<object?> child)
            {
                yield return (key, i + 1, child);
                i += 2;
            }
            else
            {
                yield return (key, -1, null);
                i++;
            }
        }
    }

    private static void AddGeneratorName(
        ImmutableArray<MissionReference>.Builder references,
        ReferenceKind kind,
        ReferenceRule rule,
        string value,
        string container,
        int fileIndex,
        ImmutableArray<int> path,
        bool selected,
        string? host,
        string context,
        bool derived = false)
    {
        references.Add(new MissionReference(
            kind,
            rule,
            new ReferenceValue(value, null, ImmutableArray<string>.Empty),
            new ReferenceSource(ReferenceScope.Mission, container, fileIndex, path,
                ImmutableArray.Create(path), context, derived),
            Context(selected, rule == ReferenceRule.Unverified) with { RelatedName = host }));
    }

    private static void ExtractZeppelins(
        List<object?> root,
        string container,
        int fileIndex,
        ImmutableArray<MissionReference>.Builder references,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        if (root.Count == 0)
        {
            issues.Add(new ReferenceIssue(
                "malformed-zeppelin-reader",
                $"{container} is empty; Zeppelins.Load rejects it.",
                ReferenceSeverity.Error,
                Source(container, fileIndex, ImmutableArray<int>.Empty),
                true));
            return;
        }

        if (root[0] is not List<object?> records)
        {
            return;
        }

        for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
        {
            if (records[recordIndex] is not List<object?> record)
            {
                continue;
            }

            if (!CampaignMissionZeppelins.HasUsableRequiredFields(record))
            {
                issues.Add(new ReferenceIssue(
                    "malformed-zeppelin-record",
                    string.Create(CultureInfo.InvariantCulture,
                        $"{container} record {recordIndex} requires node and net strings and three finite position floats."),
                    ReferenceSeverity.Error,
                    Source(container, fileIndex, Path(0, recordIndex)),
                    true));
            }

            int i = 0;
            while (i < record.Count)
            {
                if (record[i] is not string key)
                {
                    i++;
                    continue;
                }

                if (i + 1 < record.Count && record[i + 1] is List<object?> values)
                {
                    bool selected = CampaignRawList.LastListValueIndex(record, key) == i + 1;
                    ExtractZeppelinPair(
                        key.ToUpperInvariant(),
                        values,
                        record,
                        container,
                        fileIndex,
                        Path(0, recordIndex, i + 1),
                        selected,
                        references);
                    i += 2;
                }
                else
                {
                    i++;
                }
            }
        }
    }

    private static void ExtractZeppelinPair(
        string key,
        List<object?> values,
        List<object?> record,
        string container,
        int fileIndex,
        ImmutableArray<int> valuePath,
        bool selected,
        ImmutableArray<MissionReference>.Builder references)
    {
        switch (key)
        {
            case "NODE":
                AddZeppelinName(values, 0, ReferenceKind.WorldNode, ReferenceRule.GlobalFirst);
                break;
            case "NET":
                AddZeppelinName(values, 0, ReferenceKind.AiNet, ReferenceRule.NetName);
                break;
            case "TARGETS":
                for (int i = 0; i < values.Count; i++)
                {
                    AddZeppelinName(values, i, ReferenceKind.Actor, ReferenceRule.RuntimeActor, true);
                }

                break;
            case "ENGINES":
                for (int i = 0; i < values.Count; i++)
                {
                    AddZeppelinName(values, i, ReferenceKind.Unknown, ReferenceRule.Unverified, true);
                }

                break;
            case "HEALTHY":
                var seenHealthy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] is List<object?> { Count: >= 2 } pair
                        && pair[0] is string name && pair[1] is string)
                    {
                        AddPart(pair, i, 0);
                        AddPart(pair, i, 1, seenHealthy.Add(name));
                    }
                }

                break;
            case "GASBAGS":
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] is List<object?> { Count: >= 2 } bag
                        && bag[0] is string name && bag[1] is float)
                    {
                        bool last = CampaignMissionZeppelins.IsLastGasbag(values, i, name);
                        bool matched = CampaignMissionZeppelins.HasHealthyNode(record, name);
                        AddPart(bag, i, 0, last);
                        AddAnimationList(bag, i, 2, last,
                            matched ? null : UnmatchedZeppelinGasbag);
                    }
                }

                break;
            case "LEFT_CANNONS":
            case "RIGHT_CANNONS":
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] is List<object?> { Count: >= 3 } cannon
                        && cannon[0] is string && cannon[1] is string && cannon[2] is string)
                    {
                        AddPart(cannon, i, 0);
                        AddAnimation(cannon[1], Append(valuePath, i, 1));
                        AddAnimation(cannon[2], Append(valuePath, i, 2));
                    }
                }

                break;
            case "CANNON_HEALTH":
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] is not List<object?> { Count: >= 7 } cannon
                        || cannon[0] is not string || cannon[3] is not string
                        || cannon[4] is not float)
                    {
                        continue;
                    }

                    AddPart(cannon, i, 0);
                    AddPart(cannon, i, 3);
                    AddAnimationList(cannon, i, 5);
                    if (cannon[6] is List<object?> stages)
                    {
                        for (int j = 0; j < stages.Count; j++)
                        {
                            if (stages[j] is List<object?> { Count: >= 2 } stage
                                && stage[0] is float && stage[1] is string)
                            {
                                AddAnimation(stage[1], Append(valuePath, i, 6, j, 1));
                            }
                        }
                    }
                }

                break;
        }

        void AddZeppelinName(
            List<object?> list, int index, ReferenceKind kind, ReferenceRule rule, bool needsRuntime = false)
        {
            if ((uint)index < (uint)list.Count && list[index] is string name)
            {
                AddName(references, kind, rule, name, container, fileIndex,
                    Append(valuePath, index), selected, needsRuntime);
            }
        }

        void AddPart(List<object?> list, int entryIndex, int index, bool effective = true) =>
            AddName(references, ReferenceKind.Unknown, ReferenceRule.Unverified,
                (string)list[index]!, container, fileIndex,
                Append(valuePath, entryIndex, index), selected && effective, needsRuntime: true);

        void AddAnimation(object? value, ImmutableArray<int> path, bool effective = true, string? context = null)
        {
            if (value is string name)
            {
                AddName(references, ReferenceKind.Animation, ReferenceRule.EffectiveAnimation,
                    name, container, fileIndex, path, selected && effective, context: context);
            }
        }

        void AddAnimationList(
            List<object?> list, int entryIndex, int index, bool effective = true, string? context = null)
        {
            if (list.Count > index && list[index] is List<object?> { Count: > 0 } anims)
            {
                AddAnimation(anims[0], Append(valuePath, entryIndex, index, 0), effective, context);
            }
        }
    }

    private static void ExtractPickups(
        List<object?> list,
        int fileIndex,
        string container,
        ImmutableArray<int> path,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (list.Count >= 2
            && list[0] is string node
            && list[1] is float)
        {
            AddName(
                references,
                ReferenceKind.Pickup,
                ReferenceRule.PickupNode,
                node,
                container,
                fileIndex,
                Append(path, 0),
                needsRuntime: true);
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is List<object?> child)
            {
                ExtractPickups(
                    child,
                    fileIndex,
                    container,
                    Append(path, i),
                    references);
            }
        }
    }

    private static void ExtractCompiled(
        CampaignDependency<AnimArchive?> dependency,
        ReferenceScope scope,
        ImmutableArray<MissionReference>.Builder references,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        if (dependency.Availability.State != CampaignDependencyState.Ready)
        {
            if (dependency.Availability.State
                is not CampaignDependencyState.Missing)
            {
                issues.Add(
                    new ReferenceIssue(
                        "animation-coverage",
                        $"{dependency.Availability.Path}: compiled animation references are unavailable.",
                        ReferenceSeverity.Warning,
                        null,
                        false));
            }

            return;
        }

        AnimArchive? archive = dependency.Value;
        if (archive == null)
        {
            return;
        }

        if (archive.Defs.Count != 0)
        {
            issues.Add(
                new ReferenceIssue(
                    "animation-event-coverage",
                    $"{archive.Label}: compiled event payloads that require live binding remain unverified.",
                    ReferenceSeverity.Info,
                    null,
                    false));
        }

        for (int i = 0; i < archive.Defs.Count; i++)
        {
            AnimDefinition definition = archive.Defs[i];
            string container = $"{archive.Label}::{definition.AnimName}";
            AddCompiledName(
                definition.RootName,
                ReferenceKind.WorldNode,
                ReferenceRule.GlobalPattern,
                container,
                scope,
                i,
                references);
            foreach (string animation in definition.PrereqAnims)
            {
                AddCompiledName(
                    animation,
                    ReferenceKind.Animation,
                    ReferenceRule.EffectiveAnimation,
                    container,
                    scope,
                    i,
                    references);
            }

            foreach (AnimNodePrereq prerequisite in definition.PrereqNodes)
            {
                if (prerequisite.Path.Count == 0)
                {
                    continue;
                }

                AddCompiledName(
                    prerequisite.Path[^1],
                    ReferenceKind.WorldNode,
                    ReferenceRule.GlobalPattern,
                    container,
                    scope,
                    i,
                    references);
            }
        }
    }

    private static void ExtractSetup(
        CampaignMissionDependencyBundle bundle,
        ImmutableArray<MissionReference>.Builder references,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        if (bundle.Setup.Availability.State != CampaignDependencyState.Ready)
        {
            if (bundle.Setup.Availability.State
                is not CampaignDependencyState.Missing
                and not CampaignDependencyState.NotChecked)
            {
                issues.Add(
                    new ReferenceIssue(
                        "setup-coverage",
                        $"{bundle.Setup.Availability.Path}: setup references are unavailable.",
                        ReferenceSeverity.Warning,
                        null,
                        false));
            }

            return;
        }

        MissionSetup? setup = bundle.Setup.Value;
        if (setup == null)
        {
            return;
        }

        for (int i = 0; i < setup.Ops.Count; i++)
        {
            MissionSetup.Op op = setup.Ops[i];
            if (!string.IsNullOrWhiteSpace(op.Target))
            {
                AddName(
                    references,
                    ReferenceKind.Unknown,
                    ReferenceRule.Unverified,
                    op.Target,
                    setup.ScriptName,
                    null,
                    ImmutableArray<int>.Empty,
                    needsRuntime: true,
                    scope: ReferenceScope.Setup,
                    context: $"{i}: {op.Verb}");
            }
        }
    }

    private static void AddCoverageIssues(
        ZrdrSet editableMission,
        ImmutableArray<ReferenceIssue>.Builder issues)
    {
        bool objectives = false;
        foreach (ZrdrFile file in editableMission.Files)
        {
            string reader = ReaderName(file.Key);
            objectives |= reader == "objectives";
            if (reader is "map" or "location")
            {
                issues.Add(
                    new ReferenceIssue(
                        "unverified-reader-family",
                        $"{file.Key}: no current runtime consumer establishes reference semantics.",
                        ReferenceSeverity.Info,
                        null,
                        false));
            }

            if (reader == "weather")
            {
                issues.Add(
                    new ReferenceIssue(
                        "weather-geometry",
                        "weather.zrd: chapter horizon and fog-volume joins require runtime geometry.",
                        ReferenceSeverity.Info,
                        null,
                        false));
            }

            if (reader == "zeppelins")
            {
                issues.Add(
                    new ReferenceIssue(
                        "zeppelin-runtime-binding",
                        "zeppelins.zrd: live broadside targets and derived part bindings remain unverified.",
                        ReferenceSeverity.Info,
                        null,
                        false));
            }

            if (reader == "dzones")
            {
                issues.Add(
                    new ReferenceIssue(
                        "danger-zone-geometry",
                        "dzones.zrd: gate checks use available source geometry; setup and live arming remain unverified.",
                        ReferenceSeverity.Info,
                        null,
                        false));
            }

            if (reader == "pickups")
            {
                issues.Add(
                    new ReferenceIssue(
                        "pickup-runtime-binding",
                        "pickups.zrd: dynamically staged pickup hosts remain unverified.",
                        ReferenceSeverity.Info,
                        null,
                        false));
            }
        }

        if (objectives)
        {
            issues.Add(
                new ReferenceIssue(
                    "briefing-asset-coverage",
                    "Mission identity keys are checked; briefing rows, narration, and art assets remain unverified.",
                    ReferenceSeverity.Info,
                    null,
                    false));
        }
    }

    private static void AddCompiledName(
        string? value,
        ReferenceKind kind,
        ReferenceRule rule,
        string container,
        ReferenceScope scope,
        int definitionIndex,
        ImmutableArray<MissionReference>.Builder references)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddName(
            references,
            kind,
            rule,
            value,
            container,
            null,
            ImmutableArray<int>.Empty,
            scope: scope,
            context: $"definition {definitionIndex}");
    }

    private static void AddFieldName(
        List<object?> fields,
        int index,
        ReferenceKind kind,
        ReferenceRule rule,
        string container,
        int fileIndex,
        ImmutableArray<int> fieldsPath,
        ImmutableArray<MissionReference>.Builder references,
        bool needsRuntime = false)
    {
        if ((uint)index < (uint)fields.Count
            && fields[index] is string value)
        {
            AddName(
                references,
                kind,
                rule,
                value,
                container,
                fileIndex,
                Append(fieldsPath, index),
                needsRuntime: needsRuntime);
        }
    }

    private static void AddFieldNumber(
        List<object?> fields,
        int index,
        ReferenceKind kind,
        ReferenceRule rule,
        string container,
        int fileIndex,
        ImmutableArray<int> fieldsPath,
        ImmutableArray<MissionReference>.Builder references,
        bool skipNegative = false)
    {
        if ((uint)index < (uint)fields.Count
            && fields[index] is float value)
        {
            AddNumber(
                references,
                kind,
                rule,
                value,
                container,
                fileIndex,
                Append(fieldsPath, index),
                skipNegative);
        }
    }

    private static void AddStrings(
        List<object?> values,
        ReferenceKind kind,
        ReferenceRule rule,
        string container,
        int fileIndex,
        ImmutableArray<int> valuePath,
        bool selected,
        ImmutableArray<MissionReference>.Builder references,
        bool needsRuntime = false,
        bool firstOnly = false,
        bool lastOnly = false)
    {
        List<(string Value, ImmutableArray<int> Path)> strings =
            Strings(values, valuePath);
        if (firstOnly && strings.Count > 1)
        {
            strings.RemoveRange(1, strings.Count - 1);
        }
        else if (lastOnly && strings.Count > 1)
        {
            strings.RemoveRange(0, strings.Count - 1);
        }

        foreach ((string value, ImmutableArray<int> path) in strings)
        {
            AddName(
                references,
                kind,
                rule,
                value,
                container,
                fileIndex,
                path,
                selected,
                needsRuntime);
        }
    }

    private static void AddNumber(
        ImmutableArray<MissionReference>.Builder references,
        ReferenceKind kind,
        ReferenceRule rule,
        float value,
        string container,
        int fileIndex,
        ImmutableArray<int> path,
        bool skipNegative = false,
        bool skipZero = false,
        bool selected = true)
    {
        if (!float.IsFinite(value)
            || value != MathF.Truncate(value)
            || (double)value < long.MinValue
            || (double)value >= -(double)long.MinValue
            || (skipNegative && value < 0f)
            || (skipZero && value == 0f))
        {
            return;
        }

        references.Add(
            new MissionReference(
                kind,
                rule,
                new ReferenceValue(
                    null,
                    checked((long)value),
                    ImmutableArray<string>.Empty),
                Source(container, fileIndex, path),
                Context(selected)));
    }

    private static void AddName(
        ImmutableArray<MissionReference>.Builder references,
        ReferenceKind kind,
        ReferenceRule rule,
        string? value,
        string container,
        int? fileIndex,
        ImmutableArray<int> path,
        bool selected = true,
        bool needsRuntime = false,
        bool derived = false,
        ReferenceScope scope = ReferenceScope.Mission,
        string? context = null,
        string? suppression = null,
        bool allowEmpty = false)
    {
        if (value == null || (!allowEmpty && string.IsNullOrWhiteSpace(value)))
        {
            return;
        }

        references.Add(
            new MissionReference(
                kind,
                rule,
                new ReferenceValue(
                    value,
                    null,
                    ImmutableArray<string>.Empty),
                new ReferenceSource(
                    scope,
                    container,
                    fileIndex,
                    path,
                    path.IsDefaultOrEmpty
                        ? ImmutableArray<ImmutableArray<int>>.Empty
                        : ImmutableArray.Create(path),
                    context,
                    derived),
                Context(selected, needsRuntime) with
                {
                    DefinitionKey = selected && suppression != null
                        ? suppression
                        : selected ? null : "suppressed",
                }));
    }

    private static ReferenceContext Context(
        bool selected,
        bool needsRuntime = false) =>
        new(
            ImmutableArray<string>.Empty,
            null,
            null,
            null,
            selected ? null : "suppressed",
            needsRuntime);

    private static ReferenceSource Source(
        string container,
        int fileIndex,
        ImmutableArray<int> path) =>
        new(
            ReferenceScope.Mission,
            container,
            fileIndex,
            path,
            path.IsDefaultOrEmpty
                ? ImmutableArray<ImmutableArray<int>>.Empty
                : ImmutableArray.Create(path),
            null,
            false);

    private static List<(string Value, ImmutableArray<int> Path)> Strings(
        List<object?> values,
        ImmutableArray<int> path)
    {
        var result =
            new List<(string Value, ImmutableArray<int> Path)>();
        Strings(values, path, result);
        return result;
    }

    private static void Strings(
        List<object?> values,
        ImmutableArray<int> path,
        List<(string Value, ImmutableArray<int> Path)> result)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] is string value && value.Length != 0)
            {
                result.Add((value, Append(path, i)));
            }
            else if (values[i] is List<object?> child)
            {
                Strings(child, Append(path, i), result);
            }
        }
    }

    private static string ReaderName(string key)
    {
        string name = key;
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".json".Length];
        }

        if (name.EndsWith(".zrd", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".zrd".Length];
        }

        return name.ToLowerInvariant();
    }

    private static ImmutableArray<int> Path(params int[] values) =>
        ImmutableArray.Create(values);

    private static ImmutableArray<int> Append(
        ImmutableArray<int> path,
        params int[] values)
    {
        var builder =
            ImmutableArray.CreateBuilder<int>(path.Length + values.Length);
        builder.AddRange(path);
        builder.AddRange(values);
        return builder.MoveToImmutable();
    }
}

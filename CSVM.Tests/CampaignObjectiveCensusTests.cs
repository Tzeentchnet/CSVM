using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CSVM.Mech3;
using CSVM.Session.Objectives;
using CSVM.Tooling;
using Xunit;
using Xunit.Abstractions;

namespace CSVM.Tests;

/// <summary>Metadata-only inventory of story objective directives through the reader APIs.</summary>
[Trait("Tier", "Data")]
public sealed class CampaignObjectiveCensusTests
{
    private readonly ITestOutputHelper _output;

    public CampaignObjectiveCensusTests(ITestOutputHelper output) => _output = output;

    [GameFilesFact]
    public void InstalledStoryDirectiveShapesAndSelection()
    {
        var source = GameFilesData.Source!;
        Census(
            CampaignMissionDocument.LoadMissions(source),
            mission => ZrdrSet.Load(source, CampaignMissionDocument.ArchivePath(mission)));
    }

    [ExtractedDataFact]
    public void ExtractedStoryDirectiveShapesAndSelection() =>
        Census(
            CampaignSequence.Load(TestZrdr.Root),
            mission => TestZrdr.Mission(mission.ChapterFolder, mission.MissionFolder));

    private void Census(
        IReadOnlyList<CampaignMission> missions,
        Func<CampaignMission, ZrdrSet> load)
    {
        var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int objectives = 0;
        int duplicateBlocks = 0;
        int duplicateIdentities = 0;
        int nullBlocks = 0;
        int afterGaps = 0;
        int noncanonicalBlocks = 0;
        int duplicateDirectives = 0;
        int bareFirstSuppressions = 0;
        int wakeAliasPairs = 0;
        int runtimePriorityCollisions = 0;
        int briefingLines = 0;
        int briefingPriorityCollisions = 0;
        foreach (CampaignMission mission in missions)
        {
            ZrdrSet scope = load(mission);
            Assert.True(scope.Exists);
            List<object?> root = scope.LoadFileOrEmpty("objectives.json");
            ObjectiveScript script = ObjectiveScript.Load(scope);
            Assert.NotEmpty(root);
            var body = Assert.IsType<List<object?>>(root[0]);
            List<(string Key, List<object?>? Value)> pairs = Pairs(body);
            var first = new Dictionary<string, List<object?>?>(StringComparer.Ordinal);
            var briefingPriorities = new HashSet<int>();
            foreach ((string key, List<object?>? value) in pairs)
            {
                if (!first.TryAdd(key, value)
                    && CampaignMissionObjectives.TryObjectiveNumber(key, out _))
                {
                    duplicateBlocks++;
                }

                if (key.StartsWith("OBJECTIVE", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(key.AsSpan("OBJECTIVE".Length), NumberStyles.None,
                        CultureInfo.InvariantCulture, out _)
                    && !CampaignMissionObjectives.TryObjectiveNumber(key, out _))
                {
                    noncanonicalBlocks++;
                }

                if (key.StartsWith("OBJECTIVE", StringComparison.OrdinalIgnoreCase) && value != null)
                {
                    foreach ((string field, List<object?>? entry) in Pairs(value))
                    {
                        if (field.Equals("IDENTITY", StringComparison.OrdinalIgnoreCase)
                            && entry is { Count: >= 3 } && entry[1] is float priority
                            && float.IsFinite(priority) && entry[2] is string { Length: > 0 })
                        {
                            briefingLines++;
                            if (!briefingPriorities.Add((int)priority))
                            {
                                briefingPriorityCollisions++;
                            }
                        }
                    }
                }
            }

            var runtimePriorities = new HashSet<int>();
            for (int i = 0; i < script.Objectives.Count; i++)
            {
                int number = i + 1;
                string key = "OBJECTIVE" + number.ToString(CultureInfo.InvariantCulture);
                Assert.True(first.TryGetValue(key, out List<object?>? directives));
                Assert.Equal(number, script.Objectives[i].Number);
                objectives++;
                if (script.Objectives[i].Identity is { } identity
                    && !runtimePriorities.Add(identity.Priority))
                {
                    runtimePriorityCollisions++;
                }

                if (directives == null)
                {
                    nullBlocks++;
                    continue;
                }

                int identities = 0;
                var firstDirectives = new Dictionary<string, List<object?>?>(StringComparer.Ordinal);
                foreach ((string directive, List<object?>? values) in Pairs(directives))
                {
                    if (directive == "IDENTITY")
                    {
                        identities++;
                    }

                    if (!firstDirectives.TryAdd(directive, values))
                    {
                        duplicateDirectives++;
                        if (firstDirectives[directive] == null)
                        {
                            bareFirstSuppressions++;
                        }
                    }

                    string family = CampaignMissionObjectives.IsKnownDirective(directive)
                        ? directive
                        : "<unrecognized>";
                    string shape = family + ":" + Shape(values);
                    shapes[shape] = shapes.GetValueOrDefault(shape) + 1;
                }

                duplicateIdentities += Math.Max(0, identities - 1);
                if (firstDirectives.ContainsKey("WAKE_OBJECTIVE_WHEN_I_COMPLETE")
                    && firstDirectives.ContainsKey("WAKE_OBJECTIVE"))
                {
                    wakeAliasPairs++;
                }
            }

            Assert.False(first.ContainsKey(
                "OBJECTIVE" + (script.Objectives.Count + 1).ToString(CultureInfo.InvariantCulture)));
            foreach (var pair in pairs)
            {
                if (CampaignMissionObjectives.TryObjectiveNumber(pair.Key, out int number)
                    && number > script.Objectives.Count)
                {
                    afterGaps++;
                }
            }
        }

        Assert.Equal(24, missions.Count);
        Assert.True(objectives > 0);
        _output.WriteLine(
            $"Missions={missions.Count}, objectives={objectives}, null={nullBlocks}, "
            + $"duplicate blocks={duplicateBlocks}, duplicate identities={duplicateIdentities}, "
            + $"after gaps={afterGaps}, noncanonical={noncanonicalBlocks}.");
        _output.WriteLine(
            $"Duplicate directives={duplicateDirectives}, bare suppressions={bareFirstSuppressions}, "
            + $"wake aliases={wakeAliasPairs}, runtime priority collisions={runtimePriorityCollisions}, "
            + $"briefing lines={briefingLines}, briefing priority collisions={briefingPriorityCollisions}.");
        foreach ((string shape, int count) in shapes)
        {
            _output.WriteLine($"{shape}: {count}");
        }
    }

    private List<(string Key, List<object?>? Value)> Pairs(List<object?> values)
    {
        var result = new List<(string, List<object?>?)>();
        for (int i = 0; i < values.Count;)
        {
            if (values[i] is not string key)
            {
                i++;
            }
            else if (i + 1 < values.Count && values[i + 1] is List<object?> list)
            {
                result.Add((key, list));
                i += 2;
            }
            else
            {
                result.Add((key, null));
                i++;
            }
        }

        return result;
    }

    private string Shape(List<object?>? values) =>
        values == null
            ? "bare"
            : string.Join("", values.Select(value => value switch
            {
                string => "S",
                float => "F",
                List<object?> nested => "[" + Shape(nested) + "]",
                null => "N",
                _ => "?",
            }));
}

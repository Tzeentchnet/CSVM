using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Mech3;
using CSVM.Tooling;
using Xunit;
using Xunit.Abstractions;

namespace CSVM.Tests;

/// <summary>Metadata-only roster shape and duplicate census over story missions.</summary>
[Trait("Tier", "Data")]
public sealed class CampaignRosterFieldsCensusTests
{
    private readonly ITestOutputHelper _output;

    public CampaignRosterFieldsCensusTests(ITestOutputHelper output) => _output = output;

    [GameFilesFact]
    public void InstalledStoryRosterShapesAndDuplicates()
    {
        var source = GameFilesData.Source!;
        Census(CampaignMissionDocument.LoadMissions(source),
            mission => ZrdrSet.Load(source, CampaignMissionDocument.ArchivePath(mission)));
    }

    [ExtractedDataFact]
    public void ExtractedStoryRosterShapesAndDuplicates() =>
        Census(CampaignSequence.Load(TestZrdr.Root),
            mission => TestZrdr.Mission(mission.ChapterFolder, mission.MissionFolder));

    private void Census(IReadOnlyList<CampaignMission> missions, Func<CampaignMission, ZrdrSet> load)
    {
        var widths = new SortedDictionary<int, int>();
        var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int blocks = 0;
        int absentReaders = 0;
        int emptyReaders = 0;
        int duplicateReaders = 0;
        int duplicateNames = 0;
        int duplicateLabels = 0;
        int scalarNets = 0;
        int listNets = 0;
        int otherNets = 0;
        foreach (CampaignMission mission in missions)
        {
            ZrdrSet scope = load(mission);
            Assert.True(scope.Exists);
            ZrdrFile? selected = ZrdrZbd.Find(scope.Files, "aiv.json");
            if (selected == null)
            {
                absentReaders++;
                continue;
            }

            duplicateReaders += scope.Files.Count(file => ZrdrZbd.Find(new[] { file }, "aiv.json") != null) - 1;
            if (selected.Root.Count == 0)
            {
                emptyReaders++;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, List<object?> fields) in AiSkills.LoadRoster(scope))
            {
                blocks++;
                if (!names.Add(name))
                {
                    duplicateNames++;
                }

                widths[fields.Count] = widths.GetValueOrDefault(fields.Count) + 1;
                if (fields.Count == 0)
                {
                    otherNets++;
                }
                else if (fields[0] is float)
                {
                    scalarNets++;
                }
                else if (fields[0] is List<object?>)
                {
                    listNets++;
                }
                else
                {
                    otherNets++;
                }

                for (int slot = 0; slot < fields.Count; slot++)
                {
                    string shape = fields[slot] switch
                    {
                        float => "number",
                        string => "string",
                        List<object?> => "list",
                        null => "null",
                        _ => "other",
                    };
                    string key = $"{slot:D2}:{shape}";
                    shapes[key] = shapes.GetValueOrDefault(key) + 1;
                }
            }

            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in AiSkills.LoadGeneratorRoster(scope))
            {
                if (!labels.Add(template.Parameter))
                {
                    duplicateLabels++;
                }
            }
        }

        Assert.Equal(24, missions.Count);
        Assert.True(blocks > 0);
        _output.WriteLine(
            $"Missions={missions.Count}, blocks={blocks}, absent/empty readers={absentReaders}/{emptyReaders}, "
            + $"duplicate readers={duplicateReaders}, "
            + $"duplicate names={duplicateNames}, duplicate disabled labels={duplicateLabels}, "
            + $"net scalar/list/other={scalarNets}/{listNets}/{otherNets}.");
        foreach ((int width, int count) in widths)
        {
            _output.WriteLine($"Width {width}: {count}");
        }

        foreach ((string shape, int count) in shapes)
        {
            _output.WriteLine($"{shape}: {count}");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Session.Objectives;
using CSVM.Spec;
using CSVM.Tooling;
using CSVM.UI.Menu;
using CSVM.UI.Overlays;
using Xunit;
using Xunit.Abstractions;

namespace CSVM.Tests;

/// <summary>Evidence for keeping mission location records in the raw editor.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionLocationTests
{
    private static readonly CampaignMission Mission = new(0, "Probe", 6, 1, "Probe", false);

    [Fact]
    public void NoExplicitLocationFilenameOrDirectStemLookupExists()
    {
        string src = Path.Combine(TestData.RepoRoot, "CSVM", "src");
        Assert.True(Directory.Exists(src), src);
        const string fileName = "\"location(?:\\.(?:zrd(?:\\.json)?|json))?\"";
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var literalFile = new Regex("\"location\\.(?:zrd(?:\\.json)?|json)\"", options);
        var direct = new Regex(
            @"\b(?:LoadFile|LoadFileOrEmpty|HasFile|FindFileIndex)\s*\(\s*" + fileName,
            options);
        var qualified = new Regex(
            @"\b(?:ZrdrZbd\.Find|Zrdr\.(?:LoadFile|LoadFileOrEmpty))\s*\([^,]+,\s*" + fileName,
            options);
        var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                string text = File.ReadAllText(file);
                return literalFile.IsMatch(text) || direct.IsMatch(text) || qualified.IsMatch(text);
            })
            .Select(file => Path.GetRelativePath(src, file))
            .ToArray();

        Assert.Empty(hits);
    }

    [Fact]
    public void SpawnObjectivesBriefingAndMapUseTheirOwnInputs()
    {
        ZrdrFile objectives = Objectives(11f, 101f, 201f, "Anchor", "MSG_ONE");
        var scopes = new[]
        {
            Scope(objectives),
            Scope(objectives, new ZrdrFile("location.zrd", null, Row())),
            Scope(objectives, Location(900f)),
            Scope(objectives, Location(-900f)),
        };
        ZrdrSet shared = Shared("brief-map");

        foreach (ZrdrSet scope in scopes)
        {
            PlayerStart start = Assert.IsType<PlayerStart>(SpawnPoints.LoadPlayerInit(scope));
            Assert.Equal(11f, start.Spawn.Position.X);
            Assert.Equal(44f, start.Spawn.HeadingDeg);
            Assert.Equal(0.8f, start.ThrottleFrac);
            Assert.Equal(18f, start.SpeedMps);

            List<ObjectiveDef> entries = ObjectiveScript.Load(scope).Objectives;
            Assert.Equal(2, entries.Count);
            Assert.Equal(new[] { 101f, 102f, 103f }, entries[0].Travelers!.WherePoint);
            Assert.Equal(new WarpPoint(201f, 202f, 203f, 204f, null), Assert.Single(entries[0].Warp!.Value.Points));
            Assert.Equal("Anchor", entries[1].Travelers!.WhereNode);
            Assert.Null(entries[1].Travelers!.WherePoint);
            Assert.Equal(new WarpPoint(301f, 302f, 303f, 304f, "Anchor"), Assert.Single(entries[1].Warp!.Value.Points));

            CampaignBriefing briefing = Briefing(shared, scope);
            Assert.Equal("brief-map", briefing.State!.Background);
            Assert.Equal("invented.wav", briefing.NarrationWav);
            Assert.Equal(new BriefingPoint(40f, 50f), briefing.State.Steps[1].At);
            Assert.Equal("MSG_ONE", Assert.Single(briefing.Objectives).Key);
            Assert.NotNull(briefing.Reveal);
        }

        ZrdrSet changed = Scope(
            Objectives(91f, 111f, 211f, "OtherNode", "MSG_OTHER"),
            Location(900f));
        Assert.Equal(91f, SpawnPoints.LoadPlayerInit(changed)!.Value.Spawn.Position.X);
        List<ObjectiveDef> changedEntries = ObjectiveScript.Load(changed).Objectives;
        Assert.Equal(111f, changedEntries[0].Travelers!.WherePoint![0]);
        Assert.Equal(211f, Assert.Single(changedEntries[0].Warp!.Value.Points).X);
        Assert.Equal("OtherNode", changedEntries[1].Travelers!.WhereNode);
        Assert.Equal("MSG_OTHER", Assert.Single(Briefing(shared, changed).Objectives).Key);
        Assert.Equal("other-map", Briefing(Shared("other-map"), changed).State!.Background);

        var map = new EscapeMap(
            "chart",
            new BriefingPoint(10f, 20f),
            new EscapeRect(0, 0, 100, 100),
            new EscapeRect(0, 0, 100, 100));
        var icon = MissionMap.Icon(map, "pin", 25f, -50f);
        Assert.NotNull(icon);
        Assert.Equal(35f, icon.X);
        Assert.Equal(70f, icon.Y);
        Assert.NotEqual(icon, MissionMap.Icon(map, "pin", 50f, -50f));
    }

    [Fact]
    public void LocationDataDoesNotSubstituteForMissingObjectives()
    {
        ZrdrSet scope = Scope(Location(900f));

        Assert.Null(SpawnPoints.LoadPlayerInit(scope));
        Assert.Empty(ObjectiveScript.Load(scope).Objectives);
        CampaignBriefing briefing = Briefing(Shared("brief-map"), scope);
        Assert.Equal("brief-map", briefing.State!.Background);
        Assert.Empty(briefing.Objectives);
    }

    [Fact]
    public void AnimationContentScanningIsIndependentOfTheReaderName()
    {
        Assert.Empty(AnimDefs.LoadArchive(Scope(Location(900f))));
        Assert.Empty(AnimDefs.LoadArchive(Scope(new ZrdrFile("location.zrd", null, Row()))));

        var animation = new ZrdrFile(
            "location.zrd",
            null,
            Row(Row(
                "ANIMATION_DEFINITIONS", Row(
                    "ANIMATION_LIST", Row(
                        "ANIMATION_DEFINITION", Row(
                            "NAME", Row("actor"),
                            "ANIMATION_NAME", Row("invented_anim")))))));
        ZrdrSet scope = Scope(animation);
        AnimDefinition definition = Assert.Single(AnimDefs.LoadArchive(scope));
        Assert.Equal("location.zrd.json", definition.SourceFile);
        Assert.Equal("actor", definition.Name);
        Assert.Equal("invented_anim", definition.AnimName);
        Assert.Empty(MissionCutscenes.AnimNames(scope));
        var index = new ZrdrFile(
            "mis_anim.zrd",
            null,
            Row(Row(
                "ANIMATION_DEFINITION_FILE",
                Row(@"..\data\c3\m01\zrdr\cutscenes\location.zrd"))));
        Assert.Equal("invented_anim", Assert.Single(MissionCutscenes.AnimNames(Scope(index, animation))));

        AnimProgram readerOnly = AnimProgram.Load(
            ZrdrSet.None, ZrdrSet.None, scope, null, null);
        Assert.Single(readerOnly.ByAnimName("invented_anim"));

        AnimArchive emptyManifest = AnimArchive.FromDecoded(
            "mission",
            new List<AnimDefinition>(),
            new List<(string Name, SiScript Script)>());
        AnimProgram gated = AnimProgram.Load(
            ZrdrSet.None, ZrdrSet.None, scope, null, emptyManifest);
        Assert.Empty(gated.ByAnimName("invented_anim"));
        Assert.Contains("actor/invented_anim", gated.MissionLibrarySkipped);
    }

    [Fact]
    public void RawLocationEditsRetainTheInformationalCoverageIssue()
    {
        CampaignMissionDocument document = Document(Location(900f));
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());

        ReferenceSnapshot before = diagnostics.Current;
        AssertLocationIssue(before);
        Assert.DoesNotContain(before.References, item => item.Reference.Source.Container == "location.zrd");
        Assert.True(document.SetLeaf(0, new[] { 0, 1, 1, 0 }, 901f));

        ReferenceSnapshot after = diagnostics.Current;
        Assert.Equal(1L, after.DocumentRevision);
        AssertLocationIssue(after);
        Assert.DoesNotContain(after.References, item => item.Reference.Source.Container == "location.zrd");
    }

    [Fact]
    public void GenericReferenceExtractionAndIndexingAlsoInspectLocationContent()
    {
        var location = new ZrdrFile(
            "location.zrd",
            null,
            Row(Row(
                "ANIMATION_DEFINITION_FILE", Row("invented.zrd"),
                "ANIM_NAME", Row("indexed_anim"))));
        var objectives = new ZrdrFile(
            "objectives.zrd",
            null,
            Row(Row("OBJECTIVE1", Row("WAKE_ANIM", Row("indexed_anim")))));
        CampaignMissionDocument document = Document(location, objectives);
        using var diagnostics = new CampaignReferenceDiagnostics(document, Bundle());

        ReferenceSnapshot snapshot = diagnostics.Current;
        AssertLocationIssue(snapshot);
        Assert.Single(snapshot.References.Where(item =>
            item.Reference.Source.Container == "location.zrd"
            && item.Reference.Rule == ReferenceRule.ListedReader
            && item.Reference.Value.Name == "invented.zrd"));
        CheckedReference candidate = Assert.Single(snapshot.References.Where(item =>
            item.Reference.Rule == ReferenceRule.EffectiveAnimation
            && item.Reference.Value.Name == "indexed_anim"));
        Assert.Equal(ReferenceState.Resolved, candidate.State);
        Assert.Empty(AnimDefs.LoadArchive(Scope(location, objectives)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("populated")]
    public void NoEditExportPreservesLocationAndTheWholeScope(string locationState)
    {
        bool includeLocation = locationState != "missing";
        bool emptyLocation = locationState == "empty";
        List<object?> emptyRoot = ZrdrZbd.ReadReader(
            new byte[] { 4, 0, 0, 0, 1, 0, 0, 0 },
            "location.zrd");
        Assert.Empty(emptyRoot);
        ZrdrSet missing = Scope();
        Assert.False(missing.HasFile("location.json"));
        Assert.Throws<FileNotFoundException>(() => missing.LoadFileOrEmpty("location.json"));

        float negativeZero = BitConverter.UInt32BitsToSingle(0x80000000u);
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        List<object?> root = emptyLocation
            ? emptyRoot
            : Row(Row(
                "Anchor", Row("POSITION", Row(negativeZero, subnormal, 3f), "tail", Row("keep", null, Row())),
                "Anchor", Row("ROTATION", Row(4f, 5f), "unknown", Row("repeat", "repeat")),
                "loose", null));
        var files = new List<ZrdrFile>
        {
            new ZrdrFile("before.zrd", null, Row("before", Row(negativeZero, subnormal, null))),
        };
        if (includeLocation)
        {
            files.Add(new ZrdrFile("location.zrd", null, root));
        }

        files.Add(new ZrdrFile("repeat.zrd", null, Row("same", "same")));
        files.Add(new ZrdrFile("repeat.zrd", "repeat-1.zrd", Row("after")));
        CampaignMissionDocument document = Document(files.ToArray());
        Assert.Equal(includeLocation ? 1 : -1, document.FindFileIndex("LOCATION.json"));
        if (includeLocation)
        {
            Assert.NotSame(files[1].Root, document.Files[1].Root);
        }

        Assert.False(document.IsDirty);
        Assert.Equal(0L, document.Revision);

        string modRoot = Path.Combine(
            TestData.RepoRoot, ".scratch", $"c25-location-{Guid.NewGuid():N}");
        try
        {
            string output = document.Export(modRoot);
            ZrdrSet reopened = ZrdrSet.FromExtracted(output);
            Assert.True(reopened.Exists);
            Assert.Equal(files.Select(file => file.Key), reopened.Files.Select(file => file.Key));
            for (int i = 0; i < files.Count; i++)
            {
                AssertTree(files[i].Root, document.Files[i].Root);
                AssertTree(files[i].Root, reopened.Files[i].Root);
            }

            Assert.False(document.IsDirty);
            Assert.Equal(0L, document.Revision);
            Assert.Equal(includeLocation, reopened.HasFile("location.zrd.json"));
            if (!includeLocation)
            {
                Assert.Throws<FileNotFoundException>(() => reopened.LoadFileOrEmpty("location.json"));
            }

            if (emptyLocation)
            {
                Assert.Empty(reopened.LoadFileOrEmpty("location.json"));
                Assert.Throws<InvalidDataException>(() => reopened.LoadFile("location.json"));
                using var archive = ZipFile.OpenRead(output);
                using var reader = new StreamReader(archive.GetEntry("location.zrd.json")!.Open());
                Assert.Equal("null", reader.ReadToEnd());

                string jsonDirectory = Path.Combine(modRoot, "empty-json");
                Directory.CreateDirectory(jsonDirectory);
                string jsonFile = Path.Combine(jsonDirectory, "location.zrd.json");
                File.WriteAllText(jsonFile, "[]");
                ZrdrSet emptyArray = ZrdrSet.FromExtracted(jsonDirectory);
                Assert.True(emptyArray.HasFile("location.json"));
                Assert.Empty(emptyArray.LoadFileOrEmpty("location.json"));
                File.WriteAllText(jsonFile, "null");
                ZrdrSet jsonNull = ZrdrSet.FromExtracted(jsonDirectory);
                Assert.True(jsonNull.HasFile("location.json"));
                Assert.Empty(jsonNull.LoadFileOrEmpty("location.json"));
            }
        }
        finally
        {
            if (Directory.Exists(modRoot))
            {
                Directory.Delete(modRoot, recursive: true);
            }
        }
    }

    private static void AssertLocationIssue(ReferenceSnapshot snapshot)
    {
        ReferenceIssue issue = Assert.Single(snapshot.Issues.Where(item =>
            item.Code == "unverified-reader-family"
            && item.Detail.Contains("location.zrd", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(ReferenceSeverity.Info, issue.Severity);
        Assert.False(issue.BlocksReady);
    }

    private static void AssertTree(object? expected, object? actual)
    {
        switch (expected)
        {
            case null:
                Assert.Null(actual);
                break;
            case string text:
                Assert.Equal(text, Assert.IsType<string>(actual));
                break;
            case float number:
                Assert.Equal(
                    BitConverter.SingleToUInt32Bits(number),
                    BitConverter.SingleToUInt32Bits(Assert.IsType<float>(actual)));
                break;
            case List<object?> values:
                var other = Assert.IsType<List<object?>>(actual);
                Assert.Equal(values.Count, other.Count);
                for (int i = 0; i < values.Count; i++)
                {
                    AssertTree(values[i], other[i]);
                }

                break;
            default:
                throw new InvalidOperationException("The invented tree contains an unsupported value.");
        }
    }

    private static ZrdrFile Objectives(float playerX, float travelerX, float warpX, string node, string message) =>
        new("objectives.zrd", null, Row(Row(
            "PLAYER_INIT", Row(1f, Row(playerX, 22f, 33f), Row(0f, 44f, 0f), 0.8f, 180f),
            "OBJECTIVE1", Row(
                "TRAVELERS", Row("pilot", "APPROACHING", Row(travelerX, 102f, 103f), 20f),
                "WARP_VEHICLE", Row("vehicle", Row(warpX, 202f, 203f, 204f)),
                "IDENTITY", Row("PRIMARY", 1f, message)),
            "OBJECTIVE2", Row(
                "TRAVELERS", Row("pilot", "APPROACHING", node, 20f),
                "WARP_VEHICLE", Row("vehicle", Row(301f, 302f, 303f, 304f, "Anchor"))))));

    private static ZrdrFile Location(float x) =>
        new("location.zrd", null, Row(Row(
            "Anchor", Row("POSITION", Row(x, 2f, 3f), "ROTATION", Row(4f, 5f)),
            "PLAYER_INIT", Row(1f, Row(999f, 999f, 999f), Row(0f, 999f, 0f)),
            "OBJECTIVE1", Row("IDENTITY", Row("PRIMARY", 1f, "MSG_WRONG")))));

    private static ZrdrSet Shared(string background) =>
        Scope(
            new ZrdrFile("cm_sequence.zrd", null, Row(Row(Row(
                "seq", Row(0f), "campaign", Row(6f), "mission", Row(1f))))),
            new ZrdrFile("Briefing.zrd", null, Row(Row(
                "brief_c61", Row(
                    "BACKGROUND_IMAGES", Row(Row(background, 0f, 0f)),
                    "SCRIPT", Row(
                        "PlaySound", Row("sound", Row("brief_line")),
                        "Pict", Row("PIN", "bitmap", Row("pin"), "at", Row(40f, 50f))))))),
            new ZrdrFile("sounds.zrd", null, Row(Row(
                "SETS", Row("brief", Row(Row("brief_line", "invented.wav")))))));

    private static CampaignBriefing Briefing(ZrdrSet shared, ZrdrSet mission)
    {
        var data = new GameData
        {
            Zrdr = shared,
            LoadChapter = (chapter, address) => new ChapterData
            {
                Chapter = chapter,
                Mission = address,
                MissionZrdrSource = new Lazy<ZrdrSet>(() => mission),
            },
        };
        return Assert.IsType<CampaignBriefing>(CampaignBriefing.Load(data, new Messages(), null, 0));
    }

    private static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, Scope(files));

    private static ZrdrSet Scope(params ZrdrFile[] files) => new("invented", files);

    private static List<object?> Row(params object?[] values) => new(values);

    private static CampaignMissionDependencyBundle Bundle()
    {
        ZrdrSet shared = Scope();
        ZrdrSet chapter = Scope();
        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            Mission,
            Dependency(shared, CampaignDependencyState.Ready),
            Dependency<GameZ?>(new GameZ(), CampaignDependencyState.Ready),
            Dependency(new TextureSet("textures", Array.Empty<TextureImage>()), CampaignDependencyState.Ready),
            Dependency(chapter, CampaignDependencyState.Ready),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, true),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, true),
            Dependency(new Messages(), CampaignDependencyState.Ready, true),
            Dependency<InterpScripts?>(null, CampaignDependencyState.Missing, true),
            Dependency<MissionSetup?>(null, CampaignDependencyState.NotChecked, true),
            null);
    }

    private static CampaignDependency<T> Dependency<T>(T value, CampaignDependencyState state, bool optional = false) =>
        new(value, new CampaignDependencyAvailability("invented", state, optional));
}

/// <summary>Metadata-only structural censuses of the story location readers.</summary>
[Trait("Tier", "Data")]
public sealed class CampaignMissionLocationCensusTests
{
    private readonly ITestOutputHelper _output;

    public CampaignMissionLocationCensusTests(ITestOutputHelper output) => _output = output;

    [GameFilesFact]
    public void InstalledStoryReaderPresenceAndStructuralShapes()
    {
        var source = GameFilesData.Source!;
        IReadOnlyList<CampaignMission> missions = CampaignMissionDocument.LoadMissions(source);
        Census(missions, mission => ZrdrSet.Load(source, CampaignMissionDocument.ArchivePath(mission)));
    }

    [ExtractedDataFact]
    public void ExtractedStoryReaderPresenceAndStructuralShapes() =>
        Census(
            CampaignSequence.Load(TestZrdr.Root),
            mission => TestZrdr.Mission(mission.ChapterFolder, mission.MissionFolder));

    private void Census(
        IReadOnlyList<CampaignMission> missions,
        Func<CampaignMission, ZrdrSet> load)
    {
        var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var empty = new List<string>();
        var noRecords = new List<string>();
        int present = 0;
        foreach (CampaignMission mission in missions)
        {
            ZrdrSet scope = load(mission);
            Assert.True(scope.Exists, $"Missing mission scope for {mission.ChapterFolder}\\{mission.MissionFolder}.");
            ZrdrFile? file = ZrdrZbd.Find(scope.Files, "location.json");
            if (file == null)
            {
                continue;
            }

            present++;
            List<object?> root = file.Root;
            string missionPath = $"{mission.ChapterFolder}\\{mission.MissionFolder}";
            if (root.Count == 0 || (root.Count == 1 && root[0] is null))
            {
                noRecords.Add(missionPath);
            }

            if (root.Count == 0)
            {
                empty.Add(missionPath);
            }
            else
            {
                CountShapes(root, 0, shapes);
            }
        }

        _output.WriteLine(
            $"Story missions: {missions.Count}; location readers: {present}; "
            + $"no-record roots: {noRecords.Count}; top-level empty roots: {empty.Count}.");
        foreach ((string shape, int count) in shapes)
        {
            _output.WriteLine($"{shape}: {count}");
        }

        Assert.Equal(24, missions.Count);
        Assert.Equal(24, present);
        Assert.Contains(@"c5\m04", noRecords);
        Assert.All(empty, path => Assert.Equal(@"c5\m04", path));
        Assert.NotEmpty(shapes);
    }

    private void CountShapes(
        List<object?> list,
        int depth,
        SortedDictionary<string, int> shapes)
    {
        int strings = 0;
        int numbers = 0;
        int lists = 0;
        int nulls = 0;
        var types = new char[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            switch (list[i])
            {
                case string:
                    types[i] = 's';
                    strings++;
                    break;
                case float:
                    types[i] = 'f';
                    numbers++;
                    break;
                case List<object?> nested:
                    types[i] = 'l';
                    lists++;
                    CountShapes(nested, depth + 1, shapes);
                    break;
                case null:
                    types[i] = 'n';
                    nulls++;
                    break;
                default:
                    throw new InvalidDataException("A reader contained an unexpected value type.");
            }
        }

        string key = FormattableString.Invariant(
            $"depth={depth} width={list.Count} types={new string(types)} strings={strings} numbers={numbers} lists={lists} nulls={nulls}");
        shapes.TryGetValue(key, out int count);
        shapes[key] = count + 1;
    }
}

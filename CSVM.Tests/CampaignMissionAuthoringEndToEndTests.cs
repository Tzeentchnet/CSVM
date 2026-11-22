using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CSVM.Effects;
using CSVM.Flight.Modes;
using CSVM.Flight.Weapons;
using CSVM.Mech3;
using CSVM.Session.Campaign;
using CSVM.Session.Objectives;
using CSVM.Spec;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Complete invented mission authoring, exact preservation, and runtime consumption.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionAuthoringEndToEndTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatrixPreservesTheWholeScopeAndReplaysEveryEdit(bool c5)
    {
        CampaignMission mission = Mission(c5);
        ZrdrSet original = Scope(c5, edited: false);
        CampaignMissionDocument document = CampaignMissionDocument.FromScope(mission, original);

        ExerciseMatrix(document, c5);
    }

    [CampaignAuthoringExternalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalImportEditExportReopenUsesTheRuntimeModPath(bool c5)
    {
        string root = ExternalRoot();
        string run = Path.Combine(root, $"csvm-authoring-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(run));
        Directory.CreateDirectory(run);
        string inputRoot = Path.Combine(run, "input");
        string outputRoot = Path.Combine(run, "output");
        string source = Path.Combine(run, "invented-source");
        CampaignMission mission = Mission(c5);
        ZrdrSet original = Scope(c5, edited: false);
        ZrdrSet expected = Scope(c5, edited: true);

        try
        {
            string input = CampaignMissionDocument.FromScope(mission, original).Export(inputRoot, TestData.RepoRoot);
            CampaignMissionImportResult opened = CampaignMissionImport.Load(mission, original, source, input);
            Assert.Equal(CampaignMissionImportKind.Zip, opened.SourceKind);
            Assert.Empty(opened.AdditionalReaders);
            AssertScope(original.Files, opened.Document.Files);

            ExerciseMatrix(opened.Document, c5);
            string output = opened.Document.Export(outputRoot, TestData.RepoRoot);
            Assert.False(opened.Document.IsDirty);

            using (ZipArchive archive = ZipFile.OpenRead(output))
            {
                Assert.Equal(expected.Files.Select(file => file.Key + ".json"),
                    archive.Entries.Select(entry => entry.FullName));
            }

            CampaignMissionImportResult reopened = CampaignMissionImport.Load(mission, original, source, output);
            Assert.Empty(reopened.AdditionalReaders);
            AssertScope(expected.Files, reopened.Document.Files);
            AssertReopenedViews(reopened.Document, c5);
            AssertScope(expected.Files, ZrdrSet.FromExtracted(output).Files);

            GameData game = GameData.Load(null, outputRoot);
            ZrdrSet effective = game.LoadChapter(mission.ChapterFolder, mission.MissionFolder).MissionZrdr;
            Assert.Equal(Path.GetFullPath(output), Path.GetFullPath(effective.Archive));
            AssertScope(expected.Files, effective.Files);
            AssertRuntime(effective, c5, edited: true);
        }
        finally
        {
            RemoveOutput(inputRoot, mission);
            RemoveOutput(outputRoot, mission);
            if (Directory.Exists(run))
            {
                Directory.Delete(run);
            }
        }
    }

    private static void ExerciseMatrix(CampaignMissionDocument document, bool c5)
    {
        ZrdrSet original = Scope(c5, edited: false);
        ZrdrSet expected = Scope(c5, edited: true);
        AssertScope(original.Files, document.Files);
        Assert.False(SameScope(original.Files, expected.Files));
        ZrdrSet corrupted = Scope(c5, edited: true);
        corrupted.Files[^1].Root[1] = 0f;
        Assert.False(SameScope(expected.Files, corrupted.Files));

        GameZ gamez = World();
        ZrdrSet chapter = new("chapter", Array.Empty<ZrdrFile>());
        CampaignMissionDependencyBundle bundle = Bundle(document.Mission, gamez, chapter);
        CampaignDangerZoneWorld danger = CampaignDangerZoneWorld.Read(bundle.World);
        var world = new PreviewWorld();
        using var preview = new CampaignMissionPreview(document, chapter, true, danger, world, "invented-world");
        using var diagnostics = new CampaignReferenceDiagnostics(document, bundle);
        var edits = new List<CampaignMissionEdit>();
        int historyRefreshes = 0;
        using var history = new CampaignMissionEditSession(document, edits.Add, () =>
        {
            historyRefreshes++;
            preview.Refresh();
        });

        ReferenceSnapshot initial = diagnostics.Current;
        Assert.Equal(ReferenceState.Resolved, Message(initial, "GOOD").State);
        Assert.Equal(ReferenceState.InheritedMissing, Message(initial, "MSG_INHERITED").State);
        Assert.Contains(initial.References, item =>
            item.Reference.Rule == ReferenceRule.StartAnimation && item.State == ReferenceState.Unverified);
        Assert.False(initial.HasErrors);

        Assert.Equal(CampaignPreviewLayers.All, preview.Refresh());
        Assert.Equal(1f, Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin.X);
        Assert.Equal(5f, Assert.Single(preview.Items(CampaignPreviewLayers.Roster)).Pose.Origin.X);
        Assert.True(preview.Select(Assert.Single(preview.Items(CampaignPreviewLayers.Targets))));
        Assert.Equal(12f, Assert.Single(preview.Items(CampaignPreviewLayers.Generators)).Pose.Origin.X);
        Assert.False(Assert.Single(preview.Items(CampaignPreviewLayers.Danger)).Enabled);
        AssertRuntime(original, c5, edited: false);

        int rosterFile = document.FindFileIndex("aiv.json");
        Assert.True(document.SetLeaf(rosterFile, new[] { 1, 1, 20 }, "MSG_BROKEN"));
        Assert.True(diagnostics.Current.HasErrors);
        Assert.Equal(ReferenceState.Missing, Message(diagnostics.Current, "MSG_BROKEN").State);
        CampaignMissionEdit broken = Assert.Single(edits);
        Assert.True(history.Replay(broken, forward: false));
        Assert.Equal(ReferenceState.Resolved, Message(diagnostics.Current, "GOOD").State);
        Assert.True(history.Replay(broken, forward: true));
        Assert.Equal(ReferenceState.Missing, Message(diagnostics.Current, "MSG_BROKEN").State);
        Assert.True(history.Replay(broken, forward: false));
        Assert.False(diagnostics.Current.HasErrors);
        edits.Clear();

        Assert.True(CampaignPlayerInit.TryBind(document, out CampaignPlayerInit? player, out string playerProblem),
            playerProblem);
        Assert.True(player.Apply(player.State with { X = 101f }));
        Assert.False(player.Apply(player.State));
        Assert.Equal(CampaignPreviewLayers.Player, preview.Refresh());
        Assert.Equal(101f, Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin.X);
        Assert.NotNull(preview.Selection);

        CampaignRosterStart roster = Roster(document);
        Assert.True(roster.Apply(roster.State with { X = 55f }));
        Assert.Equal(CampaignPreviewLayers.Roster, preview.Refresh());
        Assert.Equal(55f, Assert.Single(preview.Items(CampaignPreviewLayers.Roster)).Pose.Origin.X);
        roster = Roster(document);
        CampaignRosterFieldGroup durability = Assert.Single(roster.Groups.Where(group => group.Label == "Durability"));
        Assert.True(roster.Apply(durability, durability.Fields.Select(field =>
            field.Key == "health" ? (object)180f : roster.Value(field)).ToArray()));
        Assert.Equal(CampaignPreviewLayers.None, preview.Refresh());

        CampaignTargetEntry target = Assert.Single(CampaignMissionTargets.Bind(document, chapter, true).Entries);
        Assert.True(target.Apply(target.State with
        {
            Description = "MSG_SITE",
            Objective = false,
            OtherTarget = true,
        }));
        Assert.Equal(ReferenceState.Resolved, Message(diagnostics.Current, "MSG_SITE").State);
        preview.Refresh();

        CampaignMissionZeppelins zeppelins = CampaignMissionZeppelins.Bind(document);
        CampaignZeppelinEntry zeppelin = Assert.Single(zeppelins.Entries);
        CampaignZeppelinRow placement = Assert.Single(
            Assert.Single(zeppelin.Sections.Where(section => section.Label == "Placement and motion")).Rows);
        Assert.True(zeppelins.Apply(placement, placement.Fields.Select(field =>
            field.Label == "Position X (m)" ? (object)110f : zeppelins.Value(field)).ToArray()));
        CampaignPreviewLayers moved = preview.Refresh();
        Assert.True(moved.HasFlag(CampaignPreviewLayers.Zeppelins));
        Assert.True(moved.HasFlag(CampaignPreviewLayers.Generators));
        Assert.Equal(110f, Assert.Single(preview.Items(CampaignPreviewLayers.Zeppelins)).Pose.Origin.X);
        Assert.Equal(112f, Assert.Single(preview.Items(CampaignPreviewLayers.Generators)).Pose.Origin.X);

        CampaignMissionGenerators generators = CampaignMissionGenerators.Bind(document);
        CampaignGeneratorEntry generator = Assert.Single(generators.Entries);
        CampaignGeneratorRow cycle = Assert.Single(
            Assert.Single(generator.Sections.Where(section => section.Label == "Timing and capacity")).Rows);
        Assert.True(generators.Apply(cycle, cycle.Fields.Select(field =>
            field.Key == "wave_period" ? (object)4f : generators.Value(field)).ToArray()));
        Assert.Equal(CampaignPreviewLayers.None, preview.Refresh());

        CampaignMissionDangerZones zones = CampaignMissionDangerZones.Bind(document, danger);
        CampaignDangerZoneEntry zone = Assert.Single(zones.Entries.Where(entry => entry.Name == "dzpath1"));
        Assert.True(zones.Apply(zone, new CampaignDangerZoneState(true, true, 29f)));
        Assert.Equal(CampaignPreviewLayers.Danger, preview.Refresh());
        Assert.True(Assert.Single(preview.Items(CampaignPreviewLayers.Danger)).Enabled);

        CampaignMissionObjectives objectives = CampaignMissionObjectives.Bind(document);
        CampaignObjectiveDirective identity = Assert.Single(
            Assert.Single(objectives.Entries).Directives.Where(directive => directive.Key == "IDENTITY"));
        Assert.True(objectives.Apply(Assert.Single(identity.Rows), new object[] { "PRIMARY", 2f, "MSG_GOAL" }));
        preview.Refresh();
        Assert.True(CampaignMissionStartAnimations.Bind(document).Move(1, 0));

        Assert.True(CampaignWeatherCloudBand.TryBind(document, out CampaignWeatherCloudBand? cloud, out string cloudProblem),
            cloudProblem);
        Assert.True(cloud.Apply(cloud.State with { Top = 1400f }));
        Assert.True(CampaignWeatherWind.TryBind(document, out CampaignWeatherWind? wind, out string windProblem),
            windProblem);
        Assert.True(wind.Apply(wind.State with { StaticX = -4f }));
        Assert.True(CampaignWeatherPrecipitation.TryBind(
            document, out CampaignWeatherPrecipitation? precipitation, out string precipitationProblem),
            precipitationProblem);
        Assert.True(precipitation.Apply(precipitation.State with { Kind = CampaignWeatherPrecipitationKind.Snow }));

        string secondZone = c5 ? "ZONE3" : "ZONE2";
        Assert.True(CampaignWeatherFogZones.TryBind(document, out CampaignWeatherFogZones? fog, out string fogProblem),
            fogProblem);
        Assert.Equal(new[] { "ZONE1", secondZone }, fog.Zones.Select(item => item.Name));
        CampaignWeatherFogZone fogZone = Assert.Single(fog.Zones.Where(item => item.Name == secondZone));
        Assert.True(fogZone.Apply(fogZone.State with { Near = 120f }));
        Assert.True(CampaignWeatherLightingZones.TryBind(
            document, out CampaignWeatherLightingZones? lighting, out string lightingProblem), lightingProblem);
        Assert.Equal(new[] { "ZONE1", secondZone }, lighting.Zones.Select(item => item.Name));
        CampaignWeatherLightingZone lightZone = Assert.Single(lighting.Zones.Where(item => item.Name == secondZone));
        Assert.True(lightZone.Apply(lightZone.State with { SunDiffuse = 1.1f }));

        int rawFile = document.FindFileIndex("extra.json");
        Assert.True(document.SetLeaf(rawFile, new[] { 0 }, "RAW_EDITED"));
        Assert.False(document.SetLeaf(rawFile, new[] { 0 }, "RAW_EDITED"));
        Assert.Equal(15, edits.Count);
        Assert.Equal(15, document.Revision);
        Assert.True(document.IsDirty);
        Assert.False(diagnostics.Current.HasErrors);
        AssertScope(expected.Files, document.Files);
        AssertRuntime(new ZrdrSet("edited", document.Files), c5, edited: true);
        AssertSummaries(document, diagnostics, bundle, secondZone);

        foreach (CampaignMissionEdit edit in edits.AsEnumerable().Reverse())
        {
            Assert.True(history.Replay(edit, forward: false), edit.Name);
        }

        Assert.Equal(0, document.Revision);
        Assert.False(document.IsDirty);
        AssertScope(original.Files, document.Files);
        Assert.Equal(1f, Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin.X);
        Assert.Equal(ReferenceState.InheritedMissing, Message(diagnostics.Current, "MSG_INHERITED").State);

        foreach (CampaignMissionEdit edit in edits)
        {
            Assert.True(history.Replay(edit, forward: true), edit.Name);
        }

        Assert.Equal(15, document.Revision);
        Assert.True(document.IsDirty);
        Assert.True(historyRefreshes >= edits.Count * 2);
        AssertScope(expected.Files, document.Files);
        Assert.Equal(101f, Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin.X);
        Assert.True(Assert.Single(preview.Items(CampaignPreviewLayers.Danger)).Enabled);
        Assert.Equal(ReferenceState.Resolved, Message(diagnostics.Current, "MSG_SITE").State);
        Assert.False(diagnostics.Current.HasErrors);
    }

    private static void AssertReopenedViews(CampaignMissionDocument document, bool c5)
    {
        ZrdrSet chapter = new("chapter", Array.Empty<ZrdrFile>());
        CampaignMissionDependencyBundle bundle = Bundle(document.Mission, World(), chapter);
        using var diagnostics = new CampaignReferenceDiagnostics(document, bundle);
        ReferenceSnapshot snapshot = diagnostics.Current;
        Assert.False(snapshot.HasErrors);
        Assert.Equal(ReferenceState.Resolved, Message(snapshot, "GOOD").State);
        Assert.Equal(ReferenceState.Resolved, Message(snapshot, "MSG_SITE").State);
        Assert.DoesNotContain(snapshot.References, item =>
            item.Reference.Kind == ReferenceKind.Message
            && (item.Reference.Value.Name is "MSG_BROKEN" or "MSG_INHERITED"));
        Assert.Contains(snapshot.References, item =>
            item.Reference.Rule == ReferenceRule.StartAnimation && item.State == ReferenceState.Unverified);

        CampaignDangerZoneWorld danger = CampaignDangerZoneWorld.Read(bundle.World);
        using var preview = new CampaignMissionPreview(document, chapter, true, danger, new PreviewWorld(), "invented-world");
        Assert.Equal(CampaignPreviewLayers.All, preview.Refresh());
        Assert.Equal(101f, Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin.X);
        Assert.Equal(55f, Assert.Single(preview.Items(CampaignPreviewLayers.Roster)).Pose.Origin.X);
        Assert.Equal(110f, Assert.Single(preview.Items(CampaignPreviewLayers.Zeppelins)).Pose.Origin.X);
        Assert.Equal(112f, Assert.Single(preview.Items(CampaignPreviewLayers.Generators)).Pose.Origin.X);
        Assert.True(Assert.Single(preview.Items(CampaignPreviewLayers.Danger)).Enabled);
        Assert.True(preview.Select(Assert.Single(preview.Items(CampaignPreviewLayers.Targets))));
        AssertSummaries(document, diagnostics, bundle, c5 ? "ZONE3" : "ZONE2");
    }

    private static void AssertSummaries(
        CampaignMissionDocument document,
        CampaignReferenceDiagnostics diagnostics,
        CampaignMissionDependencyBundle bundle,
        string secondZone)
    {
        CampaignMissionReaderSummaries summary =
            CampaignMissionReaderSummaries.Read(document, bundle, diagnostics.AnimationInspection);
        Assert.True(summary.Map.Present);
        Assert.Equal("list", summary.Map.FirstKind);
        Assert.Equal(2, summary.Net.RawRows);
        Assert.Equal(1, summary.Net.IgnoredRows);
        Assert.Equal(10f, Assert.Single(summary.Net.Points).Position.X);
        Assert.True(summary.Animations.Index.Present);
        Assert.Null(summary.Animations.Inspection.Program);
        Assert.Contains(diagnostics.Current.Issues, issue =>
            issue.Code == "unverified-reader-family" && issue.Detail.Contains("location.zrd", StringComparison.Ordinal));
        Assert.True(CampaignWeatherDiagnostics.TryCreate(
            document, out CampaignWeatherDiagnostics? weather, out string problem), problem);
        CampaignWeatherSummary weatherSummary = weather.Summary(secondZone, 1100f);
        Assert.Equal(secondZone.ToLowerInvariant(), weatherSummary.SelectedZone);
        Assert.Equal(120f, weatherSummary.FogPlot!.Value.Near);
    }

    private static void AssertRuntime(ZrdrSet scope, bool c5, bool edited)
    {
        PlayerStart player = SpawnPoints.LoadPlayerInit(scope)!.Value;
        Assert.Equal(edited ? 101f : 1f, player.Spawn.Position.X);
        Assert.Equal(90f, player.Spawn.HeadingDeg);
        Assert.Equal(18f, player.SpeedMps);

        var roster = Assert.Single(AiSkills.LoadRoster(scope));
        Assert.Equal(edited ? 55f : 5f, AiSkills.RosterSpawnPose(roster.Fields)!.Value.Position.X);
        Assert.Equal(edited ? (float?)180f : null, AiSkills.RosterInitHealth(roster.Fields));
        Assert.Equal("GOOD", AiSkills.RosterTitle(roster.Fields));

        MissionTarget target = MissionTargets.Load(scope, ZrdrSet.None).For("site");
        Assert.Equal(edited ? "MSG_SITE" : "MSG_INHERITED", target.Description);
        Assert.Equal(!edited, target.Objective);
        Assert.Equal(edited, target.OtherTarget);
        Assert.Equal(edited ? 110f : 10f, Assert.Single(Zeppelins.Load(scope)).Position.X);
        Assert.Equal(edited ? 4f : 8f, Assert.Single(EnemyGenerators.Load(scope)).WavePeriod);

        ObjectiveScript script = ObjectiveScript.Load(scope);
        Assert.Equal(edited ? 2 : 1, Assert.Single(script.Objectives).Identity!.Value.Priority);
        CampaignDangerZones? danger = CampaignDangerZones.Load(script, World(), scope);
        if (edited)
        {
            Assert.NotNull(danger);
            Assert.True(danger.TryZone("dzpath1", out int slot, out bool snapshot));
            Assert.Equal(29, slot);
            Assert.True(snapshot);
        }
        else
        {
            Assert.Null(danger);
        }

        WeatherState weather = Assert.IsType<WeatherState>(WeatherState.Load(scope));
        string zone = c5 ? "zone3" : "zone2";
        Assert.Equal(new[] { "zone1", zone }, weather.ZoneNames);
        Assert.Equal(edited ? 1400f : 1200f, weather.CloudTop);
        Assert.Equal(edited ? -4f : 1f, weather.WindStatic.X);
        Assert.Equal(edited ? WeatherState.PrecipKind.Snow : WeatherState.PrecipKind.Rain, weather.Precip!.Value.Kind);
        Assert.Equal(edited ? 120f : 100f, weather.Zone(zone).FogNear);
        Assert.Equal(edited ? 1.1f : 0.9f, weather.Zone(zone).SunDiffuse);
        Assert.Equal(0.9f, weather.Zone("zone1").SunDiffuse);

        AnimProgram animation = AnimProgram.Load(ZrdrSet.None, ZrdrSet.None, scope, null, null);
        Assert.Equal(edited ? new[] { "beta", "alpha" } : new[] { "alpha", "beta" }, animation.StartAnims);
    }

    private static CampaignRosterStart Roster(CampaignMissionDocument document)
    {
        Assert.True(CampaignRosterStarts.TryBind(document, out CampaignRosterStarts? roster, out string problem), problem);
        return Assert.Single(roster.Entries);
    }

    private static CheckedReference Message(ReferenceSnapshot snapshot, string name) =>
        Assert.Single(snapshot.References.Where(item =>
            item.Reference.Kind == ReferenceKind.Message && item.Reference.Value.Name == name));

    private static CampaignMission Mission(bool c5) =>
        new(0, "Invented", c5 ? 8 : 1, 1, "Invented", false);

    private static ZrdrSet Scope(bool c5, bool edited)
    {
        float negativeZero = BitConverter.UInt32BitsToSingle(0x80000000u);
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        List<object?> fields = Enumerable.Repeat<object?>(-1f, 42).ToList();
        fields[0] = -1f;
        fields[1] = Row(edited ? 55f : 5f, 6f, 7f, "roster tail");
        fields[2] = 30f;
        fields[3] = 1f;
        fields[4] = 0f;
        fields[5] = 1f;
        fields[6] = "";
        fields[7] = edited ? 180f : 0f;
        fields[20] = "GOOD";
        fields[21] = 0f;
        fields[32] = 0f;
        fields[33] = null;
        fields[34] = 0f;
        fields[37] = 0f;
        fields[38] = "";
        fields[39] = "";
        fields[40] = 0f;

        List<object?> target = Row(
            Row("description", edited ? "MSG_SITE" : "MSG_INHERITED", "description tail"),
            Row("nodes", Row("site")));
        if (!edited)
        {
            target.Add(Row("objective"));
        }

        target.Add(Row("unknown", Row(negativeZero, subnormal, null, Row("nested"))));
        if (edited)
        {
            target.Add(Row("other_target"));
        }

        string secondZone = c5 ? "ZONE3" : "ZONE2";
        return new ZrdrSet("invented", new[]
        {
            Reader("objectives", Row(Row(
                "PLAYER_INIT", Player(-5f),
                "PLAYER_INIT", Player(edited ? 101f : 1f),
                "OBJECTIVE1", Row(
                    "IDENTITY", Row("PRIMARY", edited ? 2f : 1f, "MSG_GOAL", "identity tail"),
                    "DANGER_ZONES_COMPLETED", Row("dzpath1"),
                    "UNKNOWN", Row(negativeZero, subnormal, null, Row("retained")))))),
            Reader("aiv", Row(Row(1f, 0f, "Wing"), Row("secfury_1", fields))),
            Reader("targets", Row(target)),
            Reader("zeppelins", Row(Row(Row(
                "node", Row("carrier", "node tail"),
                "net", Row("route"),
                "position", Row(edited ? 110f : 10f, 20f, 30f),
                "yaw", Row(0f), "pitch", Row(0f),
                "unknown", Row(negativeZero, subnormal, null))))),
            Reader("egen", Row(Row(Row(
                "node", Row("carrier"), "vehicle", Row("nets", Row("route")),
                "capacity", Row(0f), "max_active", Row(4f), "wave_size", Row(1f),
                "wave_period", Row(edited ? 4f : 8f, "period tail"), "ind_period", Row(1f),
                "zeppelin", "origin", Row("bay"))))),
            Reader("dzones", Row(
                "disable", edited ? Row() : Row("dzpath1"),
                "nosnapshot", edited ? Row() : Row("dzpath1"),
                "objective_numbers", Row(Row("dzpath1", edited ? 29f : 18f, "slot tail")),
                "unknown", Row(negativeZero, subnormal, null))),
            Reader("startanims", Row(Row("NEW_GAME_START", edited
                ? Row(Row("beta", "beta tail"), Row("alpha", "alpha tail"))
                : Row(Row("alpha", "alpha tail"), Row("beta", "beta tail"))))),
            Reader("weather", Row(Row(
                "CLOUD_COVER", Row("TOP", edited ? 1400f : 1200f, "TOP", 9999f,
                    "BOTTOM", 800f, "THICKNESS", 100f, "TOP_COLOR", Row(192f, 192f, 192f, "color tail")),
                "WIND", Row("STATIC_VELOCITY", Row(edited ? -4f : 1f, 2f, 3f, "wind tail"),
                    "RANDOM_MAX_SPEED", 4f, "RANDOM_ACCEL", 0.5f, "RANDOM_ANG_VEL", 45f),
                "TYPE", edited ? "SNOW" : "RAIN", "COLOR", Row(128f, 96f, 64f, "rain tail"),
                "WIND_DIR", 15f, "WIND_VEL", 0.8f, "GRAVITY", 3f, "PARTICLES", 100f,
                "ALPHA_GRADIENT", Row(0.5f, 0f, "alpha tail"),
                "SW_ZONE1", WeatherZone(false), "ZONE1", WeatherZone(false),
                "SW_" + secondZone, WeatherZone(false), secondZone, WeatherZone(edited),
                "UNKNOWN", Row(negativeZero, subnormal, null)))),
            Reader("map", Row(Row("MAP", Row("unknown", 12f)), "map tail")),
            Reader("net", Row(Row(Row(10f, negativeZero, 3.5f, 90f, "net tail"), Row(1f, 2f, 3f)))),
            Reader("location", Row(Row("Anchor", Row("POSITION", Row(100f, 200f, 300f))))),
            Reader("mis_anim", Row(Row())),
            Reader("extra", Row(edited ? "RAW_EDITED" : "RAW_ORIGINAL", negativeZero, subnormal, null, Row("keep"))),
            new ZrdrFile("extra.zrd", "extra-1.zrd", Row("duplicate", negativeZero, subnormal, null)),
        });
    }

    private static List<object?> Player(float x) =>
        Row(1f, Row(x, 2f, 3f, "position tail"), Row(0f, 90f, 0f, "rotation tail"), 0.8f, 180f, "player tail");

    private static List<object?> WeatherZone(bool edited) =>
        Row("FOG_COLOR", Row(0.2f, 0.3f, 0.4f, "fog tail"),
            "FOG_RANGES", Row(edited ? 120f : 100f, 200f, "range tail"),
            "FOG_ALTITUDE", Row(0f, 500f, "altitude tail"),
            "CLIP_RANGES", Row(1f, 300f, "clip tail"),
            "SUNLIGHT_DIFFUSE", Row(edited ? 1.1f : 0.9f, "light tail"),
            "SUNLIGHT_AMBIENT", Row(0.25f),
            "SUNLIGHT_COLOR_DIFFUSE", Row(0.1f, 0.2f, 0.3f),
            "SUNLIGHT_COLOR_AMBIENT", Row(0.2f, 0.3f, 0.4f),
            "SUNLIGHT_BICOLORED", Row(0f),
            "SUNLIGHT_ORIENTATION", Row(-25f, 90f, 0f));

    private static ZrdrFile Reader(string name, List<object?> root) => new(name + ".zrd", null, root);

    private static List<object?> Row(params object?[] items) => new(items);

    private static void AssertScope(IReadOnlyList<ZrdrFile> expected, IReadOnlyList<ZrdrFile> actual)
    {
        Assert.Equal(expected.Select(file => file.Key), actual.Select(file => file.Key));
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(SameValue(expected[i].Root, actual[i].Root), $"Reader {expected[i].Key} differs.");
        }
    }

    private static bool SameScope(IReadOnlyList<ZrdrFile> expected, IReadOnlyList<ZrdrFile> actual) =>
        expected.Count == actual.Count
        && expected.Zip(actual, (left, right) => left.Key == right.Key && SameValue(left.Root, right.Root)).All(value => value);

    private static bool SameValue(object? expected, object? actual) =>
        (expected, actual) switch
        {
            (null, null) => true,
            (string left, string right) => left == right,
            (float left, float right) => BitConverter.SingleToUInt32Bits(left) == BitConverter.SingleToUInt32Bits(right),
            (List<object?> left, List<object?> right) => left.Count == right.Count
                && left.Zip(right, SameValue).All(value => value),
            _ => false,
        };

    private static GameZ World()
    {
        var world = new GameZ();
        var mesh = new GameZMesh();
        mesh.Vertices.AddRange(new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f),
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f),
            new Vector3(-1f, -1f, 10f), new Vector3(1f, -1f, 10f),
            new Vector3(1f, 1f, 10f), new Vector3(-1f, 1f, 10f),
        });
        AddPolygon(mesh, 1, 0, 1);
        AddPolygon(mesh, 2, 2, 3, 4, 5);
        AddPolygon(mesh, 2, 6, 7, 8, 9);
        world.Meshes.Add(mesh);
        world.Nodes.Add(new GameZNode { Index = 0, Name = "dzpath1", MeshIndex = 0, Local = Transform3D.Identity });
        world.Nodes.Add(new GameZNode { Index = 1, Name = "site", MeshIndex = -1, Local = Transform3D.Identity });
        world.Nodes.Add(new GameZNode { Index = 2, Name = "carrier", MeshIndex = -1, Local = Transform3D.Identity });
        return world;
    }

    private static void AddPolygon(GameZMesh mesh, int material, params int[] vertices)
    {
        var polygon = new GameZPolygon { MaterialIndex = material };
        polygon.VertexIndices.AddRange(vertices);
        mesh.Polygons.Add(polygon);
    }

    private static CampaignMissionDependencyBundle Bundle(CampaignMission mission, GameZ world, ZrdrSet chapter) =>
        new(
            Guid.NewGuid(), mission,
            Dependency(new ZrdrSet("shared", Array.Empty<ZrdrFile>()), CampaignDependencyState.Ready),
            Dependency<GameZ?>(world, CampaignDependencyState.Ready),
            Dependency(TextureSet.Empty, CampaignDependencyState.Ready),
            Dependency(chapter, CampaignDependencyState.Ready),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Missing, true),
            Dependency<AnimArchive?>(null, CampaignDependencyState.Unavailable, true),
            Dependency(Messages.Of(new[] { ("GOOD", "Good"), ("MSG_GOAL", "Goal"), ("MSG_SITE", "Site") }),
                CampaignDependencyState.Ready, true),
            Dependency<InterpScripts?>(null, CampaignDependencyState.Missing, true),
            Dependency<MissionSetup?>(null, CampaignDependencyState.NotChecked, true),
            null);

    private static CampaignDependency<T> Dependency<T>(T value, CampaignDependencyState state, bool optional = false) =>
        new(value, new CampaignDependencyAvailability("invented", state, optional));

    private static string ExternalRoot()
    {
        string path = System.Environment.GetEnvironmentVariable(
            CampaignAuthoringExternalTheoryAttribute.Variable)!;
        Assert.True(Path.IsPathFullyQualified(path), "The external output root must be an absolute path.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        Assert.True(Directory.Exists(root), "Create the external output root before running this test.");
        Assert.False(IsWithin(root, TestData.RepoRoot), "The output root must be outside the checkout.");
        Assert.False(IsWithin(root, Path.GetTempPath()), "The output root must not be a temporary directory.");
        for (DirectoryInfo? directory = new(root); directory != null; directory = directory.Parent)
        {
            Assert.False((directory.Attributes & FileAttributes.ReparsePoint) != 0,
                "The output root and its ancestors must not be links or junctions.");
        }

        return root;
    }

    private static bool IsWithin(string path, string parent)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(parent), path);
        return relative == "." || (!Path.IsPathRooted(relative)
            && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static void RemoveOutput(string root, CampaignMission mission)
    {
        string chapter = Path.Combine(root, mission.ChapterFolder.ToUpperInvariant());
        string directory = Path.Combine(chapter, mission.MissionFolder.ToUpperInvariant());
        string archive = Path.Combine(directory, "zrdr.zip");
        if (File.Exists(archive))
        {
            File.Delete(archive);
        }

        foreach (string path in new[] { directory, chapter, root })
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path);
            }
        }
    }

    private sealed class PreviewWorld : ICampaignPreviewWorld
    {
        private Transform3D _carrier = Transform3D.Identity;

        public bool TryPose(string name, out Transform3D pose)
        {
            pose = _carrier;
            return name == "carrier";
        }

        public bool TryTarget(IReadOnlyList<string> path, out Vector3 position)
        {
            position = new Vector3(40f, 50f, 60f);
            return path.Count == 1 && path[0] == "site";
        }

        public bool TryGenerator(
            string host, string? origin, bool zeppelin, out Transform3D hostPose, out Vector3 offset, out string note)
        {
            hostPose = _carrier;
            offset = new Vector3(2f, 0f, 0f);
            note = "Invented host-relative placement.";
            return host == "carrier" && origin == "bay" && zeppelin;
        }

        public void PlaceZeppelins(IReadOnlyList<CampaignPreviewItem> zeppelins)
        {
            foreach (CampaignPreviewItem zeppelin in zeppelins)
            {
                if (zeppelin.Name == "carrier")
                {
                    _carrier = zeppelin.Pose;
                }
            }
        }
    }
}

/// <summary>Runs the invented ZIP round trip only when an external output root is configured.</summary>
public sealed class CampaignAuthoringExternalTheoryAttribute : TheoryAttribute
{
    internal const string Variable = "CSVM_AUTHORING_OUTPUT_ROOT";

    public CampaignAuthoringExternalTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to an existing external, non-temporary output directory.";
        }
    }
}

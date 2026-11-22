using System;
using System.Collections.Generic;
using System.IO;
using CSVM.Extraction;
using CSVM.Mech3;
using CSVM.Spec;
using CSVM.Tooling;
using Xunit;

namespace CSVM.Tests;

/// <summary>The selected mission's read-only dependency bundle over invented models.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionDependencyBundleTests
{
    [Fact]
    public void LoadCapturesExactDependencyPathsAndMaterializedModels()
    {
        (CampaignMission mission, GameData data) = InventedData();
        var source = new MemorySource(
            GameData.RootZrdrPath,
            "ZBD/c3/gamez.zbd",
            "ZBD/c3/rtexture15.zbd",
            "ZBD/c3/rtexture10.zbd",
            "ZBD/c3/zrdr.zbd",
            "ZBD/c3/cam_anim.zbd",
            "ZBD/c3/m01/mis_anim.zbd",
            Messages.GameFile,
            InterpScripts.GamePath);

        CampaignMissionDependencyBundle bundle =
            CampaignMissionDependencyBundle.Load(source, data, mission);

        Assert.NotEqual(Guid.Empty, bundle.Generation);
        Assert.Equal(mission, bundle.Mission);
        Assert.Same(data.Zrdr, bundle.SharedReaders.Value);
        Assert.NotNull(bundle.World.Value);
        Assert.Equal("ZBD/c3/rtexture15.zbd", bundle.Textures.Availability.Path);
        Assert.True(bundle.ChapterReaders.Value.Exists);
        Assert.NotNull(bundle.ChapterAnimations.Value);
        Assert.NotNull(bundle.MissionAnimations.Value);
        Assert.Equal("Probe", bundle.MessageTable.Value.Get("MSG_PROBE"));
        Assert.NotNull(bundle.Setup.Value);
        Assert.Equal(new[] { "Quit" }, bundle.SetupLines);
        Assert.All(
            bundle.Availabilities,
            state => Assert.Equal(CampaignDependencyState.Ready, state.State));
    }

    [Fact]
    public void LoadDistinguishesMissingOptionalAndRequiredInputs()
    {
        var mission = new CampaignMission(0, "Probe", 6, 1, "Probe", false);
        var data = new GameData
        {
            LoadChapter = ChapterData.Bare,
        };

        CampaignMissionDependencyBundle bundle =
            CampaignMissionDependencyBundle.Load(new MemorySource(), data, mission);

        AssertAvailability(
            bundle.SharedReaders,
            GameData.RootZrdrPath,
            CampaignDependencyState.Missing,
            optional: false);
        AssertAvailability(
            bundle.World,
            "ZBD/c3/gamez.zbd",
            CampaignDependencyState.Missing,
            optional: false);
        AssertAvailability(
            bundle.Textures,
            "ZBD/c3/texture.zbd",
            CampaignDependencyState.Missing,
            optional: false);
        AssertAvailability(
            bundle.ChapterAnimations,
            "ZBD/c3/cam_anim.zbd",
            CampaignDependencyState.Missing,
            optional: true);
        AssertAvailability(
            bundle.MissionAnimations,
            "ZBD/c3/m01/mis_anim.zbd",
            CampaignDependencyState.Missing,
            optional: true);
        Assert.Equal(
            CampaignDependencyState.NotChecked,
            bundle.Setup.Availability.State);
    }

    [Fact]
    public void LoadKeepsValidEmptyReaderScopesDistinctFromAbsentScopes()
    {
        var mission = new CampaignMission(0, "Probe", 6, 1, "Probe", false);
        var emptyShared = new ZrdrSet(
            GameData.RootZrdrPath,
            Array.Empty<ZrdrFile>());
        var emptyChapter = new ZrdrSet(
            "ZBD/c3/zrdr.zbd",
            Array.Empty<ZrdrFile>());
        var chapter = new ChapterData
        {
            Chapter = mission.ChapterFolder,
            Mission = mission.MissionFolder,
            ChapterZrdrSource = new Lazy<ZrdrSet>(() => emptyChapter),
        };
        var data = new GameData
        {
            Zrdr = emptyShared,
            LoadChapter = (_, _) => chapter,
        };
        var source = new MemorySource(
            GameData.RootZrdrPath,
            "ZBD/c3/zrdr.zbd");

        CampaignMissionDependencyBundle bundle =
            CampaignMissionDependencyBundle.Load(source, data, mission);

        Assert.Equal(
            CampaignDependencyState.Ready,
            bundle.SharedReaders.Availability.State);
        Assert.Equal(
            CampaignDependencyState.Ready,
            bundle.ChapterReaders.Availability.State);
    }

    [Fact]
    public void LoadReportsDecodeFailuresWithoutInventingModels()
    {
        var mission = new CampaignMission(0, "Probe", 6, 1, "Probe", false);
        var chapter = new ChapterData
        {
            Chapter = mission.ChapterFolder,
            Mission = mission.MissionFolder,
            GamezSource = new Lazy<GameZ?>(
                () => throw new InvalidDataException("invented failure")),
        };
        var data = new GameData
        {
            LoadChapter = (_, _) => chapter,
        };
        var source = new MemorySource("ZBD/c3/gamez.zbd");

        CampaignMissionDependencyBundle bundle =
            CampaignMissionDependencyBundle.Load(source, data, mission);

        Assert.Null(bundle.World.Value);
        Assert.Equal(
            CampaignDependencyState.Unavailable,
            bundle.World.Availability.State);
        Assert.Contains("invented failure", bundle.World.Availability.Detail);
    }

    [Fact]
    public void LoadCreatesFreshAddressedBundlesForEachSelection()
    {
        (CampaignMission firstMission, GameData data) = InventedData();
        var secondMission =
            new CampaignMission(1, "Second", 6, 2, "Second", false);
        var source = new MemorySource(
            GameData.RootZrdrPath,
            "ZBD/c3/gamez.zbd",
            "ZBD/c3/rtexture15.zbd",
            "ZBD/c3/zrdr.zbd",
            "ZBD/c3/cam_anim.zbd",
            "ZBD/c3/m01/mis_anim.zbd",
            "ZBD/c3/m02/mis_anim.zbd",
            Messages.GameFile,
            InterpScripts.GamePath);

        CampaignMissionDependencyBundle first =
            CampaignMissionDependencyBundle.Load(source, data, firstMission);
        CampaignMissionDependencyBundle second =
            CampaignMissionDependencyBundle.Load(source, data, secondMission);

        Assert.NotEqual(first.Generation, second.Generation);
        Assert.Equal("m01", first.Mission.MissionFolder);
        Assert.Equal("m02", second.Mission.MissionFolder);
        Assert.Equal(
            "ZBD/c3/m02/mis_anim.zbd",
            second.MissionAnimations.Availability.Path);
        Assert.NotSame(
            first.MissionAnimations.Value,
            second.MissionAnimations.Value);
    }

    [Fact]
    public void LoadRejectsModdedGameData()
    {
        (CampaignMission mission, GameData invented) = InventedData();
        var modded = new GameData
        {
            Zrdr = invented.Zrdr,
            Messages = invented.Messages,
            Interp = invented.Interp,
            LoadChapter = invented.LoadChapter,
            ModFolder = "invented-mod",
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => CampaignMissionDependencyBundle.Load(
                new MemorySource(),
                modded,
                mission));

        Assert.Contains("retail-only", error.Message);
    }

    private static void AssertAvailability<T>(
        CampaignDependency<T> dependency,
        string path,
        CampaignDependencyState state,
        bool optional)
    {
        Assert.Equal(path, dependency.Availability.Path);
        Assert.Equal(state, dependency.Availability.State);
        Assert.Equal(optional, dependency.Availability.Optional);
    }

    private static (CampaignMission Mission, GameData Data) InventedData()
    {
        var mission = new CampaignMission(0, "Probe", 6, 1, "Probe", false);
        var shared = new ZrdrSet(
            GameData.RootZrdrPath,
            new[]
            {
                new ZrdrFile(
                    "shared.zrd",
                    null,
                    new List<object?> { "shared" }),
            });
        var chapterReaders = new ZrdrSet(
            "ZBD/c3/zrdr.zbd",
            new[]
            {
                new ZrdrFile(
                    "chapter.zrd",
                    null,
                    new List<object?> { "chapter" }),
            });
        var missionReaders = new ZrdrSet(
            "ZBD/c3/m01/zrdr.zbd",
            new[]
            {
                new ZrdrFile(
                    "mission.zrd",
                    null,
                    new List<object?> { "mission" }),
            });
        var textures = new TextureSet(
            "c3/rtexture15",
            new[]
            {
                new TextureImage(
                    "probe.tif",
                    null,
                    1,
                    1,
                    TextureArchive.AlphaClass.None,
                    0,
                    false,
                    new byte[] { 1, 2, 3 }),
            });
        Messages messages = Messages.Of(
            new[] { (Key: "MSG_PROBE", Value: "Probe") });

        static AnimArchive Animation(string label, string name) =>
            AnimArchive.FromDecoded(
                label,
                new List<AnimDefinition>
                {
                    new() { Name = "node", AnimName = name },
                },
                new List<(string Name, SiScript Script)>());

        var interp = new InterpScripts(
            new List<InterpScript>
            {
                new(@"support\c3\m01.gw", new[] { "Quit" }),
                new(@"support\c3\m02.gw", new[] { "Quit" }),
            });
        var data = new GameData
        {
            Zrdr = shared,
            Messages = messages,
            Interp = interp,
            LoadChapter = (chapterName, missionName) =>
                new ChapterData
                {
                    Chapter = chapterName,
                    Mission = missionName,
                    GamezSource = new Lazy<GameZ?>(() => new GameZ()),
                    TexturesSource = new Lazy<TextureSet>(() => textures),
                    ChapterZrdrSource =
                        new Lazy<ZrdrSet>(() => chapterReaders),
                    MissionZrdrSource =
                        new Lazy<ZrdrSet>(() => missionReaders),
                    ChapterAnimSource = new Lazy<AnimArchive?>(
                        () => Animation("cam_anim", "chapter_anim")),
                    MissionAnimSource = new Lazy<AnimArchive?>(
                        () => Animation("mis_anim", "mission_anim")),
                },
        };
        return (mission, data);
    }

    private sealed class MemorySource : IGameFileSource
    {
        private readonly HashSet<string> _paths;

        internal MemorySource(params string[] paths)
        {
            _paths = new HashSet<string>(
                paths,
                StringComparer.OrdinalIgnoreCase);
        }

        public string Location => "invented";

        public bool Exists(string path) => _paths.Contains(path);

        public Stream? Open(string path) => null;
    }
}

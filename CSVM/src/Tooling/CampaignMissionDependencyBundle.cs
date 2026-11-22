using System;
using System.Collections.Generic;
using System.IO;
using CSVM.Extraction;
using CSVM.Mech3;
using CSVM.Spec;

namespace CSVM.Tooling;

internal enum CampaignDependencyState
{
    Ready,
    Missing,
    Unavailable,
    EmptyOrUnavailable,
    NotChecked,
}

internal sealed record CampaignDependencyAvailability(
    string Path,
    CampaignDependencyState State,
    bool Optional,
    string? Detail = null);

internal sealed record CampaignDependency<T>(
    T Value,
    CampaignDependencyAvailability Availability);

internal sealed record class CampaignMissionDependencyBundle(
    Guid Generation,
    CampaignMission Mission,
    CampaignDependency<ZrdrSet> SharedReaders,
    CampaignDependency<GameZ?> World,
    CampaignDependency<TextureSet> Textures,
    CampaignDependency<ZrdrSet> ChapterReaders,
    CampaignDependency<AnimArchive?> ChapterAnimations,
    CampaignDependency<AnimArchive?> MissionAnimations,
    CampaignDependency<Messages> MessageTable,
    CampaignDependency<InterpScripts?> Interp,
    CampaignDependency<MissionSetup?> Setup,
    IReadOnlyList<string>? SetupLines)
{
    internal IReadOnlyList<CampaignDependencyAvailability> Availabilities =>
        new[]
        {
            SharedReaders.Availability,
            World.Availability,
            Textures.Availability,
            ChapterReaders.Availability,
            ChapterAnimations.Availability,
            MissionAnimations.Availability,
            MessageTable.Availability,
            Interp.Availability,
            Setup.Availability,
        };

    internal static CampaignMissionDependencyBundle Load(
        IGameFileSource source,
        GameData data,
        CampaignMission mission)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(mission);

        if (data.ModFolder is not null)
        {
            throw new ArgumentException(
                "Campaign dependency loading requires retail-only game data.",
                nameof(data));
        }

        string chapterName = mission.ChapterFolder;
        if (chapterName.Length == 0 || mission.Mission is < 1 or > 5)
        {
            throw new ArgumentException("Invalid campaign mission address.", nameof(mission));
        }

        string missionName = mission.MissionFolder;
        string chapterPath = $"ZBD/{chapterName}";
        string missionPath = $"{chapterPath}/{missionName}";
        ChapterData chapter = data.LoadChapter(chapterName, missionName);

        CampaignDependency<ZrdrSet> shared = Capture(
            source,
            GameData.RootZrdrPath,
            () => data.Zrdr,
            value => value.Exists,
            ZrdrSet.None);
        CampaignDependency<GameZ?> world = Capture(
            source,
            $"{chapterPath}/gamez.zbd",
            () => chapter.Gamez,
            value => value is not null,
            null);
        CampaignDependency<ZrdrSet> chapterReaders = Capture(
            source,
            $"{chapterPath}/zrdr.zbd",
            () => chapter.ChapterZrdr,
            value => value.Exists,
            ZrdrSet.None);
        CampaignDependency<AnimArchive?> chapterAnimations = Capture(
            source,
            $"{chapterPath}/cam_anim.zbd",
            () => chapter.ChapterAnim,
            value => value is not null,
            null,
            optional: true);
        CampaignDependency<AnimArchive?> missionAnimations = Capture(
            source,
            $"{missionPath}/mis_anim.zbd",
            () => chapter.MissionAnim,
            value => value is not null,
            null,
            optional: true);
        CampaignDependency<Messages> messages = Capture(
            source,
            Messages.GameFile,
            () => data.Messages,
            value => value.Count > 0,
            new Messages(),
            optional: true,
            emptyOrUnavailable: true);
        CampaignDependency<InterpScripts?> interp = Capture(
            source,
            InterpScripts.GamePath,
            () => data.Interp,
            value => value is not null,
            null,
            optional: true);

        CampaignDependency<TextureSet> textures;
        try
        {
            string path = TextureSet.ChapterArchive(source, chapterName)
                ?? $"{chapterPath}/texture.zbd";
            textures = Capture(
                source,
                path,
                () => chapter.Textures,
                value => !ReferenceEquals(value, TextureSet.Empty),
                TextureSet.Empty);
        }
        catch (Exception error) when (IsInputError(error))
        {
            textures = new CampaignDependency<TextureSet>(
                TextureSet.Empty,
                new CampaignDependencyAvailability(
                    chapterPath,
                    CampaignDependencyState.Unavailable,
                    false,
                    error.Message));
        }

        string scriptName =
            $"support\\{chapterName.ToLowerInvariant()}\\{missionName.ToLowerInvariant()}.gw";
        string setupPath = $"{InterpScripts.GamePath}::{scriptName}";
        IReadOnlyList<string>? setupLines = null;
        CampaignDependency<MissionSetup?> setup;
        if (interp.Value is null)
        {
            setup = new CampaignDependency<MissionSetup?>(
                null,
                new CampaignDependencyAvailability(
                    setupPath,
                    CampaignDependencyState.NotChecked,
                    true));
        }
        else
        {
            try
            {
                IReadOnlyList<string>? rawLines = interp.Value.Lines(scriptName);
                setupLines = rawLines is null
                    ? null
                    : new List<string>(rawLines).AsReadOnly();
                MissionSetup? parsedSetup =
                    MissionSetup.Load(interp.Value, chapterName, missionName);
                CampaignDependencyState setupState = parsedSetup is null
                    ? CampaignDependencyState.Missing
                    : CampaignDependencyState.Ready;
                setup = new CampaignDependency<MissionSetup?>(
                    parsedSetup,
                    new CampaignDependencyAvailability(
                        setupPath,
                        setupState,
                        true));
            }
            catch (Exception error) when (IsInputError(error))
            {
                setup = new CampaignDependency<MissionSetup?>(
                    null,
                    new CampaignDependencyAvailability(
                        setupPath,
                        CampaignDependencyState.Unavailable,
                        true,
                        error.Message));
            }
        }

        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            mission,
            shared,
            world,
            textures,
            chapterReaders,
            chapterAnimations,
            missionAnimations,
            messages,
            interp,
            setup,
            setupLines);
    }

    private static CampaignDependency<T> Capture<T>(
        IGameFileSource source,
        string path,
        Func<T> load,
        Func<T, bool> available,
        T fallback,
        bool optional = false,
        bool emptyOrUnavailable = false)
    {
        try
        {
            T value = load();
            if (available(value))
            {
                return new CampaignDependency<T>(
                    value,
                    new CampaignDependencyAvailability(
                        path,
                        CampaignDependencyState.Ready,
                        optional));
            }

            CampaignDependencyState state = !source.Exists(path)
                ? CampaignDependencyState.Missing
                : emptyOrUnavailable
                    ? CampaignDependencyState.EmptyOrUnavailable
                    : CampaignDependencyState.Unavailable;
            return new CampaignDependency<T>(
                value,
                new CampaignDependencyAvailability(path, state, optional));
        }
        catch (Exception error) when (IsInputError(error))
        {
            return new CampaignDependency<T>(
                fallback,
                new CampaignDependencyAvailability(
                    path,
                    CampaignDependencyState.Unavailable,
                    optional,
                    error.Message));
        }
    }

    private static bool IsInputError(Exception error) =>
        error is InvalidDataException or IOException or UnauthorizedAccessException;
}

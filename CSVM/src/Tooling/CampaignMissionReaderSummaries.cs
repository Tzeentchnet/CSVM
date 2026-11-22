using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using CSVM.Flight.Modes;
using CSVM.Mech3;

namespace CSVM.Tooling;

internal sealed record CampaignAnimationInspection(AnimProgram? Program, string? UnavailableReason)
{
    internal static CampaignAnimationInspection Read(
        ZrdrSet mission,
        CampaignMissionDependencyBundle bundle)
    {
        if (bundle.SharedReaders.Availability.State != CampaignDependencyState.Ready
            || !bundle.SharedReaders.Value.Exists
            || bundle.ChapterReaders.Availability.State != CampaignDependencyState.Ready
            || !bundle.ChapterReaders.Value.Exists)
        {
            return new(null, "The shared or chapter reader scope is unavailable.");
        }

        if (!ArchiveKnown(bundle.ChapterAnimations) || !ArchiveKnown(bundle.MissionAnimations))
        {
            return new(null, "A compiled animation archive is unavailable or has not been checked.");
        }

        try
        {
            return new(
                AnimProgram.LoadForInspection(
                    bundle.SharedReaders.Value, bundle.ChapterReaders.Value, mission,
                    bundle.ChapterAnimations.Value, bundle.MissionAnimations.Value),
                null);
        }
        catch (Exception error) when (
            error is InvalidDataException or IOException or ArgumentException or InvalidOperationException
                or IndexOutOfRangeException or KeyNotFoundException)
        {
            return new(null, $"The animation program could not be inspected: {error.Message}");
        }
    }

    private static bool ArchiveKnown(CampaignDependency<AnimArchive?> archive) =>
        (archive.Availability.State == CampaignDependencyState.Missing && archive.Value == null)
        || (archive.Availability.State == CampaignDependencyState.Ready && archive.Value != null);
}

internal sealed record CampaignReaderShape(
    bool Present,
    int RootCount,
    string FirstKind,
    int? FirstListCount);

internal sealed record CampaignNetSummary(
    CampaignReaderShape Reader,
    int RawRows,
    int IgnoredRows,
    ImmutableArray<SpawnPoint> Points,
    string? Problem);

internal sealed record CampaignAnimationSummary(
    CampaignReaderShape Index,
    ImmutableArray<string> ListedPaths,
    CampaignDependencyState ChapterArchive,
    CampaignDependencyState MissionArchive,
    CampaignAnimationInspection Inspection);

internal sealed record CampaignMissionReaderSummaries(
    CampaignReaderShape Map,
    CampaignNetSummary Net,
    CampaignAnimationSummary Animations)
{
    internal static CampaignMissionReaderSummaries Read(
        CampaignMissionDocument document,
        CampaignMissionDependencyBundle bundle,
        CampaignAnimationInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(inspection);
        if (document.Mission != bundle.Mission)
        {
            throw new ArgumentException("The document and dependency bundle address different missions.", nameof(bundle));
        }

        var scope = new ZrdrSet(CampaignMissionDocument.ArchivePath(document.Mission), document.Files);
        ZrdrFile? map = ZrdrZbd.Find(scope.Files, "map.json");
        ZrdrFile? net = ZrdrZbd.Find(scope.Files, "net.json");
        ZrdrFile? index = ZrdrZbd.Find(scope.Files, "mis_anim.json");
        List<SpawnPoint>? loadedPoints;
        string? netProblem = null;
        try
        {
            loadedPoints = SpawnPoints.LoadNetTable(scope);
        }
        catch (InvalidDataException error)
        {
            loadedPoints = null;
            netProblem = $"The multiplayer reader rejects this table: {error.Message}";
        }

        ImmutableArray<SpawnPoint> points = (loadedPoints ?? new List<SpawnPoint>()).ToImmutableArray();
        int rawRows = net != null && net.Root.Count > 0 && net.Root[0] is List<object?> rows ? rows.Count : 0;
        var paths = ImmutableArray.CreateBuilder<string>();
        if (index != null)
        {
            paths.AddRange(MissionCutscenes.ListedPaths(index.Root));
        }

        return new CampaignMissionReaderSummaries(
            Shape(map),
            new CampaignNetSummary(Shape(net), rawRows, rawRows - points.Length, points, netProblem),
            new CampaignAnimationSummary(
                Shape(index), paths.ToImmutable(),
                bundle.ChapterAnimations.Availability.State,
                bundle.MissionAnimations.Availability.State,
                inspection));
    }

    private static CampaignReaderShape Shape(ZrdrFile? file)
    {
        if (file == null)
        {
            return new(false, 0, "missing", null);
        }

        if (file.Root.Count == 0)
        {
            return new(true, 0, "empty", null);
        }

        object? first = file.Root[0];
        string kind = first switch
        {
            null => "null",
            List<object?> => "list",
            string => "string",
            float => "float",
            _ => "other",
        };
        return new(true, file.Root.Count, kind, (first as List<object?>)?.Count);
    }
}

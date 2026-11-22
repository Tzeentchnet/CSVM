using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CSVM.Extraction;
using CSVM.Mech3;
using CSVM.Spec;

namespace CSVM.Tooling;

/// <summary>
/// One campaign mission's complete editable reader scope. It owns a deep copy of every
/// <see cref="ZrdrFile"/> in archive order, permits type-preserving scalar edits, and exports the
/// whole scope to the external modding layout. The raw list tree remains canonical.
/// Authoring rules: docs/formats/missions/campaign-authoring.md.
/// </summary>
public sealed class CampaignMissionDocument
{
    private static readonly DateTimeOffset ZipTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<ZrdrFile> _files;
    private readonly string? _sourceLocation;

    private CampaignMissionDocument(
        CampaignMission mission,
        ZrdrSet scope,
        string? sourceLocation)
    {
        if (!scope.Exists)
        {
            throw new FileNotFoundException($"mission reader archive '{ArchivePath(mission)}' is absent");
        }

        Mission = mission;
        _files = new List<ZrdrFile>(scope.Files.Count);
        foreach (var file in scope.Files)
        {
            _files.Add(new ZrdrFile(file.Name, file.Rename, Copy(file.Root)));
        }

        _sourceLocation = sourceLocation;
    }

    /// <summary>The campaign sequence row whose mission scope this document owns.</summary>
    public CampaignMission Mission { get; }

    /// <summary>Whether a scalar differs from the loaded or last exported document.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The complete reader set in source order. Its trees are read-only by contract;
    /// edits go through <see cref="SetLeaf"/>.</summary>
    public IReadOnlyList<ZrdrFile> Files => _files;

    /// <summary>Finds a reader by its archive or extracted-JSON spelling. Returns -1 when it is
    /// absent.</summary>
    public int FindFileIndex(string name)
    {
        ZrdrFile? found =
            ZrdrZbd.Find(_files, name);
        if (found == null)
        {
            return -1;
        }

        for (int i = 0; i < _files.Count; i++)
        {
            if (ReferenceEquals(_files[i], found))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The 24 campaign rows read from a user-owned game-file source.</summary>
    public static IReadOnlyList<CampaignMission> LoadMissions(IGameFileSource game)
    {
        var root = ZrdrSet.Load(game, GameData.RootZrdrPath);
        if (!root.Exists)
        {
            throw new FileNotFoundException(
                $"shared reader archive '{GameData.RootZrdrPath}' is absent");
        }

        var missions = CampaignSequence.Load(root);
        if (missions.Count != CampaignSequence.MissionCount)
        {
            throw new InvalidDataException(
                Invariant(
                    $"campaign sequence has {missions.Count} missions, expected {CampaignSequence.MissionCount}"));
        }

        return missions;
    }

    /// <summary>Loads one campaign mission's complete retail reader scope.</summary>
    public static CampaignMissionDocument Load(
        IGameFileSource game,
        CampaignMission mission)
    {
        string path = ArchivePath(mission);
        return new CampaignMissionDocument(
            mission,
            ZrdrSet.Load(game, path),
            game.Location);
    }

    /// <summary>Builds a document over an already decoded complete scope.</summary>
    public static CampaignMissionDocument FromScope(
        CampaignMission mission,
        ZrdrSet scope) =>
        new(mission, scope, sourceLocation: null);

    internal static CampaignMissionDocument FromScope(
        CampaignMission mission,
        ZrdrSet scope,
        string sourceLocation) =>
        new(mission, scope, sourceLocation);

    /// <summary>Returns one value addressed by reader position and raw list indexes.</summary>
    public object? ValueAt(int fileIndex, IReadOnlyList<int> path)
    {
        ZrdrFile file = FileAt(fileIndex);
        object? value = file.Root;
        foreach (int index in path)
        {
            if (value is not List<object?> list)
            {
                throw new InvalidOperationException(
                    Invariant(
                        $"'{file.Key}' path enters a scalar before index {index}"));
            }

            if ((uint)index >= (uint)list.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(path),
                    Invariant(
                        $"'{file.Key}' path index {index} is outside a {list.Count}-item list"));
            }

            value = list[index];
        }

        return value;
    }

    /// <summary>
    /// Replaces an existing string or float leaf with the same type. Returns whether its value
    /// changed. Structural edits and number-to-string conversions are rejected.
    /// </summary>
    public bool SetLeaf(int fileIndex, IReadOnlyList<int> path, object value)
    {
        if (path.Count == 0)
        {
            throw new ArgumentException("a leaf path cannot be empty", nameof(path));
        }

        ZrdrFile file = FileAt(fileIndex);
        List<object?> parent = file.Root;
        for (int depth = 0; depth < path.Count - 1; depth++)
        {
            int index = path[depth];
            if ((uint)index >= (uint)parent.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(path),
                    Invariant(
                        $"'{file.Key}' path index {index} is outside a {parent.Count}-item list"));
            }

            parent = parent[index] as List<object?>
                ?? throw new InvalidOperationException(
                    Invariant(
                        $"'{file.Key}' path enters a scalar at depth {depth}"));
        }

        int leafIndex = path[^1];
        if ((uint)leafIndex >= (uint)parent.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(path),
                Invariant(
                    $"'{file.Key}' leaf index {leafIndex} is outside a {parent.Count}-item list"));
        }

        object? old = parent[leafIndex];
        bool sameType = (old is string && value is string)
            || (old is float && value is float);
        if (!sameType)
        {
            throw new InvalidOperationException(
                $"'{file.Key}' leaf edits must preserve string or float type");
        }

        if (value is float number && !float.IsFinite(number))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "ZRDR JSON cannot represent a non-finite float");
        }

        if (SameScalar(old!, value))
        {
            return false;
        }

        parent[leafIndex] = value;
        IsDirty = true;
        return true;
    }

    /// <summary>
    /// Writes the complete scope beneath <paramref name="modFolder"/> as the mission's
    /// <c>zrdr.zip</c>. The output may not be inside the retail source or project checkout.
    /// </summary>
    public string Export(string modFolder, string? projectRoot = null)
    {
        if (string.IsNullOrWhiteSpace(modFolder))
        {
            throw new ArgumentException("a modding folder is required", nameof(modFolder));
        }

        string root = Path.GetFullPath(modFolder);
        EnsureOutside(root, _sourceLocation, "retail source");
        EnsureOutside(root, projectRoot, "project checkout");

        string output = Path.Combine(
            root,
            ModdingFolder.OutputOf(
                    ArchivePath(Mission))
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        string missionDir = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException(
                $"mission output '{output}' has no directory");
        string unpacked = Path.ChangeExtension(output, null);
        if (Directory.Exists(unpacked))
        {
            throw new IOException(
                $"'{unpacked}' already owns this scope; remove it or choose another modding folder");
        }
        Directory.CreateDirectory(missionDir);
        Directory.CreateDirectory(missionDir);
        string temporary = Path.Combine(
            missionDir,
            $".zrdr-{Guid.NewGuid():N}.tmp");

        try
        {
            WriteZip(temporary);
            VerifyZip(temporary);
            File.Move(temporary, output, overwrite: true);
            IsDirty = false;
            return output;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>The retail path of one campaign mission's reader scope.</summary>
    public static string ArchivePath(CampaignMission mission) =>
        $"ZBD/{mission.ChapterFolder}/{mission.MissionFolder}/zrdr.zbd";

    private static void EnsureOutside(
        string outputRoot,
        string? forbiddenRoot,
        string label)
    {
        if (string.IsNullOrWhiteSpace(forbiddenRoot)
            || !Directory.Exists(forbiddenRoot))
        {
            return;
        }

        string forbidden = Path.GetFullPath(forbiddenRoot);
        string relative = Path.GetRelativePath(forbidden, outputRoot);
        bool outside = relative == ".."
            || relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative);
        if (!outside)
        {
            throw new InvalidOperationException(
                $"the modding folder cannot be inside the {label} '{forbidden}'");
        }
    }

    private void WriteZip(string path)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: false);

        foreach (var file in _files)
        {
            string entryName = file.Key + ".json";
            if (file.Key.Length == 0
                || file.Key.Contains('/')
                || file.Key.Contains('\\')
                || !names.Add(entryName))
            {
                throw new InvalidDataException(
                    $"reader name '{file.Key}' cannot be written as a unique ZIP entry");
            }

            var entry = archive.CreateEntry(
                entryName,
                CompressionLevel.Optimal);
            entry.LastWriteTime = ZipTimestamp;
            using var entryStream = entry.Open();
            using var json = new Utf8JsonWriter(
                entryStream,
                new JsonWriterOptions { Indented = true });
            if (file.Root.Count == 0)
            {
                json.WriteNullValue();
            }
            else
            {
                WriteList(json, file.Root, file.Key);
            }
        }
    }

    private void VerifyZip(string path)
    {
        ZrdrSet reopened = ZrdrSet.FromExtracted(path);
        if (!reopened.Exists || reopened.Files.Count != _files.Count)
        {
            throw new InvalidDataException(
                Invariant(
                    $"export reopened with {reopened.Files.Count} readers, expected {_files.Count}"));
        }

        for (int i = 0; i < _files.Count; i++)
        {
            ZrdrFile expected = _files[i];
            ZrdrFile actual = reopened.Files[i];
            if (!string.Equals(expected.Key, actual.Key, StringComparison.Ordinal)
                || !SameList(expected.Root, actual.Root))
            {
                throw new InvalidDataException(
                    Invariant(
                        $"export round-trip changed reader {i} '{expected.Key}'"));
            }
        }
    }

    private static void WriteList(
        Utf8JsonWriter json,
        List<object?> list,
        string reader)
    {
        json.WriteStartArray();
        foreach (object? value in list)
        {
            switch (value)
            {
                case null:
                    json.WriteNullValue();
                    break;
                case string text:
                    json.WriteStringValue(text);
                    break;
                case float number when float.IsFinite(number):
                    json.WriteNumberValue(number);
                    break;
                case float:
                    throw new InvalidDataException(
                        $"reader '{reader}' contains a non-finite float");
                case List<object?> nested:
                    WriteList(json, nested, reader);
                    break;
                default:
                    throw new InvalidDataException(
                        $"reader '{reader}' contains unsupported value type '{value.GetType().Name}'");
            }
        }

        json.WriteEndArray();
    }

    private static bool SameList(
        List<object?> expected,
        List<object?> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            object? a = expected[i];
            object? b = actual[i];
            bool same = (a, b) switch
            {
                (null, null) => true,
                (string left, string right) =>
                    string.Equals(left, right, StringComparison.Ordinal),
                (float left, float right) =>
                    BitConverter.SingleToUInt32Bits(left)
                    == BitConverter.SingleToUInt32Bits(right),
                (List<object?> left, List<object?> right) =>
                    SameList(left, right),
                _ => false,
            };
            if (!same)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameScalar(object left, object right) =>
        (left, right) switch
        {
            (string a, string b) =>
                string.Equals(a, b, StringComparison.Ordinal),
            (float a, float b) =>
                BitConverter.SingleToUInt32Bits(a)
                == BitConverter.SingleToUInt32Bits(b),
            _ => false,
        };

    private ZrdrFile FileAt(int index) =>
        (uint)index < (uint)_files.Count
            ? _files[index]
            : throw new ArgumentOutOfRangeException(
                nameof(index),
                Invariant(
                    $"reader index {index} is outside the {_files.Count}-reader scope"));

    private static string Invariant(FormattableString value) =>
        FormattableString.Invariant(value);

    private static List<object?> Copy(List<object?> source)
    {
        var copy = new List<object?>(source.Count);
        foreach (object? value in source)
        {
            copy.Add(value is List<object?> nested ? Copy(nested) : value);
        }

        return copy;
    }
}

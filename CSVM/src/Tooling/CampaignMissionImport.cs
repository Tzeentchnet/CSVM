using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using CSVM.Extraction;
using CSVM.Mech3;

namespace CSVM.Tooling;

internal enum CampaignMissionImportKind
{
    Zip,
    Directory,
}

internal sealed record CampaignMissionImportResult(
    CampaignMissionDocument Document,
    string SourcePath,
    CampaignMissionImportKind SourceKind,
    IReadOnlyList<string> AdditionalReaders);

internal static class CampaignMissionImport
{
    internal static CampaignMissionImportResult Load(
        IGameFileSource retailSource,
        CampaignMission mission,
        string selectedPath)
    {
        ArgumentNullException.ThrowIfNull(retailSource);

        ZrdrSet retail = ZrdrSet.Load(
            retailSource,
            CampaignMissionDocument.ArchivePath(mission));
        return Load(
            mission,
            retail,
            retailSource.Location,
            selectedPath);
    }

    internal static CampaignMissionImportResult Load(
        CampaignMission mission,
        ZrdrSet retail,
        string retailLocation,
        string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(retailLocation))
        {
            throw new ArgumentException(
                "The retail source location is required.",
                nameof(retailLocation));
        }

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new ArgumentException(
                "An existing mod reader scope is required.",
                nameof(selectedPath));
        }

        string path = Path.GetFullPath(selectedPath);
        (CampaignMissionImportKind kind, IReadOnlyList<string> inspected) =
            Inspect(path);
        ZrdrSet mod = ZrdrSet.FromExtracted(path);
        VerifyInspectedInventory(kind, inspected, mod);
        IReadOnlyList<string> additional =
            VerifyCompleteInventory(retail, mod);
        CampaignMissionDocument document =
            CampaignMissionDocument.FromScope(
                mission,
                mod,
                Path.GetFullPath(retailLocation));
        return new CampaignMissionImportResult(
            document,
            path,
            kind,
            additional);
    }

    private static (
        CampaignMissionImportKind Kind,
        IReadOnlyList<string> Readers) Inspect(string path)
    {
        if (File.Exists(path))
        {
            if (!string.Equals(
                    Path.GetFileName(path),
                    "zrdr.zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The selected archive must be named zrdr.zip.");
            }

            string unpacked = SessionPaths.PreferUnzipped(path);
            if (Directory.Exists(unpacked))
            {
                throw new InvalidDataException(
                    $"The unpacked scope '{unpacked}' takes precedence over the selected archive.");
            }

            return (CampaignMissionImportKind.Zip, InspectZip(path));
        }

        if (Directory.Exists(path))
        {
            if (!string.Equals(
                    new DirectoryInfo(path).Name,
                    "zrdr",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The selected unpacked scope must be named zrdr.");
            }

            return (
                CampaignMissionImportKind.Directory,
                InspectDirectory(path));
        }

        throw new FileNotFoundException(
            $"Existing mod reader scope '{path}' was not found.",
            path);
    }

    private static IReadOnlyList<string> InspectZip(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        var readers = new List<string>(archive.Entries.Count);
        var unique = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string fullName = entry.FullName;
            if (fullName.Length == 0
                || fullName.IndexOfAny(new[] { '/', '\\' }) >= 0
                || !string.Equals(
                    fullName,
                    entry.Name,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Nested or directory ZIP entry '{fullName}' is not a reader.");
            }

            string key = ReaderKey(fullName);
            if (!unique.Add(key))
            {
                throw new InvalidDataException(
                    $"Duplicate mod reader '{key}'.");
            }

            using Stream stream = entry.Open();
            ValidateJson(stream, fullName);
            readers.Add(key);
        }

        return readers.AsReadOnly();
    }

    private static IReadOnlyList<string> InspectDirectory(string path)
    {
        var readers = new List<string>();
        var unique = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (!File.Exists(entry))
            {
                throw new InvalidDataException(
                    $"Nested directory '{Path.GetFileName(entry)}' is not a reader.");
            }

            string fileName = Path.GetFileName(entry);
            string key = ReaderKey(fileName);
            if (!unique.Add(key))
            {
                throw new InvalidDataException(
                    $"Duplicate mod reader '{key}'.");
            }

            using FileStream stream = File.OpenRead(entry);
            ValidateJson(stream, fileName);
            readers.Add(key);
        }

        return readers.AsReadOnly();
    }

    private static string ReaderKey(string fileName)
    {
        const string Suffix = ".zrd.json";
        if (!fileName.EndsWith(Suffix, StringComparison.Ordinal)
            || fileName.Length == Suffix.Length)
        {
            throw new InvalidDataException(
                $"Unsupported mod-scope entry '{fileName}'.");
        }

        return fileName[..^".json".Length];
    }

    private static void ValidateJson(Stream stream, string name)
    {
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        if (root.ValueKind is not JsonValueKind.Array
            and not JsonValueKind.Null)
        {
            throw new InvalidDataException(
                $"Reader '{name}' must contain an array or null root.");
        }

        ValidateJsonValue(root, name);
    }

    private static void ValidateJsonValue(JsonElement value, string name)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (JsonElement child in value.EnumerateArray())
                {
                    ValidateJsonValue(child, name);
                }

                return;
            case JsonValueKind.String:
            case JsonValueKind.Null:
                return;
            case JsonValueKind.Number:
                if (value.TryGetSingle(out float number)
                    && float.IsFinite(number))
                {
                    return;
                }

                throw new InvalidDataException(
                    $"Reader '{name}' contains a number outside the finite float range.");
            default:
                throw new InvalidDataException(
                    $"Reader '{name}' contains unsupported JSON value {value.ValueKind}.");
        }
    }

    private static void VerifyInspectedInventory(
        CampaignMissionImportKind kind,
        IReadOnlyList<string> inspected,
        ZrdrSet scope)
    {
        if (!scope.Exists || scope.Files.Count == 0)
        {
            throw new InvalidDataException(
                "The selected mod reader scope is empty.");
        }

        if (scope.Files.Count != inspected.Count)
        {
            throw new InvalidDataException(
                "The decoded mod reader inventory differs from its inspected layout.");
        }

        if (kind == CampaignMissionImportKind.Zip)
        {
            for (int i = 0; i < inspected.Count; i++)
            {
                if (!string.Equals(
                        inspected[i],
                        scope.Files[i].Key,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The decoded ZIP reader order or spelling changed.");
                }
            }

            return;
        }

        var decoded = new HashSet<string>(
            scope.Files.Select(file => file.Key),
            StringComparer.OrdinalIgnoreCase);
        if (decoded.Count != inspected.Count
            || inspected.Any(reader => !decoded.Contains(reader)))
        {
            throw new InvalidDataException(
                "The decoded directory reader inventory changed.");
        }
    }

    private static IReadOnlyList<string> VerifyCompleteInventory(
        ZrdrSet retail,
        ZrdrSet mod)
    {
        if (!retail.Exists || retail.Files.Count == 0)
        {
            throw new InvalidDataException(
                "The selected retail mission reader scope is missing or empty.");
        }

        var retailKeys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (ZrdrFile file in retail.Files)
        {
            if (!retailKeys.Add(file.Key))
            {
                throw new InvalidDataException(
                    $"Duplicate retail reader '{file.Key}'.");
            }
        }

        var modKeys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var additional = new List<string>();
        foreach (ZrdrFile file in mod.Files)
        {
            if (!modKeys.Add(file.Key))
            {
                throw new InvalidDataException(
                    $"Duplicate mod reader '{file.Key}'.");
            }

            if (!retailKeys.Contains(file.Key))
            {
                additional.Add(file.Key);
            }
        }

        var missing = new List<string>();
        foreach (ZrdrFile file in retail.Files)
        {
            if (!modKeys.Contains(file.Key))
            {
                missing.Add(file.Key);
            }
        }

        if (missing.Count != 0)
        {
            throw new InvalidDataException(
                $"Incomplete mod reader scope; missing: {string.Join(", ", missing)}.");
        }

        return additional.AsReadOnly();
    }
}

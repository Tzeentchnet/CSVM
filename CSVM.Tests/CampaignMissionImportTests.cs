using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using CSVM.Extraction;
using CSVM.Mech3;
using CSVM.Tooling;
using Xunit;

namespace CSVM.Tests;

/// <summary>Complete existing mod-scope import over invented ZIP and directory layouts.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionImportTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void ZipImportPreservesOrderBitsExtraReadersAndRoundTrips()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        ZrdrSet retail = Scope(
            File("objectives.zrd", "retail"),
            File("weather.zrd", 1f));
        ZrdrSet mod = Scope(
            File("weather.zrd", -0f, subnormal),
            File("objectives.zrd", "edited"),
            File("extra.zrd", null, "kept"));
        string sourceRoot = TestData.TempDir();
        string zip = CampaignMissionDocument
            .FromScope(Mission, mod)
            .Export(sourceRoot);
        string retailLocation =
            Path.Combine(TestData.TempDir(), "retail");

        CampaignMissionImportResult imported =
            CampaignMissionImport.Load(
                Mission,
                retail,
                retailLocation,
                zip);

        Assert.Equal(CampaignMissionImportKind.Zip, imported.SourceKind);
        Assert.Equal(Path.GetFullPath(zip), imported.SourcePath);
        Assert.Equal(
            new[] { "weather.zrd", "objectives.zrd", "extra.zrd" },
            imported.Document.Files.Select(file => file.Key));
        Assert.Equal(new[] { "extra.zrd" }, imported.AdditionalReaders);
        Assert.Equal(
            0x80000000u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(
                    imported.Document.ValueAt(0, new[] { 0 }))));
        Assert.Equal(
            1u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(
                    imported.Document.ValueAt(0, new[] { 1 }))));

        Assert.True(
            imported.Document.SetLeaf(1, new[] { 0 }, "edited again"));
        string outputRoot = TestData.TempDir();
        string output = imported.Document.Export(outputRoot);
        ZrdrSet reopened = ZrdrSet.FromExtracted(output);

        Assert.Equal(
            new[] { "weather.zrd", "objectives.zrd", "extra.zrd" },
            reopened.Files.Select(file => file.Key));
        Assert.Equal(
            "edited again",
            Assert.IsType<string>(
                reopened.LoadFile("objectives.zrd")[0]));
    }

    [Fact]
    public void DirectoryImportUsesTheEffectiveUnpackedOwner()
    {
        ZrdrSet retail = Scope(
            File("objectives.zrd", "retail"),
            File("weather.zrd", 1f));
        ZrdrSet mod = Scope(
            File("weather.zrd", 2f),
            File("objectives.zrd", "edited"));
        string root = TestData.TempDir();
        string zip = CampaignMissionDocument
            .FromScope(Mission, mod)
            .Export(root);
        string directory = UnpackedSibling(zip);
        ZipFile.ExtractToDirectory(zip, directory);

        CampaignMissionImportResult imported =
            CampaignMissionImport.Load(
                Mission,
                retail,
                Path.Combine(TestData.TempDir(), "retail"),
                directory);

        Assert.Equal(
            CampaignMissionImportKind.Directory,
            imported.SourceKind);
        Assert.Equal(Path.GetFullPath(directory), imported.SourcePath);
        Assert.Empty(imported.AdditionalReaders);
        Assert.Equal(
            Directory
                .EnumerateFiles(directory)
                .Select(path => Path.GetFileName(path)[..^".json".Length]),
            imported.Document.Files.Select(file => file.Key),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportAcceptsNullAndEmptyReaderTrees()
    {
        ZrdrSet retail = Scope(
            File("null.zrd", "retail"),
            File("empty.zrd", "retail"));
        string zip = WriteZip(
            ("null.zrd.json", "null"),
            ("empty.zrd.json", "[]"));

        CampaignMissionImportResult imported =
            CampaignMissionImport.Load(
                Mission,
                retail,
                Path.Combine(TestData.TempDir(), "retail"),
                zip);

        Assert.Equal(2, imported.Document.Files.Count);
        Assert.Empty(imported.Document.Files[0].Root);
        Assert.Empty(imported.Document.Files[1].Root);
    }

    [Fact]
    public void ImportRejectsPartialScopeWithoutRetailFallback()
    {
        ZrdrSet retail = Scope(
            File("objectives.zrd", "retail"),
            File("weather.zrd", 1f));
        string zip = CampaignMissionDocument
            .FromScope(
                Mission,
                Scope(File("objectives.zrd", "mod")))
            .Export(TestData.TempDir());

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => CampaignMissionImport.Load(
                Mission,
                retail,
                Path.Combine(TestData.TempDir(), "retail"),
                zip));

        Assert.Contains("weather.zrd", error.Message);
    }

    [Theory]
    [InlineData("nested/probe.zrd.json", "[]", "Nested")]
    [InlineData("readme.txt", "[]", "Unsupported")]
    [InlineData("probe.zrd.json", "{}", "array or null")]
    [InlineData("probe.zrd.json", "[true]", "unsupported JSON")]
    public void ImportRejectsUnsupportedOrMalformedLayouts(
        string name,
        string json,
        string expected)
    {
        string zip = WriteZip((name, json));

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => CampaignMissionImport.Load(
                Mission,
                Scope(File("probe.zrd", "retail")),
                Path.Combine(TestData.TempDir(), "retail"),
                zip));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void ImportRejectsMalformedJson()
    {
        string zip = WriteZip(("probe.zrd.json", "["));

        Assert.ThrowsAny<JsonException>(
            () => CampaignMissionImport.Load(
                Mission,
                Scope(File("probe.zrd", "retail")),
                Path.Combine(TestData.TempDir(), "retail"),
                zip));
    }

    [Fact]
    public void ImportRejectsCaseInsensitiveDuplicateReaders()
    {
        string zip = WriteZip(
            ("probe.zrd.json", "[]"),
            ("PROBE.zrd.json", "[]"));

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => CampaignMissionImport.Load(
                Mission,
                Scope(File("probe.zrd", "retail")),
                Path.Combine(TestData.TempDir(), "retail"),
                zip));

        Assert.Contains("Duplicate", error.Message);
    }

    [Fact]
    public void ImportRejectsZipWhoseUnpackedSiblingTakesPrecedence()
    {
        string zip = WriteZip(("probe.zrd.json", "[]"));
        Directory.CreateDirectory(UnpackedSibling(zip));

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => CampaignMissionImport.Load(
                Mission,
                Scope(File("probe.zrd", "retail")),
                Path.Combine(TestData.TempDir(), "retail"),
                zip));

        Assert.Contains("takes precedence", error.Message);
    }

    private static ZrdrFile File(
        string name,
        params object?[] values) =>
        new(name, null, new List<object?>(values));

    private static ZrdrSet Scope(params ZrdrFile[] files) =>
        new("invented mission zrdr.zbd", files);

    private static string UnpackedSibling(string zip) =>
        Path.Combine(Path.GetDirectoryName(zip)!, Path.GetFileNameWithoutExtension(zip));

    private static string WriteZip(
        params (string Name, string Json)[] entries)
    {
        string path = Path.Combine(TestData.TempDir(), "zrdr.zip");
        using ZipArchive archive = ZipFile.Open(
            path,
            ZipArchiveMode.Create);
        foreach ((string name, string json) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                name,
                CompressionLevel.NoCompression);
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                leaveOpen: false);
            writer.Write(json);
        }

        return path;
    }
}

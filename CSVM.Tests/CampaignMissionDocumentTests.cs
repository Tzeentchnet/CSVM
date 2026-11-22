using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>
/// The campaign editor's lossless document and complete-scope export over invented reader trees.
/// No retail data is needed.
/// </summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionDocumentTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void ScalarEditsPreserveTypeStructureAndFloatBits()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        var sourceRoot = new List<object?>
        {
            "BLOCK",
            new List<object?>
            {
                "LABEL",
                new List<object?> { "before" },
                "VALUES",
                new List<object?> { -0f, subnormal },
                "FLAG",
                null,
            },
        };
        var document = CampaignMissionDocument.FromScope(
            Mission,
            Scope(new ZrdrFile("probe.zrd", null, sourceRoot)));

        sourceRoot.Clear();
        Assert.Equal("before", document.ValueAt(0, new[] { 1, 1, 0 }));
        Assert.False(document.IsDirty);
        Assert.False(document.SetLeaf(0, new[] { 1, 1, 0 }, "before"));
        Assert.True(document.SetLeaf(0, new[] { 1, 1, 0 }, "after"));
        Assert.True(document.SetLeaf(0, new[] { 1, 3, 0 }, 0f));

        Assert.True(document.IsDirty);
        Assert.Equal("after", document.ValueAt(0, new[] { 1, 1, 0 }));
        Assert.Equal(
            0u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(
                    document.ValueAt(0, new[] { 1, 3, 0 }))));
        Assert.Equal(
            1u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(
                    document.ValueAt(0, new[] { 1, 3, 1 }))));
        Assert.Null(document.ValueAt(0, new[] { 1, 5 }));

        Assert.Throws<InvalidOperationException>(
            () => document.SetLeaf(0, new[] { 1, 1, 0 }, 1f));
        Assert.Throws<InvalidOperationException>(
            () => document.SetLeaf(0, new[] { 1, 5 }, "not null"));
        Assert.Throws<ArgumentException>(
            () => document.SetLeaf(0, Array.Empty<int>(), "root"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => document.SetLeaf(0, new[] { 1, 99 }, "missing"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => document.SetLeaf(0, new[] { 1, 3, 0 }, float.NaN));
    }

    [Fact]
    public void ExportWritesEveryReaderInOrderAndRoundTripsExactly()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        var document = CampaignMissionDocument.FromScope(
            Mission,
            Scope(
                new ZrdrFile(
                    "objectives.zrd",
                    null,
                    new List<object?>
                    {
                        "VALUES",
                        new List<object?> { -0f, subnormal, null, "text" },
                    }),
                new ZrdrFile("empty.zrd", null, new List<object?>()),
                new ZrdrFile(
                    "repeat.zrd",
                    "repeat-1.zrd",
                    new List<object?> { "kept" })));
        document.SetLeaf(0, new[] { 1, 3 }, "edited");
        string modRoot = TestData.TempDir();

        string output = document.Export(modRoot);

        Assert.Equal(
            Path.Combine(modRoot, "C3", "M01", "zrdr.zip"),
            output);
        Assert.False(document.IsDirty);
        using (var archive = ZipFile.OpenRead(output))
        {
            Assert.Equal(
                new[]
                {
                    "objectives.zrd.json",
                    "empty.zrd.json",
                    "repeat-1.zrd.json",
                },
                archive.Entries.Select(e => e.FullName));
            Assert.All(
                archive.Entries,
                entry => Assert.Equal(1980, entry.LastWriteTime.Year));
        }

        ZrdrSet reopened = ZrdrSet.FromExtracted(output);
        Assert.Equal(
            new[] { "objectives.zrd", "empty.zrd", "repeat-1.zrd" },
            reopened.Files.Select(f => f.Key));
        var values = Assert.IsType<List<object?>>(
            reopened.LoadFile("objectives.json")[1]);
        Assert.Equal(
            0x80000000u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(values[0])));
        Assert.Equal(
            1u,
            BitConverter.SingleToUInt32Bits(
                Assert.IsType<float>(values[1])));
        Assert.Null(values[2]);
        Assert.Equal("edited", values[3]);
        Assert.Empty(reopened.LoadFileOrEmpty("empty.json"));
    }

    [Fact]
    public void ExportRefusesAnUnpackedOwnerOrAPathInsideTheProject()
    {
        var document = CampaignMissionDocument.FromScope(
            Mission,
            Scope(new ZrdrFile(
                "objectives.zrd",
                null,
                new List<object?> { "kept" })));
        string root = TestData.TempDir();
        string unpacked = Path.Combine(root, "C3", "M01", "zrdr");
        Directory.CreateDirectory(unpacked);

        IOException owner = Assert.Throws<IOException>(
            () => document.Export(root));
        Assert.Contains(unpacked, owner.Message, StringComparison.Ordinal);

        string projectRoot = Path.Combine(root, "project");
        Directory.CreateDirectory(projectRoot);
        string nested = Path.Combine(projectRoot, "mods");
        InvalidOperationException project = Assert.Throws<InvalidOperationException>(
            () => document.Export(nested, projectRoot));
        Assert.Contains("project checkout", project.Message, StringComparison.Ordinal);

        var sourced = CampaignMissionDocument.FromScope(
            Mission,
            Scope(new ZrdrFile(
                "objectives.zrd",
                null,
                new List<object?> { "kept" })),
            projectRoot);
        InvalidOperationException retail = Assert.Throws<InvalidOperationException>(
            () => sourced.Export(nested));
        Assert.Contains("retail source", retail.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidScopesAndValuesFailBeforePublishingAnArchive()
    {
        Assert.Throws<FileNotFoundException>(
            () => CampaignMissionDocument.FromScope(Mission, ZrdrSet.None));

        var document = CampaignMissionDocument.FromScope(
            Mission,
            Scope(new ZrdrFile(
                "broken.zrd",
                null,
                new List<object?> { true })));
        string root = TestData.TempDir();

        Assert.Throws<InvalidDataException>(
            () => document.Export(root));
        Assert.False(
            File.Exists(Path.Combine(root, "C3", "M01", "zrdr.zip")));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.Combine(root, "C3", "M01"),
                "*.tmp"));
    }

    [Fact]
    public void GodotProjectEnablesTheEditorPlugin()
    {
        Assert.True(
            typeof(EditorPlugin).IsAssignableFrom(
                typeof(CampaignMissionEditorPlugin)));

        string plugin =
            "res://addons/campaign_mission_editor/plugin.cfg";
        string project = File.ReadAllText(
            Path.Combine(TestData.RepoRoot, "CSVM", "project.godot"));
        string pluginDirectory = Path.Combine(
            TestData.RepoRoot,
            "CSVM",
            "addons",
            "campaign_mission_editor");
        string config = File.ReadAllText(
            Path.Combine(pluginDirectory, "plugin.cfg"));

        Assert.Contains(plugin, project, StringComparison.Ordinal);
        const string script =
            "../../src/Tooling/CampaignMissionEditorPlugin.cs";
        Assert.Contains(
            $"script=\"{script}\"",
            config,
            StringComparison.Ordinal);
        Assert.True(
            File.Exists(
                Path.GetFullPath(
                    Path.Combine(
                        pluginDirectory,
                        script.Replace(
                            '/',
                            Path.DirectorySeparatorChar)))));
    }

    private static ZrdrSet Scope(params ZrdrFile[] files) =>
        new("invented mission zrdr.zbd", files);
}

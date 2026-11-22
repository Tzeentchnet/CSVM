using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CSVM.Extraction;
using CSVM.Mech3;
using CSVM.Spec;
using Xunit;

namespace CSVM.Tests;

/// <summary>The campaign editor's lightweight retail dependency load over invented binaries.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignEditorLoadTests
{
    [Fact]
    public void LoadsInventedInputsAndClosesEverySourceStream()
    {
        TrackingSource source = Complete();

        GameData data = GameData.LoadCampaignEditor(source);

        InterpScripts scripts = Assert.IsType<InterpScripts>(data.Interp);
        Assert.Equal(
            "Quit",
            Assert.Single(scripts.Lines(@"support\c3\m01.gw")!));
        Assert.Equal(1, data.Messages.Count);
        Assert.Equal("Probe text", data.Messages.Get("msg_probe"));
        Assert.True(data.Zrdr.Exists);
        Assert.Equal(
            "probe",
            Assert.IsType<string>(
                Assert.Single(data.Zrdr.LoadFile("probe.zrd"))));
        Assert.Null(data.Sounds);
        Assert.Null(data.Planes);
        Assert.Same(TextureSet.Empty, data.Rimage);

        _ = data.LoadChapter("c3", "m01");

        AssertClosed(source);
    }

    [Theory]
    [InlineData(InterpScripts.GamePath)]
    [InlineData(Messages.GameFile)]
    [InlineData(GameData.RootZrdrPath)]
    public void DecodeFailureFallsBackAndClosesEverySourceStream(
        string badPath)
    {
        TrackingSource source = Complete();
        source.Put(badPath, new byte[] { 0 });

        GameData data = GameData.LoadCampaignEditor(source);

        if (badPath == InterpScripts.GamePath)
        {
            Assert.Null(data.Interp);
        }
        else
        {
            Assert.NotNull(data.Interp);
        }

        if (badPath == Messages.GameFile)
        {
            Assert.Equal(0, data.Messages.Count);
            Assert.Equal("MSG_PROBE", data.Messages.Get("MSG_PROBE"));
        }
        else
        {
            Assert.Equal("Probe text", data.Messages.Get("MSG_PROBE"));
        }

        if (badPath == GameData.RootZrdrPath)
        {
            Assert.Same(ZrdrSet.None, data.Zrdr);
        }
        else
        {
            Assert.True(data.Zrdr.Exists);
        }

        AssertClosed(source);
    }

    private static void AssertClosed(TrackingSource source)
    {
        Assert.Equal(
            new[]
            {
                InterpScripts.GamePath,
                Messages.GameFile,
                GameData.RootZrdrPath,
            },
            source.Opened.Select(stream => stream.Path).ToArray());
        Assert.All(
            source.Opened,
            stream =>
            {
                Assert.True(
                    stream.Disposed,
                    $"Not disposed: {stream.Path}");
                Assert.False(stream.CanRead);
            });
    }

    private static TrackingSource Complete()
    {
        var source = new TrackingSource();
        source.Put(InterpScripts.GamePath, Interp());
        source.Put(Messages.GameFile, StringsDll());
        source.Put(GameData.RootZrdrPath, SharedZrdr());
        source.Put(GameData.SoundsPath, new byte[] { 0 });
        source.Put(GameData.PlanesPath, new byte[] { 0 });
        source.Put(GameData.RimagePath, new byte[] { 0 });
        return source;
    }

    private static byte[] SharedZrdr()
    {
        using var file = new MemoryStream();
        using var writer =
            new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);

        byte[] value = Encoding.ASCII.GetBytes("probe");
        writer.Write(4u);
        writer.Write(2u);
        writer.Write(3u);
        writer.Write(value.Length);
        writer.Write(value);
        uint payloadLength = (uint)file.Length;

        writer.Write(0u);
        writer.Write(payloadLength);
        var name = new byte[140];
        Encoding.ASCII.GetBytes("probe.zrd").CopyTo(name, 0);
        writer.Write(name);
        writer.Write(1u);
        writer.Write(1u);
        return file.ToArray();
    }

    private static byte[] Interp()
    {
        using var file = new MemoryStream();
        using var writer =
            new BinaryWriter(file, Encoding.ASCII, leaveOpen: true);

        writer.Write(0x08971119u);
        writer.Write(7u);
        writer.Write(1u);
        var name = new byte[120];
        Encoding.ASCII.GetBytes(@"support\c3\m01.gw").CopyTo(name, 0);
        writer.Write(name);
        writer.Write(0u);
        writer.Write(140u);
        writer.Write(5u);
        writer.Write(1u);
        writer.Write(Encoding.ASCII.GetBytes("Quit\0"));
        writer.Write(0u);
        return file.ToArray();
    }

    private static byte[] StringsDll()
    {
        const uint ImageBase = 0x10000000;
        const uint DataRva = 0x8000;

        byte[] resource = ExtractionFixtures.StringTablePe(
            3,
            new string?[] { "Probe text" });
        byte[] name = Encoding.ASCII.GetBytes("MSG_PROBE\0");
        var data = new byte[56 + name.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(48),
            ImageBase + DataRva + 56);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(52), 32);
        name.CopyTo(data, 56);

        var pe = new byte[resource.Length + data.Length];
        resource.CopyTo(pe, 0);
        data.CopyTo(pe, resource.Length);

        int optional = 0x40 + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(0x40 + 6), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            pe.AsSpan(optional + 28),
            ImageBase);
        int section = optional + 224 + 40;
        Encoding.ASCII.GetBytes(".data").CopyTo(pe, section);
        BinaryPrimitives.WriteUInt32LittleEndian(
            pe.AsSpan(section + 8),
            (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            pe.AsSpan(section + 12),
            DataRva);
        BinaryPrimitives.WriteUInt32LittleEndian(
            pe.AsSpan(section + 16),
            (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            pe.AsSpan(section + 20),
            (uint)resource.Length);
        return pe;
    }

    private sealed class TrackingSource : IGameFileSource
    {
        private readonly Dictionary<string, byte[]> _files =
            new(StringComparer.OrdinalIgnoreCase);

        public List<TrackingStream> Opened { get; } = new();

        public string Location => "invented";

        public void Put(string path, byte[] bytes) =>
            _files[Normalize(path)] = bytes;

        public bool Exists(string path) =>
            _files.ContainsKey(Normalize(path));

        public Stream? Open(string path)
        {
            if (!_files.TryGetValue(Normalize(path), out byte[]? bytes))
            {
                return null;
            }

            var stream = new TrackingStream(path, bytes);
            Opened.Add(stream);
            return stream;
        }

        private static string Normalize(string path) =>
            path.Replace('\\', '/');
    }

    private sealed class TrackingStream : MemoryStream
    {
        internal TrackingStream(string path, byte[] bytes)
            : base(bytes, writable: false)
        {
            Path = path;
        }

        internal string Path { get; }

        internal bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}

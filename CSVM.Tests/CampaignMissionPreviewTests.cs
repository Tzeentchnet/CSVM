using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Flight.Modes;
using CSVM.Mech3;
using CSVM.Tooling;
using Godot;
using Xunit;

namespace CSVM.Tests;

/// <summary>Invented mission projections, source identity, refresh and disposal.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignMissionPreviewTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void StartsUseRuntimeAxesAndRetainTheSelectedRawPaths()
    {
        CampaignMissionDocument document = Document(
            Objectives(Row(
                "PLAYER_INIT", Player(1f, 2f, 3f, 10f),
                "other", Row("keep"),
                "player_init", Player(10f, 20f, 30f, 90f))),
            Roster(Row(
                Row(0f, "Player", 1f, "Wing", 2f, "Template"),
                Row("player", Fields(200f, 201f, 202f, 30f, true)),
                Row("wing", Fields(40f, 50f, 60f, -45f, true)),
                Row("template", Fields(70f, 80f, 90f, 5f, false)))));
        var world = new FakeWorld();
        using var preview = Preview(document, world);
        Assert.Equal(CampaignPreviewLayers.All, preview.Refresh());

        CampaignPreviewItem player = Assert.Single(preview.Items(CampaignPreviewLayers.Player));
        PlayerStart runtime = SpawnPoints.LoadPlayerInit(new ZrdrSet("mission", document.Files))!.Value;
        Assert.Equal(runtime.Spawn.Position, player.Pose.Origin);
        Near(runtime.Spawn.Forward, -player.Pose.Basis.Z);
        Assert.Equal(ReferenceScope.Mission, player.Source.Scope);
        Assert.Equal("objectives.zrd", player.Source.Container);
        Assert.Equal(0, player.Source.FileIndex);
        Assert.Equal(new[] { 0, 5, 1 }, player.Source.Path);

        CampaignPreviewItem wing = Assert.Single(preview.Items(CampaignPreviewLayers.Roster));
        Assert.Equal("wing", wing.Name);
        Assert.Equal(new Vector3(40f, 50f, 60f), wing.Pose.Origin);
        Near(new Basis(Vector3.Up, Mathf.DegToRad(-45f)) * Vector3.Forward, -wing.Pose.Basis.Z);
        Assert.Equal(new[] { 2, 1, 1 }, wing.Source.Path);
        Assert.DoesNotContain(preview.Items(CampaignPreviewLayers.Roster),
            item => item.Name is "player" or "template");

        world.Poses["wing"] = new Transform3D(
            new Basis(Vector3.Up, Mathf.DegToRad(35f)), new Vector3(500f, 600f, 700f));
        CampaignMissionDocument second = Document(
            Objectives(Row("PLAYER_INIT", Player(10f, 20f, 30f, 90f))),
            Roster(Row(Row(0f, "Wing"), Row("wing", Fields(40f, 50f, 60f, -45f, true)))));
        using var worldPreview = Preview(second, world);
        worldPreview.Refresh();
        wing = Assert.Single(worldPreview.Items(CampaignPreviewLayers.Roster));
        Assert.Equal(world.Poses["wing"], wing.Pose);
        Assert.Contains("takes precedence", wing.Note);
    }

    [Fact]
    public void ZeppelinAndGeneratorUseAuthoredPoseAndHostRelativeOffset()
    {
        var world = new FakeWorld();
        world.Poses["carrier"] = Transform3D.Identity;
        world.Offsets[("carrier", "bay", true)] = new Vector3(4f, 2f, -8f);
        world.Poses["surface"] = new Transform3D(
            new Basis(Vector3.Up, Mathf.DegToRad(-90f)), new Vector3(50f, 10f, 70f));
        world.Offsets[("surface", null, false)] = new Vector3(3f, 1f, 5f);
        CampaignMissionDocument document = Document(
            Zeppelins(Row(Row(
                "node", Row("old"),
                "NODE", Row("carrier"),
                "net", Row("missing_net"),
                "position", Row(1f, 2f, 3f),
                "POSITION", Row(100f, 200f, 300f),
                "yaw", Row(90f),
                "pitch", Row(30f)))),
            Generators(Row(
                Row("node", Row("carrier"), "vehicle", Row("nets", Row("net")),
                    "zeppelin", "origin", Row("bay")),
                Row("node", Row("surface"), "vehicle", Row("nets", Row("net"))))));
        using var preview = Preview(document, world);
        preview.Refresh();

        CampaignPreviewItem zeppelin = Assert.Single(preview.Items(CampaignPreviewLayers.Zeppelins));
        Transform3D expected = new(
            Basis.FromEuler(new Vector3(Mathf.DegToRad(30f), Mathf.DegToRad(90f), 0f), EulerOrder.Yxz),
            new Vector3(100f, 200f, 300f));
        Assert.Equal(expected, zeppelin.Pose);
        Assert.Equal(new[] { 0, 0, 9 }, zeppelin.Source.Path);
        Assert.Contains("startup animation", zeppelin.Note);
        Assert.Equal(expected, world.Poses["carrier"]);

        CampaignPreviewItem drop = Assert.Single(
            preview.Items(CampaignPreviewLayers.Generators).Where(item => item.Name == "carrier"));
        Near(expected * new Vector3(4f, 2f, -8f), drop.Pose.Origin);
        Assert.NotEqual(expected.Origin + new Vector3(4f, 2f, -8f), drop.Pose.Origin);
        Assert.Equal(new[] { 0, 0, 1, 0 }, drop.Source.Path);
        Assert.Equal(new[] { 0, 0, 6, 0 }, drop.RelatedSource!.Path);

        CampaignPreviewItem surface = Assert.Single(
            preview.Items(CampaignPreviewLayers.Generators).Where(item => item.Name == "surface"));
        Vector3 launch = (world.Poses["surface"] * new Vector3(3f, 1f, 5f))
            + (Vector3.Up * 0.2f);
        Near(launch, surface.Pose.Origin);
    }

    [Fact]
    public void TargetsKeepChapterScopeScopedPathsAndObjectivePointPrecedence()
    {
        CampaignMissionDocument document = Document(
            Objectives(Row(
                "PLAYER_INIT", Player(10f, 20f, 30f, 0f),
                "OBJECTIVE1", Row(
                    "TRAVELERS", Row("player", "APPROACHING", Row(8f, 9f, 10f), 40f),
                    "ADD_OBJECTIVE_TARGET", Row("landmark")))));
        ZrdrSet chapter = Scope(Targets(Row(
            Row(Row("nodes", Row(Row("parent", "child"), "landmark", "player")), Row("objective")))));
        var world = new FakeWorld();
        world.Targets["parent/child"] = new Vector3(1f, 2f, 3f);
        world.Targets["landmark"] = new Vector3(1000f, 1000f, 1000f);
        using var preview = Preview(document, world, chapter);
        preview.Refresh();

        CampaignPreviewItem scoped = Assert.Single(
            preview.Items(CampaignPreviewLayers.Targets).Where(item => item.Name == "parent/child"));
        Assert.Equal(new Vector3(1f, 2f, 3f), scoped.Pose.Origin);
        Assert.Equal(ReferenceScope.Chapter, scoped.Source.Scope);
        Assert.Equal("targets.zrd", scoped.Source.Container);
        Assert.Equal(0, scoped.Source.FileIndex);
        Assert.Equal(new[] { 0, 0, 1, 0 }, scoped.Source.Path);

        CampaignPreviewItem landmark = Assert.Single(
            preview.Items(CampaignPreviewLayers.Targets).Where(item => item.Name == "landmark"));
        Assert.Equal(new Vector3(8f, 9f, 10f), landmark.Pose.Origin);
        Assert.Contains("TRAVELERS", landmark.Note);
        Assert.Equal(ReferenceScope.Mission, landmark.RelatedSource!.Scope);
        Assert.Equal(new[] { 0, 3, 1, 2 }, landmark.RelatedSource.Path);
        CampaignPreviewItem player = Assert.Single(
            preview.Items(CampaignPreviewLayers.Targets).Where(item => item.Name == "player"));
        Assert.Equal(new Vector3(10f, 20f, 30f), player.Pose.Origin);
        Assert.Contains("approximation", player.Note);
    }

    [Fact]
    public void RibbonUsesTheWinningWorldNodeAndExactMissionOverridePath()
    {
        var gamez = new GameZ();
        gamez.Nodes.Add(new GameZNode { Index = 0, Name = "dzpath1", MeshIndex = -1 });
        CampaignMissionDangerZonesTests.AddZone(gamez, "dzpath01", 20f);
        CampaignMissionDangerZonesTests.AddZone(gamez, "DZPATH1", 40f);
        CampaignDangerZoneWorld danger = CampaignMissionDangerZonesTests.Project(gamez);
        CampaignMissionDocument document = Document(
            new ZrdrFile("dzones.zrd", null, Row(
                "disable", Row("dzpath1"),
                "objective_numbers", Row(Row("dzpath1", 17f)))));
        using var preview = Preview(document, new FakeWorld(), danger: danger);
        preview.Refresh();

        CampaignPreviewItem ribbon = Assert.Single(preview.Items(CampaignPreviewLayers.Danger));
        Assert.Equal("DZPATH1", ribbon.Name);
        Assert.Equal(ReferenceScope.World, ribbon.Source.Scope);
        Assert.Equal("invented/gamez.zbd", ribbon.Source.Container);
        Assert.Equal(new[] { 2 }, ribbon.Source.Path);
        Assert.Equal(ReferenceScope.Mission, ribbon.RelatedSource!.Scope);
        Assert.Equal(new[] { 3, 0, 1 }, ribbon.RelatedSource.Path);
        Assert.Equal(new[] { 1, 0 }, Assert.Single(ribbon.AdditionalSources).Path);
        Assert.False(ribbon.Enabled);
        Assert.True(ribbon.Points.Length > 2);
        Near(new Vector3(40f, 0f, 0f), ribbon.Points[0]);
        Assert.Contains("no established", preview.LocationStatus);
    }

    [Fact]
    public void NonFiniteRibbonGeometryIsOmittedWithAnExplicitProblem()
    {
        var gamez = new GameZ();
        CampaignMissionDangerZonesTests.AddZone(gamez, "dzpath1", 0f);
        gamez.Meshes[0].Vertices[1] = new Vector3(float.NaN, 0f, 0f);
        CampaignDangerZoneWorld danger = CampaignMissionDangerZonesTests.Project(gamez);
        using var preview = Preview(Document(), new FakeWorld(), danger: danger);

        preview.Refresh();

        Assert.Empty(preview.Items(CampaignPreviewLayers.Danger));
        Assert.Contains("malformed ribbon", preview.Status(CampaignPreviewLayers.Danger));
    }

    [Fact]
    public void ChangeTrackingRebuildsOnlyChangedLayersAndCoalescesEdits()
    {
        var world = new FakeWorld();
        world.Poses["carrier"] = Transform3D.Identity;
        world.Offsets[("carrier", "bay", true)] = new Vector3(2f, 0f, 0f);
        CampaignMissionDocument document = Document(
            Objectives(Row("PLAYER_INIT", Player(1f, 2f, 3f, 0f))),
            Zeppelins(Row(Row("node", Row("carrier"), "net", Row("net"),
                "position", Row(10f, 20f, 30f), "yaw", Row(0f)))),
            Generators(Row(Row("node", Row("carrier"), "vehicle", Row("nets", Row("net")),
                "zeppelin", "origin", Row("bay"), "capacity", Row(4f)))),
            new ZrdrFile("other.zrd", null, Row("unrelated")));
        using var preview = Preview(document, world);
        preview.Refresh();
        IReadOnlyList<CampaignPreviewItem> player = preview.Items(CampaignPreviewLayers.Player);
        IReadOnlyList<CampaignPreviewItem> generators = preview.Items(CampaignPreviewLayers.Generators);
        Assert.True(preview.Select(Assert.Single(player)));
        int placements = world.Placements;

        document.SetLeaf(3, new[] { 0 }, "changed");
        Assert.Equal(CampaignPreviewLayers.None, preview.Dirty);
        Assert.Equal(CampaignPreviewLayers.None, preview.Refresh());
        Assert.Same(player, preview.Items(CampaignPreviewLayers.Player));

        document.SetLeaf(2, new[] { 0, 0, 8, 0 }, 5f);
        Assert.Equal(CampaignPreviewLayers.Generators, preview.Dirty);
        Assert.Equal(CampaignPreviewLayers.None, preview.Refresh());
        Assert.Same(generators, preview.Items(CampaignPreviewLayers.Generators));
        Assert.Equal(placements, world.Placements);

        document.SetLeaf(0, new[] { 0, 1, 1, 0 }, 4f);
        document.SetLeaf(0, new[] { 0, 1, 1, 1 }, 5f);
        Assert.Equal(CampaignPreviewLayers.Player | CampaignPreviewLayers.Targets | CampaignPreviewLayers.Danger,
            preview.Dirty);
        Assert.Equal(CampaignPreviewLayers.Player, preview.Refresh());
        Assert.Equal(new Vector3(4f, 5f, 3f), Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin);
        Assert.NotNull(preview.Selection);
        Assert.Same(generators, preview.Items(CampaignPreviewLayers.Generators));
        Assert.Equal(placements, world.Placements);

        document.SetLeaf(1, new[] { 0, 0, 5, 0 }, 100f);
        Assert.Equal(
            CampaignPreviewLayers.Zeppelins | CampaignPreviewLayers.Generators
                | CampaignPreviewLayers.Targets | CampaignPreviewLayers.Roster,
            preview.Dirty);
        Assert.Equal(CampaignPreviewLayers.Zeppelins | CampaignPreviewLayers.Generators, preview.Refresh());
        Assert.Equal(placements + 1, world.Placements);
        Assert.Equal(new Vector3(102f, 20f, 30f),
            Assert.Single(preview.Items(CampaignPreviewLayers.Generators)).Pose.Origin);
    }

    [Fact]
    public void UndoAndRedoRefreshOnlyAffectedPreviewLayers()
    {
        CampaignMissionDocument document = Document(
            Objectives(Row("PLAYER_INIT", Player(1f, 2f, 3f, 0f))),
            Roster(Row(Row(0f, "Wing"), Row("wing", Fields(40f, 50f, 60f, 0f, true)))));
        using var preview = Preview(document, new FakeWorld());
        preview.Refresh();
        IReadOnlyList<CampaignPreviewItem> roster = preview.Items(CampaignPreviewLayers.Roster);
        var edits = new List<CampaignMissionEdit>();
        var refreshed = new List<CampaignPreviewLayers>();
        using var session = new CampaignMissionEditSession(
            document, edits.Add, () => refreshed.Add(preview.Refresh()));

        Assert.True(document.SetLeaf(0, new[] { 0, 1, 1, 0 }, 9f));
        Assert.Equal(CampaignPreviewLayers.Player, preview.Refresh());
        Assert.Equal(9f, Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin.X);
        CampaignMissionEdit edit = Assert.Single(edits);
        Assert.True(session.Replay(edit, forward: false));
        Assert.Equal(1f, Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin.X);
        Assert.True(session.Replay(edit, forward: true));
        Assert.Equal(9f, Assert.Single(preview.Items(CampaignPreviewLayers.Player)).Pose.Origin.X);
        Assert.Equal(new[] { CampaignPreviewLayers.Player, CampaignPreviewLayers.Player }, refreshed);
        Assert.Same(roster, preview.Items(CampaignPreviewLayers.Roster));
    }

    [Fact]
    public void FloatBitsTriggerInspectionWithoutRebuildingAnUnchangedPose()
    {
        CampaignMissionDocument document = Document(
            Objectives(Row("PLAYER_INIT", Player(0f, 2f, 3f, 0f))));
        using var preview = Preview(document, new FakeWorld());
        preview.Refresh();
        IReadOnlyList<CampaignPreviewItem> before = preview.Items(CampaignPreviewLayers.Player);

        Assert.True(document.SetLeaf(0, new[] { 0, 1, 1, 0 }, -0f));
        Assert.NotEqual(CampaignPreviewLayers.None, preview.Dirty);
        Assert.Equal(CampaignPreviewLayers.None, preview.Refresh());
        Assert.Same(before, preview.Items(CampaignPreviewLayers.Player));
        Assert.Equal(0x80000000u,
            BitConverter.SingleToUInt32Bits(Assert.IsType<float>(document.ValueAt(0, new[] { 0, 1, 1, 0 }))));
    }

    [Fact]
    public void SelectionClearsOnStructuralChangesAndDisposeUnsubscribes()
    {
        CampaignMissionDocument document = Document(Targets(Row(
            Row(Row("nodes", Row("site")), Row("objective")))));
        var world = new FakeWorld();
        world.Targets["site"] = new Vector3(4f, 5f, 6f);
        var preview = Preview(document, world);
        preview.Refresh();
        CampaignPreviewItem selected = Assert.Single(preview.Items(CampaignPreviewLayers.Targets));
        Assert.True(preview.Select(selected));
        Assert.Equal(new[] { 0, 0, 1, 0 }, preview.Selection!.Source.Path);

        CampaignMissionTargets binding = CampaignMissionTargets.Bind(document, Scope(), true);
        CampaignTargetEntry entry = Assert.Single(binding.Entries);
        Assert.True(entry.Apply(entry.State with { Objective = false }));
        Assert.Null(preview.Selection);
        Assert.Equal(CampaignPreviewLayers.Targets, preview.Refresh());
        Assert.False(Assert.Single(preview.Items(CampaignPreviewLayers.Targets)).Enabled);

        preview.Dispose();
        preview.Dispose();
        Assert.Null(preview.Selection);
        Assert.Throws<ObjectDisposedException>(() => preview.Refresh());
        Assert.True(document.SetLeaf(0, new[] { 0, 0, 1, 0 }, "another"));
    }

    [Fact]
    public void MissingMalformedAndUnresolvedInputsDoNotInventHandles()
    {
        CampaignMissionDocument document = Document(
            Objectives(Row("PLAYER_INIT", Row(1f, Row(1f, 2f), Row(0f, 0f, 0f), 1f, 1f))),
            Roster(Row(Row(0f, "Bad"), Row("bad", Row(1f, Row(1f, 2f))))),
            Targets(Row(Row(Row("nodes", Row("missing")), Row("objective")))),
            Zeppelins(Row(Row("node", Row("absent"), "net", Row("net"), "position", Row(1f, 2f)))),
            Generators(Row(
                Row("node", Row("missing_host"), "vehicle", Row("nets", Row("net"))),
                Row("node", Row("bad")))));
        using var preview = Preview(document, new FakeWorld(), chapterAvailable: false);
        preview.Refresh();

        foreach (CampaignPreviewLayers layer in new[]
        {
            CampaignPreviewLayers.Player, CampaignPreviewLayers.Roster, CampaignPreviewLayers.Targets,
            CampaignPreviewLayers.Generators, CampaignPreviewLayers.Zeppelins, CampaignPreviewLayers.Danger,
        })
        {
            Assert.Empty(preview.Items(layer));
            Assert.NotEmpty(preview.Status(layer));
        }

        Assert.False(preview.Select(new CampaignPreviewItem(
            CampaignPreviewLayers.Player, "invented",
            new CampaignPreviewSource(ReferenceScope.Mission, "other.zrd", 0,
                System.Collections.Immutable.ImmutableArray.Create(0)),
            null, Transform3D.Identity, System.Collections.Immutable.ImmutableArray<Vector3>.Empty, true, "")));
        Assert.Null(preview.Selection);
    }

    private static CampaignMissionPreview Preview(
        CampaignMissionDocument document,
        FakeWorld world,
        ZrdrSet? chapter = null,
        bool chapterAvailable = true,
        CampaignDangerZoneWorld? danger = null) =>
        new(document, chapter ?? Scope(), chapterAvailable,
            danger ?? CampaignMissionDangerZonesTests.Project(null), world, "invented/gamez.zbd");

    private static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, Scope(files));

    private static ZrdrSet Scope(params ZrdrFile[] files) => new("invented", files);

    private static ZrdrFile Objectives(List<object?> inner) => new("objectives.zrd", null, Row(inner));

    private static ZrdrFile Roster(List<object?> root) => new("aiv.zrd", null, root);

    private static ZrdrFile Targets(List<object?> root) => new("targets.zrd", null, root);

    private static ZrdrFile Zeppelins(List<object?> records) => new("zeppelins.zrd", null, Row(records));

    private static ZrdrFile Generators(List<object?> records) => new("egen.zrd", null, Row(records));

    private static List<object?> Player(float x, float y, float z, float yaw) =>
        Row(1f, Row(x, y, z), Row(10f, yaw, 30f), 0.8f, 180f);

    private static List<object?> Fields(float x, float y, float z, float yaw, bool enabled) =>
        Row(-1f, Row(x, y, z), yaw, 1f, 0f, enabled ? 1f : 0f, "");

    private static List<object?> Row(params object?[] values) => new(values);

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(expected.DistanceTo(actual) < 0.001f, $"expected {expected}, got {actual}");

    private sealed class FakeWorld : ICampaignPreviewWorld
    {
        internal Dictionary<string, Transform3D> Poses { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal Dictionary<string, Vector3> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal Dictionary<(string Host, string? Origin, bool Zeppelin), Vector3> Offsets { get; } = new();

        internal int Placements { get; private set; }

        public bool TryPose(string name, out Transform3D pose) => Poses.TryGetValue(name, out pose);

        public bool TryTarget(IReadOnlyList<string> path, out Vector3 position) =>
            Targets.TryGetValue(string.Join("/", path), out position);

        public bool TryGenerator(
            string host,
            string? origin,
            bool zeppelin,
            out Transform3D hostPose,
            out Vector3 offset,
            out string note)
        {
            note = "Invented host-relative placement.";
            bool hasHost = Poses.TryGetValue(host, out hostPose);
            bool hasOffset = Offsets.TryGetValue((host, origin, zeppelin), out offset);
            return hasHost && hasOffset;
        }

        public void PlaceZeppelins(IReadOnlyList<CampaignPreviewItem> zeppelins)
        {
            Placements++;
            foreach (CampaignPreviewItem item in zeppelins)
            {
                Poses[item.Name] = item.Pose;
            }
        }
    }
}

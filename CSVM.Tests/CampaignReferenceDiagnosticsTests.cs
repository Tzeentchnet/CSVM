using System;
using System.Collections.Generic;
using System.Linq;
using CSVM.Mech3;
using CSVM.Tooling;
using Xunit;

namespace CSVM.Tests;

/// <summary>Continuous mission reference diagnostics over invented reader bundles.</summary>
[Trait("Tier", "Quick")]
public sealed class CampaignReferenceDiagnosticsTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void RefreshesAfterEditsAndClearsAfterRepair()
    {
        List<object?> fields = Fields();
        fields[20] = "GOOD";
        CampaignMissionDocument document = Document(
            new ZrdrFile(
                "aiv.zrd",
                null,
                new List<object?>
                {
                    new List<object?>(),
                    new List<object?> { "player", fields },
                }));
        CampaignMissionDependencyBundle bundle = Bundle(
            messages: Messages.Of(
                new[] { (Key: "GOOD", Value: "Good") }));
        using var diagnostics =
            new CampaignReferenceDiagnostics(document, bundle);
        int published = 0;
        diagnostics.Changed += _ => published++;

        ReferenceSnapshot initial = diagnostics.Current;

        CheckedReference initialMessage = Assert.Single(
            initial.References.Where(reference =>
                reference.Reference.Kind == ReferenceKind.Message));
        Assert.Equal(ReferenceState.Resolved, initialMessage.State);
        Assert.Equal(
            new[] { 1, 1, 20 },
            initialMessage.Reference.Source.Path);
        Assert.Equal(1, published);

        Assert.True(
            document.SetLeaf(0, new[] { 1, 1, 20 }, "OTHER"));
        Assert.True(
            document.SetLeaf(0, new[] { 1, 1, 20 }, "ANOTHER"));
        ReferenceSnapshot broken = diagnostics.Current;

        Assert.Equal(2, broken.DocumentRevision);
        Assert.Equal(
            ReferenceState.Missing,
            Assert.Single(
                broken.References.Where(reference =>
                    reference.Reference.Kind
                        == ReferenceKind.Message)).State);
        Assert.Equal(2, published);

        Assert.True(
            document.SetLeaf(0, new[] { 1, 1, 20 }, "GOOD"));
        ReferenceSnapshot repaired = diagnostics.Current;

        Assert.Equal(3, repaired.DocumentRevision);
        Assert.Equal(
            ReferenceState.Resolved,
            Assert.Single(
                repaired.References.Where(reference =>
                    reference.Reference.Kind
                        == ReferenceKind.Message)).State);
        Assert.Equal(3, published);
        Assert.False(
            document.SetLeaf(0, new[] { 1, 1, 20 }, "GOOD"));
        Assert.Same(repaired, diagnostics.Current);
        Assert.Equal(3, published);
    }

    [Fact]
    public void UndoAndRedoRepublishReferenceVerdicts()
    {
        List<object?> fields = Fields();
        fields[20] = "GOOD";
        CampaignMissionDocument document = Document(new ZrdrFile(
            "aiv.zrd", null, new List<object?> { new List<object?>(), new List<object?> { "player", fields } }));
        using var diagnostics = new CampaignReferenceDiagnostics(
            document, Bundle(messages: Messages.Of(new[] { (Key: "GOOD", Value: "Good") })));
        var edits = new List<CampaignMissionEdit>();
        var published = new List<ReferenceState>();
        ReferenceState State() => Assert.Single(
            diagnostics.Current.References.Where(reference => reference.Reference.Kind == ReferenceKind.Message)).State;
        using var session = new CampaignMissionEditSession(document, edits.Add, () => published.Add(State()));

        Assert.Equal(ReferenceState.Resolved, State());
        Assert.True(document.SetLeaf(0, new[] { 1, 1, 20 }, "MISSING"));
        Assert.Equal(ReferenceState.Missing, State());
        CampaignMissionEdit edit = Assert.Single(edits);
        Assert.True(session.Replay(edit, forward: false));
        Assert.True(session.Replay(edit, forward: true));
        Assert.Equal(new[] { ReferenceState.Resolved, ReferenceState.Missing }, published);
        Assert.True(diagnostics.Current.HasErrors);
    }

    [Fact]
    public void AggregatesIndependentFailuresAndDuplicatePolicies()
    {
        var objective = new List<object?>
        {
            "IDENTITY",
            new List<object?> { 1f, 1f, "FIRST" },
            "IDENTITY",
            new List<object?> { 1f, 1f, "SECOND" },
            "ANIMATION_DEFINITION_FILE",
            new List<object?> { "missing_anim.zrd" },
        };
        CampaignMissionDocument document = Document(
            new ZrdrFile(
                "objectives.zrd",
                null,
                new List<object?>
                {
                    new List<object?>
                    {
                        "OBJECTIVE1", objective,
                        "MISSION_WON_SOUND", new List<object?> { "NO_SOUND" },
                    },
                }),
            new ZrdrFile(
                "startanims.zrd",
                null,
                new List<object?>
                {
                    new List<object?>
                    {
                        "NEW_GAME_START",
                        new List<object?>
                        {
                            new List<object?> { "opening" },
                        },
                    },
                }));
        CampaignMissionDependencyBundle bundle = Bundle(
            messages: Messages.Of(
                new[]
                {
                    (Key: "FIRST", Value: "First"),
                    (Key: "SECOND", Value: "Second"),
                }),
            missionAnimations: Animation("opening"));
        using var diagnostics =
            new CampaignReferenceDiagnostics(document, bundle);

        ReferenceSnapshot snapshot = diagnostics.Current;

        CheckedReference first = Find(snapshot, ReferenceKind.Message, "FIRST");
        CheckedReference second = Find(snapshot, ReferenceKind.Message, "SECOND");
        Assert.Equal(ReferenceState.Resolved, first.State);
        Assert.Equal(ReferenceState.Resolved, second.State);
        Assert.Equal(
            new[] { 0, 1, 1, 2 },
            first.Reference.Source.Path);
        Assert.Equal(
            ReferenceState.Resolved,
            Find(snapshot, ReferenceKind.Animation, "opening").State);
        Assert.Equal(
            ReferenceState.InheritedMissing,
            Find(snapshot, ReferenceKind.Sound, "NO_SOUND").State);
        Assert.Equal(
            ReferenceState.InheritedMissing,
            Find(
                snapshot,
                ReferenceKind.ReaderPath,
                "missing_anim.zrd").State);
        Assert.True(snapshot.InheritedMissingCount >= 2);
    }

    [Fact]
    public void ANameInTheWrongScopeDoesNotResolve()
    {
        List<object?> fields = Fields();
        fields[20] = "WRONG_SCOPE";
        CampaignMissionDocument document = Document(
            new ZrdrFile(
                "aiv.zrd",
                null,
                new List<object?>
                {
                    new List<object?>(),
                    new List<object?> { "player", fields },
                }));
        ZrdrSet shared = new(
            "shared",
            new[]
            {
                new ZrdrFile(
                    "unrelated.zrd",
                    null,
                    new List<object?> { "WRONG_SCOPE" }),
            });
        CampaignMissionDependencyBundle bundle = Bundle(
            shared: shared,
            messages: new Messages());
        using var diagnostics =
            new CampaignReferenceDiagnostics(document, bundle);

        Assert.Equal(
            ReferenceState.InheritedMissing,
            Find(
                diagnostics.Current,
                ReferenceKind.Message,
                "WRONG_SCOPE").State);
    }

    [Fact]
    public void UnavailableDependenciesProduceUnverifiedReferences()
    {
        List<object?> fields = Fields();
        fields[20] = "UNKNOWN";
        CampaignMissionDocument document = Document(
            new ZrdrFile(
                "aiv.zrd",
                null,
                new List<object?>
                {
                    new List<object?>(),
                    new List<object?> { "player", fields },
                }));
        CampaignMissionDependencyBundle bundle = Bundle(
            messageState: CampaignDependencyState.Unavailable);
        using var diagnostics =
            new CampaignReferenceDiagnostics(document, bundle);

        ReferenceSnapshot snapshot = diagnostics.Current;

        Assert.Equal(
            ReferenceState.Unverified,
            Find(snapshot, ReferenceKind.Message, "UNKNOWN").State);
        Assert.Contains(
            snapshot.Issues,
            issue => issue.Code == "dependency-unavailable");
    }

    [Fact]
    public void ConstructorRejectsAStaleBundleAddress()
    {
        CampaignMissionDocument document = Document(
            new ZrdrFile(
                "objectives.zrd",
                null,
                new List<object?>()));
        CampaignMission other =
            new(1, "Other", 6, 2, "Other", false);
        CampaignMissionDependencyBundle bundle = Bundle(mission: other);

        Assert.Throws<ArgumentException>(
            () => new CampaignReferenceDiagnostics(document, bundle));
    }

    private static CheckedReference Find(
        ReferenceSnapshot snapshot,
        ReferenceKind kind,
        string name) =>
        Assert.Single(
            snapshot.References.Where(reference =>
                reference.Reference.Kind == kind
                && string.Equals(
                    reference.Reference.Value.Name,
                    name,
                    StringComparison.Ordinal)));

    private static List<object?> Fields()
    {
        var fields = new List<object?>();
        for (int i = 0; i < 66; i++)
        {
            fields.Add(null);
        }

        return fields;
    }

    private static CampaignMissionDocument Document(
        params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(
            Mission,
            new ZrdrSet("mission", files));

    private static CampaignMissionDependencyBundle Bundle(
        CampaignMission? mission = null,
        ZrdrSet? shared = null,
        Messages? messages = null,
        AnimArchive? missionAnimations = null,
        CampaignDependencyState messageState =
            CampaignDependencyState.Ready)
    {
        CampaignMission address = mission ?? Mission;
        ZrdrSet sharedReaders = shared ?? new ZrdrSet(
            "shared",
            Array.Empty<ZrdrFile>());
        ZrdrSet chapterReaders = new(
            "chapter",
            Array.Empty<ZrdrFile>());
        var world = new GameZ();
        var textures = new TextureSet(
            "textures",
            Array.Empty<TextureImage>());
        var messageTable = messages ?? new Messages();
        return new CampaignMissionDependencyBundle(
            Guid.NewGuid(),
            address,
            Ready(sharedReaders, "ZBD/zrdr.zbd"),
            Ready<GameZ?>(world, "ZBD/c3/gamez.zbd"),
            Ready(textures, "ZBD/c3/rtexture15.zbd"),
            Ready(chapterReaders, "ZBD/c3/zrdr.zbd"),
            Missing<AnimArchive?>(
                null,
                "ZBD/c3/cam_anim.zbd",
                optional: true),
            missionAnimations == null
                ? Missing<AnimArchive?>(
                    null,
                    "ZBD/c3/m01/mis_anim.zbd",
                    optional: true)
                : Ready<AnimArchive?>(
                    missionAnimations,
                    "ZBD/c3/m01/mis_anim.zbd",
                    optional: true),
            new CampaignDependency<Messages>(
                messageTable,
                new CampaignDependencyAvailability(
                    Messages.GameFile,
                    messageState,
                    true)),
            Missing<InterpScripts?>(
                null,
                InterpScripts.GamePath,
                optional: true),
            new CampaignDependency<MissionSetup?>(
                null,
                new CampaignDependencyAvailability(
                    "interp::setup",
                    CampaignDependencyState.NotChecked,
                    true)),
            null);
    }

    private static CampaignDependency<T> Ready<T>(
        T value,
        string path,
        bool optional = false) =>
        new(
            value,
            new CampaignDependencyAvailability(
                path,
                CampaignDependencyState.Ready,
                optional));

    private static CampaignDependency<T> Missing<T>(
        T value,
        string path,
        bool optional) =>
        new(
            value,
            new CampaignDependencyAvailability(
                path,
                CampaignDependencyState.Missing,
                optional));

    private static AnimArchive Animation(string name) =>
        AnimArchive.FromDecoded(
            "mission",
            new List<AnimDefinition>
            {
                new() { Name = "node", AnimName = name },
            },
            new List<(string Name, SiScript Script)>());
}

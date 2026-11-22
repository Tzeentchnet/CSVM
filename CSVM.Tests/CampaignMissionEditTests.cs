using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CSVM.Mech3;
using CSVM.Tooling;
using Xunit;

namespace CSVM.Tests;

[Trait("Tier", "Quick")]
public sealed class CampaignMissionEditTests
{
    private static readonly CampaignMission Mission =
        new(0, "Invented mission", 6, 1, "Invented", false);

    [Fact]
    public void RawEditCapturesBitsAndNotifiesOncePerTransition()
    {
        CampaignMissionDocument document = Document(
            new ZrdrFile("probe.zrd", null, Row("before", -0f)));
        var edits = new List<CampaignMissionEdit>();
        int changes = 0;
        int refreshes = 0;
        document.Changed += () => changes++;
        using var session = new CampaignMissionEditSession(document, edits.Add, () => refreshes++);
        var path = new[] { 1 };

        Assert.True(document.SetLeaf(0, path, 0f));
        path[0] = 0;
        CampaignMissionEdit edit = Assert.Single(edits);
        CampaignRawChange leaf = Assert.Single(edit.Changes);
        Assert.Equal(new[] { 1 }, leaf.Path.ToArray());
        Assert.Equal(0x80000000u, leaf.Before.Bits);
        Assert.Equal(0u, leaf.After.Bits);
        Assert.False(edit.Before.Dirty);
        Assert.True(edit.After.Dirty);
        Assert.Equal(1, document.Revision);
        Assert.True(document.IsDirty);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(session.Replay(edit, forward: false));
            Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 1 })));
            Assert.Equal(0, document.Revision);
            Assert.False(document.IsDirty);
            Assert.False(session.Replay(edit, forward: false));
            Assert.True(session.Replay(edit, forward: true));
            Assert.Equal(0u, Bits(document.ValueAt(0, new[] { 1 })));
            Assert.Equal(1, document.Revision);
            Assert.True(document.IsDirty);
        }

        Assert.Single(edits);
        Assert.Equal(7, changes);
        Assert.Equal(6, refreshes);
    }

    [Fact]
    public void PlayerEditIsOneAtomicOperationAndRestoresAuthoredSpeedBits()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument document = Document(
            new ZrdrFile("objectives.zrd", null, Row(Row(
                "PLAYER_INIT", Row(1f, Row(-0f, 2f, 3f), Row(4f, 5f, 6f), 0.5f, subnormal)))));
        Assert.True(CampaignPlayerInit.TryBind(document, out CampaignPlayerInit? player, out string problem), problem);
        var edits = new List<CampaignMissionEdit>();
        int changes = 0;
        document.Changed += () => changes++;
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });
        CampaignPlayerInitState original = player.State;

        Assert.True(player.Apply(original with { X = 0f, Y = 7f, SpeedMps = 1f }));
        CampaignMissionEdit edit = Assert.Single(edits);
        Assert.Equal(3, edit.Changes.Length);
        Assert.Equal(1, document.Revision);
        Assert.Equal(1, changes);
        Assert.Equal(0u, Bits(document.ValueAt(0, new[] { 0, 1, 1, 0 })));
        Assert.Equal(10f, document.ValueAt(0, new[] { 0, 1, 4 }));

        Assert.True(session.Replay(edit, forward: false));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 0, 1, 1, 0 })));
        Assert.Equal(1u, Bits(document.ValueAt(0, new[] { 0, 1, 4 })));
        Assert.True(session.Replay(edit, forward: true));
        Assert.Equal(10f, document.ValueAt(0, new[] { 0, 1, 4 }));
        Assert.False(player.Apply(player.State));
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Apply(player.State with { ThrottleFrac = 2f }));
        Assert.Single(edits);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void InvalidAndDuplicateBatchesNeverPublishPartialChanges()
    {
        CampaignMissionDocument document = Document(
            new ZrdrFile("probe.zrd", null, Row("before", 1f, 2f)));
        var edits = new List<CampaignMissionEdit>();
        int changes = 0;
        document.Changed += () => changes++;
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });

        Assert.Throws<ArgumentOutOfRangeException>(() => document.BatchLeaves("invalid", () =>
        {
            document.SetLeaf(0, new[] { 0 }, "after");
            document.SetLeaf(0, new[] { 1 }, float.NaN);
            return true;
        }));
        Assert.Throws<InvalidOperationException>(() => document.BatchLeaves("duplicate", () =>
        {
            document.SetLeaf(0, new[] { 1 }, 3f);
            document.SetLeaf(0, new[] { 1 }, 4f);
            return true;
        }));
        Assert.Throws<InvalidOperationException>(() => document.BatchLeaves("duplicate unchanged", () =>
        {
            document.SetLeaf(0, new[] { 1 }, 3f);
            document.SetLeaf(0, new[] { 1 }, 1f);
            return true;
        }));
        Assert.False(document.BatchLeaves("unchanged", () =>
        {
            document.SetLeaf(0, new[] { 0 }, "before");
            return false;
        }));
        Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
        Assert.Equal(1f, document.ValueAt(0, new[] { 1 }));
        Assert.Equal(0, document.Revision);
        Assert.False(document.IsDirty);
        Assert.Equal(0, changes);
        Assert.Empty(edits);
    }

    [Fact]
    public void MismatchedLeafRejectsAnEntireHistoryTransition()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row(1f, 2f)));
        var edits = new List<CampaignMissionEdit>();
        int refreshes = 0;
        using var session = new CampaignMissionEditSession(document, edits.Add, () => refreshes++);
        Assert.True(document.BatchLeaves("two leaves", () =>
        {
            document.SetLeaf(0, new[] { 0 }, 3f);
            document.SetLeaf(0, new[] { 1 }, 4f);
            return true;
        }));
        CampaignMissionEdit edit = Assert.Single(edits);
        document.Files[0].Root[1] = 5f;

        Assert.False(session.Replay(edit, forward: false));
        Assert.Equal(3f, document.ValueAt(0, new[] { 0 }));
        Assert.Equal(5f, document.ValueAt(0, new[] { 1 }));
        Assert.Equal(1, document.Revision);
        Assert.True(document.IsDirty);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public void FailedRegistrationRollsBackAndPermanentlyRejectsTheAbortedOperation()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before", "other")));
        var captured = new List<CampaignMissionEdit>();
        int changes = 0;
        int refreshes = 0;
        int rollbacks = 0;
        document.Changed += () => changes++;
        using var recording = new CampaignMissionEditSession(document, captured.Add, () => refreshes++, () =>
        {
            rollbacks++;
            Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
            Assert.Equal(0, document.Revision);
        });
        var failing = new CampaignMissionEditSession(
            document, _ => throw new InvalidOperationException("history unavailable"), () => refreshes++);

        Assert.Throws<InvalidOperationException>(() => document.SetLeaf(0, new[] { 0 }, "failed"));
        CampaignMissionEdit aborted = Assert.Single(captured);
        Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
        Assert.Equal(0, document.Revision);
        Assert.False(document.IsDirty);
        Assert.Equal(0, changes);
        Assert.Equal(1, rollbacks);
        Assert.False(recording.Replay(aborted, forward: true));
        Assert.False(recording.Replay(aborted, forward: false));
        Assert.Equal(0, refreshes);
        failing.Dispose();

        Assert.True(document.SetLeaf(0, new[] { 0 }, "success"));
        Assert.NotEqual(aborted.After.Identity, captured[1].After.Identity);
        Assert.False(recording.Replay(aborted, forward: false));
        Assert.Equal("success", document.ValueAt(0, new[] { 0 }));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ReentrantRegistrationCannotPublishOrReplayAnotherOperation()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before", "other")));
        CampaignMissionEdit? captured = null;
        using var session = new CampaignMissionEditSession(document, edit =>
        {
            captured = edit;
            document.SetLeaf(0, new[] { 1 }, "nested");
        }, () => { });

        Assert.Throws<InvalidOperationException>(() => document.SetLeaf(0, new[] { 0 }, "after"));
        Assert.NotNull(captured);
        Assert.False(session.Replay(captured, forward: true));
        Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
        Assert.Equal("other", document.ValueAt(0, new[] { 1 }));
        Assert.Equal(0, document.Revision);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void DeferredEditsPreserveAnUnassignedForeignActionAndEachDocument()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument first = Document(new ZrdrFile("objectives.zrd", null, Row(Row(
            "PLAYER_INIT", Row(1f, Row(-0f, 2f, 3f), Row(4f, 5f, 6f), 0.5f, subnormal)))));
        CampaignMissionDocument second = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        Assert.True(CampaignPlayerInit.TryBind(first, out CampaignPlayerInit? player, out string problem), problem);
        var deferred = new Queue<Action>();
        var history = new EditorHistoryModel();
        var failures = new List<Exception>();
        int firstChanges = 0;
        int secondChanges = 0;
        int firstRefreshes = 0;
        int secondRefreshes = 0;
        first.Changed += () => firstChanges++;
        second.Changed += () => secondChanges++;
        CampaignMissionEditSession? firstSession = null;
        CampaignMissionEditSession? secondSession = null;
        firstSession = new CampaignMissionEditSession(
            first, edit => history.Register(edit, firstSession!), () => firstRefreshes++,
            defer: deferred.Enqueue, registrationFailed: failures.Add);
        secondSession = new CampaignMissionEditSession(
            second, edit => history.Register(edit, secondSession!), () => secondRefreshes++,
            defer: deferred.Enqueue, registrationFailed: failures.Add);
        using var firstLifetime = firstSession;
        using var secondLifetime = secondSession;

        history.CreateAction("Foreign scene action");
        CampaignPlayerInit boundPlayer = player!;
        Assert.True(boundPlayer.Apply(boundPlayer.State with { X = 0f }));
        Assert.True(second.SetLeaf(0, new[] { 0 }, "after"));
        Assert.True(first.SetLeaf(0, new[] { 0, 1, 3 }, 0.75f));
        Assert.Equal("Foreign scene action", history.PendingName);
        Assert.Null(history.PendingHistory);
        Assert.Empty(history.Published);
        Assert.Equal(3, deferred.Count);

        int foreignValue = 1;
        history.Add("scene-17", () => foreignValue = 0, () => foreignValue = 1);
        history.Commit();
        while (deferred.Count > 0)
        {
            deferred.Dequeue()();
        }

        Assert.Empty(failures);
        Assert.Equal(
            new[] { "Foreign scene action", "Edit player start", "Edit raw value", "Edit raw value" },
            history.Published.Select(action => action.Name));
        Assert.Equal(
            new[] { "scene-17", "global", "global", "global" },
            history.Published.Select(action => action.History));

        history.Undo();
        Assert.Equal(0.5f, first.ValueAt(0, new[] { 0, 1, 3 }));
        history.Undo();
        Assert.Equal("before", second.ValueAt(0, new[] { 0 }));
        Assert.False(second.IsDirty);
        history.Undo();
        Assert.Equal(0x80000000u, Bits(first.ValueAt(0, new[] { 0, 1, 1, 0 })));
        Assert.Equal(1u, Bits(first.ValueAt(0, new[] { 0, 1, 4 })));
        Assert.False(first.IsDirty);
        history.Undo();
        Assert.Equal(0, foreignValue);

        history.Redo();
        history.Redo();
        history.Redo();
        history.Redo();
        Assert.Equal(1, foreignValue);
        Assert.Equal(0u, Bits(first.ValueAt(0, new[] { 0, 1, 1, 0 })));
        Assert.Equal(0.75f, first.ValueAt(0, new[] { 0, 1, 3 }));
        Assert.Equal("after", second.ValueAt(0, new[] { 0 }));
        Assert.Equal(6, firstChanges);
        Assert.Equal(3, secondChanges);
        Assert.Equal(4, firstRefreshes);
        Assert.Equal(2, secondRefreshes);
    }

    [Fact]
    public void DeferredRegistrationFailureRollsBackOnlyTheUnpublishedSuffix()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row(-0f, "a", "b")));
        var deferred = new Queue<Action>();
        var captured = new List<CampaignMissionEdit>();
        var registered = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        int attempts = 0;
        int changes = 0;
        int refreshes = 0;
        int rollbacks = 0;
        document.EditCommitted += captured.Add;
        document.Changed += () => changes++;
        using var session = new CampaignMissionEditSession(document, edit =>
        {
            if (++attempts == 2)
            {
                throw new InvalidOperationException("history unavailable");
            }

            registered.Add(edit);
        }, () => refreshes++, () => rollbacks++, deferred.Enqueue, failures.Add);

        Assert.True(document.SetLeaf(0, new[] { 0 }, 0f));
        Assert.True(document.SetLeaf(0, new[] { 1 }, "changed a"));
        Assert.True(document.SetLeaf(0, new[] { 2 }, "changed b"));
        deferred.Dequeue()();
        deferred.Dequeue()();
        deferred.Dequeue()();

        Assert.Single(registered);
        Assert.Single(failures);
        Assert.Equal("history unavailable", failures[0].Message);
        Assert.Equal(1, rollbacks);
        Assert.Equal(1, document.Revision);
        Assert.True(document.IsDirty);
        Assert.Equal(0u, Bits(document.ValueAt(0, new[] { 0 })));
        Assert.Equal("a", document.ValueAt(0, new[] { 1 }));
        Assert.Equal("b", document.ValueAt(0, new[] { 2 }));
        Assert.False(session.Replay(captured[1], forward: false));
        Assert.False(session.Replay(captured[2], forward: true));
        Assert.True(session.Replay(registered[0], forward: false));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 0 })));
        Assert.False(document.IsDirty);
        Assert.Equal(5, changes);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void DeferredFailureRollsBackStructuralEditsWithoutReplacingPermutationEntries()
    {
        CampaignMissionDocument document = Document(new ZrdrFile(
            "startanims.zrd", null, Row(Row("NEW_GAME_START",
                Row(Row("one", -0f), Row("two", "tail"))))));
        CampaignMissionStartAnimations animations = CampaignMissionStartAnimations.Bind(document);
        List<object?> one = animations.Entries[0].Raw;
        List<object?> two = animations.Entries[1].Raw;
        var deferred = new Queue<Action>();
        var captured = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        int changes = 0;
        int rollbacks = 0;
        document.EditCommitted += captured.Add;
        document.Changed += () => changes++;
        using var session = new CampaignMissionEditSession(
            document, _ => throw new InvalidOperationException("history unavailable"),
            () => { }, () => rollbacks++, deferred.Enqueue, failures.Add);

        Assert.True(animations.Move(0, 1));
        int index = document.CreateDangerZoneReader(document.ChangeGeneration);
        List<object?> root = document.Files[index].Root;
        Assert.True(document.ReplaceDangerZoneRoot(
            index, document.ChangeGeneration, root, Row("unknown", Row(-0f, null))));
        while (deferred.Count > 0)
        {
            deferred.Dequeue()();
        }

        Assert.Single(failures);
        Assert.Single(document.Files);
        Assert.Same(one, document.ValueAt(0, new[] { 0, 1, 0 }));
        Assert.Same(two, document.ValueAt(0, new[] { 0, 1, 1 }));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 0, 1, 0, 1 })));
        Assert.False(document.IsDirty);
        Assert.Equal(0, document.Revision);
        Assert.Equal(4, changes);
        Assert.Equal(1, rollbacks);
        Assert.All(captured, edit => Assert.False(session.Replay(edit, forward: true)));
    }

    [Fact]
    public void DeferredFailureDoesNotOverwriteDivergentRawValues()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        var deferred = new Queue<Action>();
        var captured = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        int rollbacks = 0;
        document.EditCommitted += captured.Add;
        using var session = new CampaignMissionEditSession(
            document, _ => throw new InvalidOperationException("history unavailable"),
            () => { }, () => rollbacks++, deferred.Enqueue, failures.Add);
        Assert.True(document.SetLeaf(0, new[] { 0 }, "after"));
        document.Files[0].Root[0] = "unexpected";
        deferred.Dequeue()();

        Assert.Single(failures);
        Assert.Contains("could not be rolled back", failures[0].Message);
        Assert.Equal("unexpected", document.ValueAt(0, new[] { 0 }));
        Assert.Equal(1, document.Revision);
        Assert.True(document.IsDirty);
        Assert.Equal(0, rollbacks);
        Assert.False(session.Replay(Assert.Single(captured), forward: false));
    }

    [Fact]
    public void UndoBeforeDeferredPublicationKeepsHistoryAndDocumentAligned()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row(-0f, "before")));
        var deferred = new Queue<Action>();
        var history = new EditorHistoryModel();
        var captured = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        int changes = 0;
        int refreshes = 0;
        int rollbacks = 0;
        document.EditCommitted += captured.Add;
        document.Changed += () => changes++;
        CampaignMissionEditSession? session = null;
        session = new CampaignMissionEditSession(
            document, edit => history.Register(edit, session!), () => refreshes++, () => rollbacks++,
            deferred.Enqueue, failures.Add);
        using var lifetime = session;
        Assert.True(document.SetLeaf(0, new[] { 0 }, 0f));
        deferred.Dequeue()();
        Assert.True(document.SetLeaf(0, new[] { 1 }, "pending"));

        history.Undo();
        Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 0 })));
        Assert.Equal("before", document.ValueAt(0, new[] { 1 }));
        Assert.False(document.IsDirty);
        Assert.Equal(0, document.Revision);
        Assert.Equal(1, rollbacks);
        Assert.Single(failures);
        Assert.False(session.Replay(captured[1], forward: true));
        deferred.Dequeue()();
        Assert.Single(history.Published);
        history.Redo();
        Assert.Equal(0u, Bits(document.ValueAt(0, new[] { 0 })));
        Assert.Equal("before", document.ValueAt(0, new[] { 1 }));
        Assert.Equal(4, changes);
        Assert.Equal(2, refreshes);
    }

    [Fact]
    public void ReentrantReplayDoesNotStrandAReadyReplacementEdit()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before", "other")));
        var deferred = new Queue<Action>();
        var registered = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        CampaignMissionEditSession? session = null;
        session = new CampaignMissionEditSession(document, edit =>
        {
            if (registered.Count == 1)
            {
                Assert.True(session!.Replay(registered[0], forward: false));
                Assert.True(document.SetLeaf(0, new[] { 1 }, "replacement"));
                deferred.Dequeue()();
            }

            registered.Add(edit);
        }, () => { }, defer: deferred.Enqueue, registrationFailed: failures.Add);
        using var lifetime = session;
        Assert.True(document.SetLeaf(0, new[] { 0 }, "published"));
        deferred.Dequeue()();
        Assert.True(document.SetLeaf(0, new[] { 1 }, "pending"));
        deferred.Dequeue()();

        Assert.Equal(3, registered.Count);
        Assert.Single(failures);
        Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
        Assert.Equal("replacement", document.ValueAt(0, new[] { 1 }));
        Assert.False(session.Replay(registered[1], forward: false));
        Assert.True(session.Replay(registered[2], forward: false));
        Assert.Equal("other", document.ValueAt(0, new[] { 1 }));
    }

    [Fact]
    public void ReentrantFailureHandlerDoesNotStrandAReadyReplacementEdit()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        var deferred = new Queue<Action>();
        var registered = new List<CampaignMissionEdit>();
        int attempts = 0;
        int failures = 0;
        using var session = new CampaignMissionEditSession(document, edit =>
        {
            if (++attempts == 1)
            {
                throw new InvalidOperationException("history unavailable");
            }

            registered.Add(edit);
        }, () => { }, defer: deferred.Enqueue, registrationFailed: _ =>
        {
            failures++;
            Assert.True(document.SetLeaf(0, new[] { 0 }, "replacement"));
            deferred.Dequeue()();
        });
        Assert.True(document.SetLeaf(0, new[] { 0 }, "pending"));
        deferred.Dequeue()();

        Assert.Equal(2, attempts);
        Assert.Equal(1, failures);
        Assert.Equal("replacement", document.ValueAt(0, new[] { 0 }));
        Assert.True(session.Replay(Assert.Single(registered), forward: false));
        Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
    }

    [Fact]
    public void FailureAfterReentrantReplayDoesNotRejectANewerEdit()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before", "other")));
        var deferred = new Queue<Action>();
        var registered = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        int attempts = 0;
        CampaignMissionEditSession? session = null;
        session = new CampaignMissionEditSession(document, edit =>
        {
            if (++attempts == 2)
            {
                Assert.True(session!.Replay(registered[0], forward: false));
                Assert.True(document.SetLeaf(0, new[] { 1 }, "replacement"));
                deferred.Dequeue()();
                throw new InvalidOperationException("history unavailable");
            }

            registered.Add(edit);
        }, () => { }, defer: deferred.Enqueue, registrationFailed: failures.Add);
        using var lifetime = session;
        Assert.True(document.SetLeaf(0, new[] { 0 }, "published"));
        deferred.Dequeue()();
        Assert.True(document.SetLeaf(0, new[] { 1 }, "pending"));
        deferred.Dequeue()();

        Assert.Equal(3, attempts);
        Assert.Equal(2, failures.Count);
        Assert.Equal(2, registered.Count);
        Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
        Assert.Equal("replacement", document.ValueAt(0, new[] { 1 }));
        Assert.True(session.Replay(registered[1], forward: false));
        Assert.Equal("other", document.ValueAt(0, new[] { 1 }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnloadDuringReplayDoesNotRefreshOrReportToTheOldSession(bool hasPendingEdit)
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before", "other")));
        var deferred = new Queue<Action>();
        var registered = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        int refreshes = 0;
        bool replaying = false;
        CampaignMissionEditSession? session = null;
        document.Changed += () =>
        {
            if (replaying)
            {
                session!.Dispose();
            }
        };
        session = new CampaignMissionEditSession(
            document, registered.Add, () => refreshes++,
            defer: deferred.Enqueue, registrationFailed: failures.Add);
        using var lifetime = session;
        Assert.True(document.SetLeaf(0, new[] { 0 }, "published"));
        deferred.Dequeue()();
        if (hasPendingEdit)
        {
            Assert.True(document.SetLeaf(0, new[] { 1 }, "pending"));
        }

        replaying = true;
        Assert.True(session.Replay(Assert.Single(registered), forward: false));
        if (hasPendingEdit)
        {
            deferred.Dequeue()();
        }

        Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
        Assert.Equal("other", document.ValueAt(0, new[] { 1 }));
        Assert.Equal(0, refreshes);
        Assert.Empty(failures);
        Assert.Single(registered);
        Assert.False(session.Replay(registered[0], forward: true));
    }

    [Fact]
    public void FailedDeferralRollsBackBeforeReturningToTheEditor()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row(-0f)));
        var registered = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        int changes = 0;
        int rollbacks = 0;
        document.Changed += () => changes++;
        using var session = new CampaignMissionEditSession(
            document, registered.Add, () => { }, () => rollbacks++,
            _ => throw new InvalidOperationException("deferred queue unavailable"), failures.Add);

        Assert.Throws<InvalidOperationException>(() => document.SetLeaf(0, new[] { 0 }, 0f));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 0 })));
        Assert.False(document.IsDirty);
        Assert.Equal(0, document.Revision);
        Assert.Equal(0, changes);
        Assert.Equal(1, rollbacks);
        Assert.Empty(registered);
        Assert.Empty(failures);
    }

    [Fact]
    public void InlineDispatchCannotReachTheSharedHistory()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        int registrations = 0;
        int rollbacks = 0;
        var failures = new List<Exception>();
        using var session = new CampaignMissionEditSession(
            document, _ => registrations++, () => { }, () => rollbacks++,
            action => action(), failures.Add);

        Assert.Throws<InvalidOperationException>(() => document.SetLeaf(0, new[] { 0 }, "after"));
        Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
        Assert.False(document.IsDirty);
        Assert.Equal(0, registrations);
        Assert.Equal(1, rollbacks);
        Assert.Empty(failures);
    }

    [Fact]
    public void DeferredEditsStayWithTheirOriginalSessionAndDiscardAbortedRegistrations()
    {
        CampaignMissionDocument first = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        CampaignMissionDocument second = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        var deferred = new Queue<Action>();
        var captured = new List<CampaignMissionEdit>();
        var registered = new List<CampaignMissionEdit>();
        var failures = new List<Exception>();
        first.EditCommitted += captured.Add;
        var oldSession = new CampaignMissionEditSession(
            first, registered.Add, () => { }, defer: deferred.Enqueue, registrationFailed: failures.Add);
        Assert.True(first.SetLeaf(0, new[] { 0 }, "first"));
        oldSession.Dispose();

        using var newSession = new CampaignMissionEditSession(
            second, registered.Add, () => { }, defer: deferred.Enqueue, registrationFailed: failures.Add);
        var failing = new CampaignMissionEditSession(
            second, _ => throw new InvalidOperationException("later registrar failed"), () => { });
        Assert.Throws<InvalidOperationException>(() => second.SetLeaf(0, new[] { 0 }, "aborted"));
        failing.Dispose();
        Assert.True(second.SetLeaf(0, new[] { 0 }, "second"));
        Assert.False(newSession.Replay(captured[0], forward: false));
        Assert.Equal("second", second.ValueAt(0, new[] { 0 }));
        while (deferred.Count > 0)
        {
            deferred.Dequeue()();
        }

        CampaignMissionEdit live = Assert.Single(registered);
        Assert.Same(second, live.Document);
        Assert.Empty(failures);
        Assert.False(oldSession.Replay(captured[0], forward: false));
        Assert.False(newSession.Replay(captured[0], forward: false));
        Assert.Equal("first", first.ValueAt(0, new[] { 0 }));
        Assert.True(newSession.Replay(live, forward: false));
        Assert.Equal("before", second.ValueAt(0, new[] { 0 }));
    }

    [Fact]
    public void WindEditRecordsOneBatchAndRestoresEachExactRawLeaf()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("weather.zrd", null, Row(Row(
            "WIND", Row(
                "STATIC_VELOCITY", Row(-0f, 2f, 3f, "tail"),
                "RANDOM_MAX_SPEED", 4f,
                "RANDOM_ACCEL", 0.5f,
                "RANDOM_ANG_VEL", 45f)))));
        Assert.True(CampaignWeatherWind.TryBind(document, out CampaignWeatherWind? wind, out string problem), problem);
        var edits = new List<CampaignMissionEdit>();
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });
        Assert.True(wind.Apply(wind.State with { StaticX = 0f, StaticY = 9f, RandomAccel = 0.75f }));
        CampaignMissionEdit edit = Assert.Single(edits);
        Assert.Equal(3, edit.Changes.Length);
        Assert.Equal(1, document.Revision);

        Assert.True(session.Replay(edit, forward: false));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 0, 1, 1, 0 })));
        Assert.Equal(2f, document.ValueAt(0, new[] { 0, 1, 1, 1 }));
        Assert.Equal(0.5f, document.ValueAt(0, new[] { 0, 1, 5 }));
        Assert.Equal("tail", document.ValueAt(0, new[] { 0, 1, 1, 3 }));
        Assert.False(document.IsDirty);
        Assert.True(session.Replay(edit, forward: true));
        Assert.Equal(0u, Bits(document.ValueAt(0, new[] { 0, 1, 1, 0 })));
        Assert.Equal(9f, document.ValueAt(0, new[] { 0, 1, 1, 1 }));
        Assert.Equal(0.75f, document.ValueAt(0, new[] { 0, 1, 5 }));
    }

    [Fact]
    public void ExportMovesTheCleanMarkerWithoutChangingHistory()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        var edits = new List<CampaignMissionEdit>();
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });
        string root = Path.GetFullPath(Path.Combine(".scratch", $"d32-export-{Guid.NewGuid():N}"));
        try
        {
            Assert.True(document.SetLeaf(0, new[] { 0 }, "saved"));
            CampaignMissionEdit edit = Assert.Single(edits);
            document.Export(root);
            Assert.False(document.IsDirty);
            Assert.Equal(1, document.Revision);

            Assert.True(session.Replay(edit, forward: false));
            Assert.True(document.IsDirty);
            Assert.Equal(0, document.Revision);
            Assert.Equal("before", document.ValueAt(0, new[] { 0 }));
            Assert.True(session.Replay(edit, forward: true));
            Assert.False(document.IsDirty);
            Assert.Equal(1, document.Revision);
            Assert.Equal("saved", document.ValueAt(0, new[] { 0 }));
            Assert.True(session.Replay(edit, forward: false));
            Assert.True(document.SetLeaf(0, new[] { 0 }, "alternative"));
            Assert.Equal(1, document.Revision);
            Assert.True(document.IsDirty);
            Assert.False(session.Replay(edit, forward: false));
            Assert.Equal("alternative", document.ValueAt(0, new[] { 0 }));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ReplacedOrDisposedSessionsAndMismatchedRawValuesRejectReplay()
    {
        CampaignMissionDocument first = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        CampaignMissionDocument second = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        var firstEdits = new List<CampaignMissionEdit>();
        var secondEdits = new List<CampaignMissionEdit>();
        int refreshes = 0;
        var oldSession = new CampaignMissionEditSession(first, firstEdits.Add, () => refreshes++);
        using var newSession = new CampaignMissionEditSession(second, secondEdits.Add, () => refreshes++);
        Assert.True(first.SetLeaf(0, new[] { 0 }, "after"));
        Assert.True(second.SetLeaf(0, new[] { 0 }, "after"));

        CampaignMissionEdit oldEdit = Assert.Single(firstEdits);
        Assert.False(newSession.Replay(oldEdit, forward: false));
        oldSession.Dispose();
        oldSession.Dispose();
        Assert.False(oldSession.Replay(oldEdit, forward: false));
        Assert.Equal("after", first.ValueAt(0, new[] { 0 }));
        Assert.Equal("after", second.ValueAt(0, new[] { 0 }));
        Assert.Equal(0, refreshes);

        CampaignMissionEdit newEdit = Assert.Single(secondEdits);
        second.Files[0].Root[0] = "unexpected";
        Assert.False(newSession.Replay(newEdit, forward: false));
        Assert.Equal("unexpected", second.ValueAt(0, new[] { 0 }));
        Assert.Equal(1, second.Revision);
        Assert.True(second.IsDirty);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public void StructuralChangesAndReaderCreationReplayCompleteSnapshots()
    {
        float subnormal = BitConverter.UInt32BitsToSingle(1);
        CampaignMissionDocument document = Document(new ZrdrFile("other.zrd", null, Row("kept")));
        var edits = new List<CampaignMissionEdit>();
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });
        int index = document.CreateDangerZoneReader(document.ChangeGeneration);
        List<object?> root = document.Files[index].Root;
        var replacement = Row("unknown", Row(-0f, subnormal, null, true));

        Assert.True(document.ReplaceDangerZoneRoot(index, document.ChangeGeneration, root, replacement));
        replacement.Clear();
        Assert.Equal(2, edits.Count);
        Assert.Equal(CampaignMissionEditKind.AddedReader, edits[0].Kind);
        Assert.Equal(CampaignRawValueKind.Missing, Assert.Single(edits[0].Changes).Before.Kind);
        Assert.True(session.Replay(edits[1], forward: false));
        Assert.Empty(document.Files[index].Root);
        Assert.True(session.Replay(edits[0], forward: false));
        Assert.Single(document.Files);
        Assert.False(document.IsDirty);
        Assert.True(session.Replay(edits[0], forward: true));
        Assert.True(session.Replay(edits[1], forward: true));
        Assert.Equal("unknown", document.ValueAt(index, new[] { 0 }));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(index, new[] { 1, 0 })));
        Assert.Equal(1u, Bits(document.ValueAt(index, new[] { 1, 1 })));
        Assert.Null(document.ValueAt(index, new[] { 1, 2 }));
        Assert.Equal(true, document.ValueAt(index, new[] { 1, 3 }));
    }

    [Fact]
    public void ChapterCopyAndTargetStructureReplayWithoutConsultingTheChangedChapter()
    {
        List<object?> source = Row(Row(
            Row("description", "CHAPTER"),
            Row("nodes", Row("site")),
            Row("unknown", Row(-0f, null, "tail"))));
        ZrdrSet chapter = new("chapter", new[] { new ZrdrFile("targets.zrd", null, source) });
        CampaignMissionDocument document = Document(new ZrdrFile("other.zrd", null, Row("kept")));
        var edits = new List<CampaignMissionEdit>();
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });
        CampaignMissionTargets inherited = CampaignMissionTargets.Bind(document, chapter, chapterAvailable: true);
        int fileIndex = inherited.CopyToMission();
        CampaignTargetEntry local = Assert.Single(
            CampaignMissionTargets.Bind(document, chapter, chapterAvailable: true).Entries);
        Assert.True(local.Apply(local.State with { Description = "LOCAL", OtherTarget = true }));
        Assert.Equal(2, edits.Count);
        Assert.Equal(CampaignMissionEditKind.AddedReader, edits[0].Kind);
        Assert.Single(edits[1].Changes);
        ((List<object?>)((List<object?>)source[0]!)[0]!)[1] = "CHANGED";

        Assert.True(session.Replay(edits[1], forward: false));
        Assert.Equal("CHAPTER", document.ValueAt(fileIndex, new[] { 0, 0, 1 }));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(fileIndex, new[] { 0, 2, 1, 0 })));
        Assert.True(session.Replay(edits[0], forward: false));
        Assert.Equal(-1, document.FindFileIndex("targets.json"));
        Assert.False(document.IsDirty);
        Assert.True(session.Replay(edits[0], forward: true));
        Assert.Equal("CHAPTER", document.ValueAt(fileIndex, new[] { 0, 0, 1 }));
        Assert.True(session.Replay(edits[1], forward: true));
        Assert.Equal("LOCAL", document.ValueAt(fileIndex, new[] { 0, 0, 1 }));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(fileIndex, new[] { 0, 2, 1, 0 })));
        Assert.Null(document.ValueAt(fileIndex, new[] { 0, 2, 1, 1 }));
        Assert.Equal("tail", document.ValueAt(fileIndex, new[] { 0, 2, 1, 2 }));
    }

    [Fact]
    public void ObjectiveNumberSwapRestoresAllKeysAndLinksAtomically()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("objectives.zrd", null, Row(Row(
            "OBJECTIVE1", Row("WAKE_OBJECTIVE", Row(2f)),
            "OBJECTIVE2", Row("WAKE_OBJECTIVE", Row(1f))))));
        var edits = new List<CampaignMissionEdit>();
        int changes = 0;
        document.Changed += () => changes++;
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });
        Assert.True(CampaignMissionObjectives.Bind(document).SwapNumbers(1, 2));
        CampaignMissionEdit edit = Assert.Single(edits);
        Assert.Equal(4, edit.Changes.Length);
        Assert.Equal("OBJECTIVE2", document.ValueAt(0, new[] { 0, 0 }));
        Assert.Equal(1f, document.ValueAt(0, new[] { 0, 1, 1, 0 }));
        Assert.True(session.Replay(edit, forward: false));
        Assert.Equal("OBJECTIVE1", document.ValueAt(0, new[] { 0, 0 }));
        Assert.Equal("OBJECTIVE2", document.ValueAt(0, new[] { 0, 2 }));
        Assert.Equal(2f, document.ValueAt(0, new[] { 0, 1, 1, 0 }));
        Assert.Equal(1f, document.ValueAt(0, new[] { 0, 3, 1, 0 }));
        Assert.True(session.Replay(edit, forward: true));
        Assert.Equal("OBJECTIVE2", document.ValueAt(0, new[] { 0, 0 }));
        Assert.Equal("OBJECTIVE1", document.ValueAt(0, new[] { 0, 2 }));
        Assert.Equal(1f, document.ValueAt(0, new[] { 0, 1, 1, 0 }));
        Assert.Equal(2f, document.ValueAt(0, new[] { 0, 3, 1, 0 }));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void DivergentEditsCannotReplayAnOperationAtTheSameRevision()
    {
        CampaignMissionDocument document = Document(new ZrdrFile("probe.zrd", null, Row("before")));
        var edits = new List<CampaignMissionEdit>();
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });
        Assert.True(document.SetLeaf(0, new[] { 0 }, "first"));
        CampaignMissionEdit first = Assert.Single(edits);
        Assert.True(session.Replay(first, forward: false));
        Assert.True(document.SetLeaf(0, new[] { 0 }, "second"));
        CampaignMissionEdit second = edits[1];
        Assert.Equal(first.After.Revision, second.After.Revision);
        Assert.NotEqual(first.After.Identity, second.After.Identity);
        Assert.False(session.Replay(first, forward: false));
        Assert.False(session.Replay(first, forward: true));
        Assert.Equal("second", document.ValueAt(0, new[] { 0 }));
        Assert.True(document.IsDirty);
    }

    [Fact]
    public void ReorderingPreservesEntryIdentityAndNeverRevivesStaleBindings()
    {
        CampaignMissionDocument document = Document(new ZrdrFile(
            "startanims.zrd", null, Row(Row("NEW_GAME_START",
                Row(Row("one", "first-tail"), Row("two", -0f))))));
        CampaignMissionStartAnimations original = CampaignMissionStartAnimations.Bind(document);
        List<object?> one = original.Entries[0].Raw;
        List<object?> two = original.Entries[1].Raw;
        var edits = new List<CampaignMissionEdit>();
        using var session = new CampaignMissionEditSession(document, edits.Add, () => { });

        Assert.True(original.Move(0, 1));
        CampaignMissionEdit edit = Assert.Single(edits);
        Assert.Same(two, document.ValueAt(0, new[] { 0, 1, 0 }));
        Assert.True(session.Replay(edit, forward: false));
        Assert.Same(one, document.ValueAt(0, new[] { 0, 1, 0 }));
        Assert.False(original.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => original.Move(0, 0));
        Assert.True(session.Replay(edit, forward: true));
        Assert.Same(two, document.ValueAt(0, new[] { 0, 1, 0 }));
        Assert.Equal(0x80000000u, Bits(document.ValueAt(0, new[] { 0, 1, 0, 1 })));
    }

    private static CampaignMissionDocument Document(params ZrdrFile[] files) =>
        CampaignMissionDocument.FromScope(Mission, new ZrdrSet("invented", files));

    private static List<object?> Row(params object?[] values) => new(values);

    private static uint Bits(object? value) =>
        BitConverter.SingleToUInt32Bits(Assert.IsType<float>(value));

    private sealed class EditorHistoryModel
    {
        private readonly List<(string Name, string History)> _published = new();
        private readonly List<(Action Undo, Action Redo)> _callbacks = new();
        private Action? _undo;
        private Action? _redo;
        private int _level;
        private int _position = -1;

        internal string? PendingName { get; private set; }
        internal string? PendingHistory { get; private set; }
        internal IReadOnlyList<(string Name, string History)> Published => _published;

        internal void CreateAction(string name, string? context = null)
        {
            if (PendingHistory == null)
            {
                PendingName = name;
            }
            else
            {
                _level++;
            }

            if (context != null)
            {
                Assign(context);
            }
        }

        internal void Add(string history, Action undo, Action redo)
        {
            Assign(history);
            _undo = undo;
            _redo = redo;
        }

        internal void Commit()
        {
            if (PendingHistory == null || --_level > 0)
            {
                return;
            }

            _published.Add((PendingName!, PendingHistory!));
            _callbacks.Add((_undo!, _redo!));
            _position = _published.Count - 1;
            PendingName = null;
            PendingHistory = null;
            _undo = null;
            _redo = null;
        }

        internal void Register(CampaignMissionEdit edit, CampaignMissionEditSession session)
        {
            CreateAction(edit.Name, "global");
            Add("global",
                () => Assert.True(session.Replay(edit, forward: false)),
                () => Assert.True(session.Replay(edit, forward: true)));
            Commit();
        }

        internal void Undo()
        {
            Assert.True(_position >= 0);
            _callbacks[_position--].Undo();
        }

        internal void Redo()
        {
            Assert.True(_position + 1 < _callbacks.Count);
            _callbacks[++_position].Redo();
        }

        private void Assign(string history)
        {
            if (PendingHistory == null)
            {
                PendingHistory = history;
                _level = 1;
            }
            else
            {
                Assert.Equal(PendingHistory, history);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using CSVM.Utils;
using Godot;

namespace CSVM.Tooling;

[Tool]
internal sealed partial class CampaignMissionUndoBridge : RefCounted
{
    private readonly Dictionary<long, CampaignMissionEdit> _edits = new();
    private Action<string, long>? _registerAction;
    private CampaignMissionEditSession? _session;
    private long _nextId;

    public void Replay(GodotObject keepAlive, long id, bool forward)
    {
        if (!IsInstanceValid(keepAlive)
            || keepAlive.GetInstanceId() != GetInstanceId()
            || _session == null
            || !_edits.TryGetValue(id, out CampaignMissionEdit? edit))
        {
            return;
        }

        try
        {
            if (!_session.Replay(edit, forward))
            {
                Log.Warn("ui", $"campaign editor undo: the action no longer matches its document");
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            Log.Warn("ui", $"campaign editor undo: {error.Message}");
        }
    }

    internal static void RequirePublished(
        Action<Action> subscribe,
        Action<Action> unsubscribe,
        Action commit)
    {
        bool published = false;
        void Published() => published = true;
        subscribe(Published);
        try
        {
            commit();
        }
        finally
        {
            unsubscribe(Published);
        }

        if (!published)
        {
            throw new InvalidOperationException("The campaign edit was nested inside another editor action.");
        }
    }

    internal void Bind(
        EditorUndoRedoManager manager,
        CampaignMissionDocument document,
        Action refreshed,
        Action? rolledBack = null,
        Action<Exception>? registrationFailed = null)
    {
        Bind(document, (name, id) =>
        {
            if (manager.IsCommittingAction()
                || manager.GetObjectHistoryId(this) != (int)EditorUndoRedoManager.SpecialHistory.GlobalHistory)
            {
                throw new InvalidOperationException("Cannot create a campaign edit inside another editor action.");
            }

            RequirePublished(
                handler => manager.HistoryChanged += handler,
                handler => manager.HistoryChanged -= handler,
                () =>
                {
                    manager.CreateAction(name, UndoRedo.MergeMode.Disable, this, false, true);
                    // The bound object keeps this bridge alive and identifies its history callbacks.
                    manager.AddDoMethod(this, nameof(Replay), this, id, true);
                    manager.AddUndoMethod(this, nameof(Replay), this, id, false);
                    manager.CommitAction(false);
                });
        }, refreshed, rolledBack,
            registrationFailed: registrationFailed
                ?? (error => Log.Warn("ui", $"campaign editor undo: {error.Message}")));
    }

    internal void Bind(
        CampaignMissionDocument document,
        Action<string, long> registerAction,
        Action refreshed,
        Action? rolledBack = null,
        Action<Action>? defer = null,
        Action<Exception>? registrationFailed = null)
    {
        if (_session != null)
        {
            throw new InvalidOperationException("The undo bridge is already bound.");
        }

        ArgumentNullException.ThrowIfNull(registerAction);
        _registerAction = registerAction;
        _session = new CampaignMissionEditSession(
            document, Register, refreshed, rolledBack, defer, registrationFailed);
    }

    internal void Invalidate()
    {
        _session?.Dispose();
        _session = null;
        _registerAction = null;
        _edits.Clear();
    }

    private void Register(CampaignMissionEdit edit)
    {
        Action<string, long> register = _registerAction
            ?? throw new InvalidOperationException("The editor undo manager is unavailable.");
        long id = checked(++_nextId);
        _edits.Add(id, edit);
        try
        {
            register(edit.Name, id);
        }
        catch
        {
            _edits.Remove(id);
            throw;
        }
    }
}

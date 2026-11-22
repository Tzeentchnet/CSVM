using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using CSVM.Mech3;

namespace CSVM.Tooling;

internal enum CampaignMissionEditKind
{
    Slots,
    Permutation,
    AddedReader,
}

internal enum CampaignRawValueKind
{
    Missing,
    Null,
    String,
    Float,
    Boolean,
    List,
}

internal readonly record struct CampaignRawChange(
    int FileIndex,
    ImmutableArray<int> Path,
    CampaignRawValue Before,
    CampaignRawValue After);

internal readonly record struct CampaignEditState(long Revision, long Identity, bool Dirty);

internal sealed class CampaignRawValue
{
    private CampaignRawValue(
        CampaignRawValueKind kind,
        string? text = null,
        uint bits = 0,
        bool boolean = false,
        ImmutableArray<CampaignRawValue> items = default)
    {
        Kind = kind;
        Text = text;
        Bits = bits;
        Boolean = boolean;
        Items = items;
    }

    internal static CampaignRawValue Missing { get; } = new(CampaignRawValueKind.Missing);

    internal CampaignRawValueKind Kind { get; }
    internal string? Text { get; }
    internal uint Bits { get; }
    internal bool Boolean { get; }
    internal ImmutableArray<CampaignRawValue> Items { get; }

    internal static CampaignRawValue Capture(object? value)
    {
        switch (value)
        {
            case null:
                return new CampaignRawValue(CampaignRawValueKind.Null);
            case string text:
                return new CampaignRawValue(CampaignRawValueKind.String, text: text);
            case float number:
                return new CampaignRawValue(
                    CampaignRawValueKind.Float, bits: BitConverter.SingleToUInt32Bits(number));
            case bool boolean:
                return new CampaignRawValue(CampaignRawValueKind.Boolean, boolean: boolean);
            case List<object?> list:
                var items = ImmutableArray.CreateBuilder<CampaignRawValue>(list.Count);
                foreach (object? item in list)
                {
                    items.Add(Capture(item));
                }

                return new CampaignRawValue(CampaignRawValueKind.List, items: items.MoveToImmutable());
            default:
                throw new InvalidOperationException($"Unsupported raw value type '{value.GetType().Name}'.");
        }
    }

    internal bool Matches(object? value)
    {
        switch (Kind)
        {
            case CampaignRawValueKind.Null:
                return value == null;
            case CampaignRawValueKind.String:
                return value is string text && string.Equals(Text, text, StringComparison.Ordinal);
            case CampaignRawValueKind.Float:
                return value is float number && Bits == BitConverter.SingleToUInt32Bits(number);
            case CampaignRawValueKind.Boolean:
                return value is bool boolean && Boolean == boolean;
            case CampaignRawValueKind.List:
                if (value is not List<object?> list || list.Count != Items.Length)
                {
                    return false;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (!Items[i].Matches(list[i]))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    internal object? Restore()
    {
        switch (Kind)
        {
            case CampaignRawValueKind.Null:
                return null;
            case CampaignRawValueKind.String:
                return Text!;
            case CampaignRawValueKind.Float:
                return BitConverter.UInt32BitsToSingle(Bits);
            case CampaignRawValueKind.Boolean:
                return Boolean;
            case CampaignRawValueKind.List:
                var list = new List<object?>(Items.Length);
                foreach (CampaignRawValue item in Items)
                {
                    list.Add(item.Restore());
                }

                return list;
            default:
                throw new InvalidOperationException("A missing reader has no raw value.");
        }
    }
}

internal sealed record CampaignReaderSnapshot(string Name, string? Rename, CampaignRawValue Root)
{
    internal static CampaignReaderSnapshot Capture(ZrdrFile file) =>
        new(file.Name, file.Rename, CampaignRawValue.Capture(file.Root));

    internal bool Matches(ZrdrFile file) =>
        string.Equals(Name, file.Name, StringComparison.Ordinal)
        && string.Equals(Rename, file.Rename, StringComparison.Ordinal)
        && Root.Matches(file.Root);

    internal ZrdrFile Restore() => new(Name, Rename, (List<object?>)Root.Restore()!);
}

internal sealed record CampaignMissionEdit(
    CampaignMissionDocument Document,
    string Name,
    CampaignMissionEditKind Kind,
    CampaignEditState Before,
    CampaignEditState After,
    ImmutableArray<CampaignRawChange> Changes,
    ImmutableArray<int> Permutation,
    CampaignReaderSnapshot? AddedReader);

internal sealed class CampaignMissionEditSession : IDisposable
{
    private readonly Action<CampaignMissionEdit> _register;
    private readonly Action _refreshed;
    private readonly Action? _rolledBack;
    private readonly Action<Action>? _defer;
    private readonly Action<Exception>? _registrationFailed;
    private readonly List<PendingEdit> _pending = new();
    private CampaignMissionDocument? _document;
    private bool _registering;

    internal CampaignMissionEditSession(
        CampaignMissionDocument document,
        Action<CampaignMissionEdit> register,
        Action refreshed,
        Action? rolledBack = null,
        Action<Action>? defer = null,
        Action<Exception>? registrationFailed = null)
    {
        if (defer != null)
        {
            ArgumentNullException.ThrowIfNull(registrationFailed);
        }

        _document = document ?? throw new ArgumentNullException(nameof(document));
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _refreshed = refreshed ?? throw new ArgumentNullException(nameof(refreshed));
        _rolledBack = rolledBack;
        _defer = defer;
        _registrationFailed = registrationFailed;
        document.EditCommitted += Register;
        if (_rolledBack != null)
        {
            document.EditRolledBack += _rolledBack;
        }
    }

    public void Dispose()
    {
        if (_document != null)
        {
            _document.EditCommitted -= Register;
            if (_rolledBack != null)
            {
                _document.EditRolledBack -= _rolledBack;
            }

            _document = null;
        }

        _pending.Clear();
    }

    internal bool Replay(CampaignMissionEdit edit, bool forward)
    {
        CampaignMissionDocument? document = _document;
        if (document == null)
        {
            return false;
        }

        if (_pending.Count > 0)
        {
            int firstPending = _registering && SameEdit(_pending[0].Edit, edit) ? 1 : 0;
            var unpublished = new List<CampaignMissionEdit>();
            for (int i = firstPending; i < _pending.Count; i++)
            {
                CampaignMissionEdit pending = _pending[i].Edit;
                if (SameEdit(pending, edit))
                {
                    return false;
                }

                if (!document.IsAborted(pending))
                {
                    unpublished.Add(pending);
                }
            }

            if (unpublished.Count > 0 && document.CanRejectThenReplay(unpublished, edit, forward))
            {
                _pending.RemoveRange(firstPending, _pending.Count - firstPending);
                if (!document.RejectThenReplay(unpublished, edit, forward))
                {
                    return false;
                }

                if (ReferenceEquals(_document, document))
                {
                    _refreshed();
                    if (ReferenceEquals(_document, document))
                    {
                        _registrationFailed!(new InvalidOperationException(
                            "Pending campaign edits were reverted because editor history changed before registration."));
                    }
                }

                return true;
            }
        }

        if (!document.Replay(edit, forward))
        {
            return false;
        }

        if (ReferenceEquals(_document, document))
        {
            _refreshed();
        }

        return true;
    }

    private static bool SameEdit(CampaignMissionEdit first, CampaignMissionEdit second) =>
        ReferenceEquals(first.Document, second.Document) && first.After.Identity == second.After.Identity;

    private void Register(CampaignMissionEdit edit)
    {
        if (_defer == null)
        {
            _register(edit);
            return;
        }

        var pending = new PendingEdit(edit);
        _pending.Add(pending);
        bool scheduled = false;
        try
        {
            // A pending editor action is unobservable before its history is assigned.
            // Defer until the initiating callback unwinds before touching the shared manager.
            _defer(() =>
            {
                if (!scheduled)
                {
                    throw new InvalidOperationException("The editor history callback must be deferred.");
                }

                Publish(pending);
            });
            scheduled = true;
        }
        catch
        {
            _pending.Remove(pending);
            throw;
        }
    }

    private void Publish(PendingEdit pending)
    {
        if (_document == null || !_pending.Contains(pending))
        {
            return;
        }

        pending.Ready = true;
        if (_registering)
        {
            return;
        }

        _registering = true;
        try
        {
            while (_document is { } document && _pending.Count > 0 && _pending[0].Ready)
            {
                PendingEdit current = _pending[0];
                if (document.IsAborted(current.Edit))
                {
                    _pending.RemoveAt(0);
                    continue;
                }

                try
                {
                    _register(current.Edit);
                }
                catch (Exception error)
                {
                    if (ReferenceEquals(_document, document))
                    {
                        if (_pending.Contains(current))
                        {
                            RejectPending(document, error);
                        }
                        else
                        {
                            _registrationFailed!(error);
                        }

                        continue;
                    }

                    return;
                }

                if (!ReferenceEquals(_document, document))
                {
                    return;
                }

                if (_pending.Count > 0 && ReferenceEquals(_pending[0], current))
                {
                    _pending.RemoveAt(0);
                }
            }
        }
        finally
        {
            _registering = false;
        }
    }

    private void RejectPending(CampaignMissionDocument document, Exception error)
    {
        var edits = new List<CampaignMissionEdit>(_pending.Count);
        foreach (PendingEdit pending in _pending)
        {
            if (!document.IsAborted(pending.Edit))
            {
                edits.Add(pending.Edit);
            }
        }

        _pending.Clear();
        try
        {
            if (!document.RejectUnpublished(edits))
            {
                error = new InvalidOperationException(
                    "The unpublished edits could not be rolled back because the document changed.", error);
            }
        }
        catch (Exception rollbackError)
        {
            error = new AggregateException("The unpublished edits could not be rolled back.", error, rollbackError);
        }

        if (ReferenceEquals(_document, document))
        {
            _registrationFailed!(error);
        }
    }

    private sealed class PendingEdit
    {
        internal PendingEdit(CampaignMissionEdit edit) => Edit = edit;

        internal CampaignMissionEdit Edit { get; }
        internal bool Ready { get; set; }
    }
}

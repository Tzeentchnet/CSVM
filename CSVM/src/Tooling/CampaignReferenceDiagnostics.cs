using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using CSVM.Mech3;

namespace CSVM.Tooling;

internal sealed class CampaignReferenceDiagnostics : IDisposable
{
    private readonly CampaignMissionDocument _document;
    private readonly CampaignMissionDependencyBundle _bundle;
    private readonly Guid _bindingId = Guid.NewGuid();
    private readonly Dictionary<List<object?>, int> _startEntryIds = new();
    private ReferenceSnapshot? _current;
    private CampaignAnimationInspection? _animationInspection;
    private HashSet<string>? _baselineMissing;
    private int _nextStartEntryId;
    private bool _stale = true;
    private bool _disposed;

    internal CampaignReferenceDiagnostics(
        CampaignMissionDocument document,
        CampaignMissionDependencyBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(bundle);
        if (document.Mission != bundle.Mission)
        {
            throw new ArgumentException(
                "The document and dependency bundle address different missions.",
                nameof(bundle));
        }

        _document = document;
        _bundle = bundle;
        _document.Changed += DocumentChanged;
    }

    internal event Action<ReferenceSnapshot>? Changed;

    internal ReferenceSnapshot Current
    {
        get
        {
            RefreshNow();
            return _current!;
        }
    }

    internal CampaignAnimationInspection AnimationInspection
    {
        get
        {
            RefreshNow();
            return _animationInspection!;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _document.Changed -= DocumentChanged;
        _disposed = true;
    }

    internal void RefreshNow()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_stale && _current != null)
        {
            return;
        }

        long revision = _document.Revision;
        long generation = _document.ChangeGeneration;
        ZrdrSet mission = Snapshot(_document);
        ReferenceExtraction extraction =
            CampaignReferenceExtractor.Extract(mission, _bundle);
        CampaignReferenceIndex index = CampaignReferenceIndex.Build(
            mission,
            _bundle,
            extraction.References,
            out ImmutableArray<ReferenceIssue> indexIssues);
        var checkedReferences =
            ImmutableArray.CreateBuilder<CheckedReference>(
                extraction.References.Length);
        foreach (MissionReference reference in extraction.References)
        {
            checkedReferences.Add(index.Resolve(reference));
        }

        ImmutableArray<CheckedReference> resolved =
            checkedReferences.MoveToImmutable();
        if (_baselineMissing == null)
        {
            _baselineMissing = new HashSet<string>(
                resolved
                    .Where(reference =>
                        reference.State == ReferenceState.Missing)
                    .Select(Identity),
                StringComparer.Ordinal);
        }

        var classified =
            ImmutableArray.CreateBuilder<CheckedReference>(resolved.Length);
        foreach (CheckedReference reference in resolved)
        {
            classified.Add(
                reference.State == ReferenceState.Missing
                    && _baselineMissing.Contains(Identity(reference))
                    ? reference with
                    {
                        State = ReferenceState.InheritedMissing,
                        Detail =
                            $"{reference.Detail} Present when this scope was opened.",
                    }
                    : reference);
        }

        if (_document.ChangeGeneration != generation)
        {
            throw new InvalidOperationException(
                "The campaign mission changed while references were being checked.");
        }

        var issues = ImmutableArray.CreateBuilder<ReferenceIssue>(
            extraction.Issues.Length + indexIssues.Length);
        issues.AddRange(extraction.Issues);
        issues.AddRange(indexIssues);
        _current = new ReferenceSnapshot(
            _bindingId,
            _bundle.Generation,
            revision,
            classified.MoveToImmutable(),
            issues.MoveToImmutable());
        _animationInspection = index.AnimationInspection;
        _stale = false;
        Changed?.Invoke(_current);
    }

    private static ZrdrSet Snapshot(CampaignMissionDocument document)
    {
        var files = new List<ZrdrFile>(document.Files.Count);
        foreach (ZrdrFile file in document.Files)
        {
            files.Add(
                new ZrdrFile(
                    file.Name,
                    file.Rename,
                    Copy(file.Root)));
        }

        return new ZrdrSet(
            CampaignMissionDocument.ArchivePath(document.Mission),
            files);
    }

    private static List<object?> Copy(List<object?> source)
    {
        var result = new List<object?>(source.Count);
        foreach (object? item in source)
        {
            result.Add(
                item is List<object?> list
                    ? Copy(list)
                    : item);
        }

        return result;
    }

    private void DocumentChanged()
    {
        _stale = true;
    }

    private string Identity(CheckedReference checkedReference)
    {
        MissionReference reference = checkedReference.Reference;
        ReferenceValue value = reference.Value;
        string displayed = value.Name
            ?? (value.Number is long number
                ? number.ToString(CultureInfo.InvariantCulture)
                : string.Join("/", value.Segments));
        if (reference.Rule == ReferenceRule.StartAnimation
            && reference.Source.FileIndex is int fileIndex
            && reference.Source.Path.Length > 0
            && _document.ValueAt(fileIndex, reference.Source.Path.Take(reference.Source.Path.Length - 1).ToArray())
                is List<object?> entry)
        {
            if (!_startEntryIds.TryGetValue(entry, out int id))
            {
                id = ++_nextStartEntryId;
                _startEntryIds.Add(entry, id);
            }

            return string.Join("|",
                (int)reference.Kind, (int)reference.Rule, (int)reference.Source.Scope,
                fileIndex, "start-entry", id, displayed.ToUpperInvariant());
        }

        string location = reference.Source.Context is { } context
            && (context.StartsWith("target:", StringComparison.Ordinal)
                || (context.StartsWith("danger:", StringComparison.Ordinal)
                    && !context.StartsWith("danger:objective:", StringComparison.Ordinal)))
                ? context
                : string.Join(",", reference.Source.Path);
        string anchor = reference.Rule is ReferenceRule.GeneratorChild or ReferenceRule.DangerSlot
            ? reference.Context.RelatedName?.ToUpperInvariant() ?? ""
            : "";
        if (reference.Kind == ReferenceKind.DangerZone)
        {
            displayed = displayed.ToUpperInvariant();
        }
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{reference.Kind}|{reference.Rule}|{reference.Source.Container}|{location}|{anchor}|{displayed}");
    }
}

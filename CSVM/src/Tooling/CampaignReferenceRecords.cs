using System;
using System.Collections.Immutable;
using System.Linq;

namespace CSVM.Tooling;

internal enum ReferenceScope
{
    Mission,
    Chapter,
    Shared,
    World,
    ChapterCompiled,
    MissionCompiled,
    Setup,
}

internal enum ReferenceKind
{
    WorldNode,
    WorldPath,
    Actor,
    Target,
    Message,
    Vehicle,
    Template,
    AiNet,
    StopPoint,
    Animation,
    Sequence,
    Script,
    Sound,
    Generator,
    Objective,
    BriefingRow,
    DangerZone,
    Pickup,
    ScriptedPath,
    Accent,
    Maneuver,
    ReaderPath,
    Weather,
    Puffer,
    Texture,
    Unknown,
}

internal enum ReferenceRule
{
    GlobalFirst,
    GlobalPattern,
    ScopedPath,
    RuntimeActor,
    TargetSite,
    TargetNode,
    MessageKey,
    VehicleBlock,
    TemplateName,
    NetId,
    NetName,
    NetStopPoint,
    EffectiveAnimation,
    StartAnimation,
    LocalSequence,
    ScriptSlot,
    SoundDef,
    SoundCue,
    QueuedCue,
    GeneratorName,
    GeneratorHost,
    GeneratorChild,
    GeneratorNet,
    ObjectiveNumber,
    BriefingIndex,
    DangerGeometry,
    DangerOverridePath,
    DangerSlot,
    PickupNode,
    ScriptedPath,
    AccentId,
    ManeuverName,
    ListedReader,
    WeatherZone,
    Unverified,
}

internal enum ReferenceState
{
    Resolved,
    Missing,
    InheritedMissing,
    Unverified,
    Suppressed,
}

internal enum ReferenceSeverity
{
    Info,
    Warning,
    Error,
}

internal sealed record ReferenceSource(
    ReferenceScope Scope,
    string Container,
    int? FileIndex,
    ImmutableArray<int> Path,
    ImmutableArray<ImmutableArray<int>> LeafPaths,
    string? Context,
    bool Derived);

internal sealed record ReferenceValue(
    string? Name,
    long? Number,
    ImmutableArray<string> Segments);

internal sealed record ReferenceContext(
    ImmutableArray<string> Anchor,
    string? RelatedName,
    long? RelatedNumber,
    int? CompiledNodeIndex,
    string? DefinitionKey,
    bool NeedsRuntimeStaging);

internal sealed record MissionReference(
    ReferenceKind Kind,
    ReferenceRule Rule,
    ReferenceValue Value,
    ReferenceSource Source,
    ReferenceContext Context);

internal sealed record CheckedReference(
    MissionReference Reference,
    ReferenceState State,
    string? Detail);

internal sealed record ReferenceIssue(
    string Code,
    string Detail,
    ReferenceSeverity Severity,
    ReferenceSource? Source,
    bool BlocksReady);

internal sealed record ReferenceExtraction(
    ImmutableArray<MissionReference> References,
    ImmutableArray<ReferenceIssue> Issues);

internal sealed record ReferenceSnapshot(
    Guid BindingId,
    Guid BundleGeneration,
    long DocumentRevision,
    ImmutableArray<CheckedReference> References,
    ImmutableArray<ReferenceIssue> Issues)
{
    internal bool HasErrors =>
        Issues.Any(issue => issue.BlocksReady)
        || References.Any(reference =>
            reference.State == ReferenceState.Missing);

    internal int MissingCount =>
        References.Count(reference =>
            reference.State == ReferenceState.Missing);

    internal int InheritedMissingCount =>
        References.Count(reference =>
            reference.State == ReferenceState.InheritedMissing);

    internal int UnverifiedCount =>
        References.Count(reference =>
            reference.State == ReferenceState.Unverified);
}

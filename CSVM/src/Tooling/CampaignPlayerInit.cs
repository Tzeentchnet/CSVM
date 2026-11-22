using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using CSVM.Flight.Modes;
using CSVM.Mech3;

namespace CSVM.Tooling;

/// <summary>The editable values of one objectives reader's <c>PLAYER_INIT</c>. Speed is exposed
/// in metres per second rather than the authored value the runtime multiplies by 0.1.</summary>
public readonly record struct CampaignPlayerInitState(
    float X,
    float Y,
    float Z,
    float PitchDeg,
    float YawDeg,
    float RollDeg,
    float ThrottleFrac,
    float SpeedMps);

/// <summary>
/// A typed view over one <c>PLAYER_INIT</c> value list in a
/// <see cref="CampaignMissionDocument"/>. It retains the exact raw indexes and writes through the
/// document, so unknown objective data remains untouched. Binding selects the last duplicate key,
/// matching <see cref="ZrdrDict"/>.
/// </summary>
public sealed class CampaignPlayerInit
{
    private const string ReaderName = "objectives.json";
    private const string Key = "PLAYER_INIT";

    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _valueIndex;

    private CampaignPlayerInit(
        CampaignMissionDocument document,
        int fileIndex,
        int valueIndex)
    {
        _document = document;
        _fileIndex = fileIndex;
        _valueIndex = valueIndex;
    }

    /// <summary>The authored pending flag. The editor displays but does not change it.</summary>
    public float Pending => Scalar(0);

    /// <summary>The current typed view, read from the raw document on every access.</summary>
    public CampaignPlayerInitState State =>
        new(
            Vector(1, 0),
            Vector(1, 1),
            Vector(1, 2),
            Vector(2, 0),
            Vector(2, 1),
            Vector(2, 2),
            Scalar(3),
            Scalar(4) * SpawnPoints.SpeedScale);

    /// <summary>Binds the objectives reader's last <c>PLAYER_INIT</c>. Returns false with a
    /// specific problem when the reader, key, or complete five-element shape is absent.</summary>
    public static bool TryBind(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignPlayerInit? binding,
        out string problem)
    {
        binding = null;
        int fileIndex =
            document.FindFileIndex(ReaderName);
        if (fileIndex < 0)
        {
            problem = $"'{ReaderName}' is absent";
            return false;
        }

        List<object?> root = document.Files[fileIndex].Root;
        if (root.Count == 0
            || root[0] is not List<object?> inner)
        {
            problem = $"'{ReaderName}' has no alternating root list";
            return false;
        }

        int valueIndex =
            CampaignRawList.LastListValueIndex(
                inner,
                Key);
        if (valueIndex < 0)
        {
            problem = valueIndex == -2
                ? $"'{Key}' is a bare key rather than a value list"
                : $"'{Key}' is absent";
            return false;
        }

        if (!Complete(inner[valueIndex]))
        {
            problem = $"'{Key}' is not the complete five-element player start";
            return false;
        }

        binding = new CampaignPlayerInit(
            document,
            fileIndex,
            valueIndex);
        problem = "";
        return true;
    }

    /// <summary>Writes all editable fields after validating them together. Returns whether any raw
    /// float changed.</summary>
    public bool Apply(CampaignPlayerInitState state)
    {
        Validate(state);
        float currentAuthoredSpeed = Scalar(4);
        float currentSpeedMps =
            currentAuthoredSpeed * SpawnPoints.SpeedScale;
        bool speedChanged =
            BitConverter.SingleToUInt32Bits(state.SpeedMps)
            != BitConverter.SingleToUInt32Bits(currentSpeedMps);
        float authoredSpeed = currentAuthoredSpeed;
        if (speedChanged)
        {
            authoredSpeed =
                state.SpeedMps / SpawnPoints.SpeedScale;
            if (!float.IsFinite(authoredSpeed))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(state),
                    "spawn speed is outside the writable float range");
            }
        }

        bool changed = false;
        changed |= SetVector(1, 0, state.X);
        changed |= SetVector(1, 1, state.Y);
        changed |= SetVector(1, 2, state.Z);
        changed |= SetVector(2, 0, state.PitchDeg);
        changed |= SetVector(2, 1, state.YawDeg);
        changed |= SetVector(2, 2, state.RollDeg);
        changed |= SetScalar(3, state.ThrottleFrac);
        if (speedChanged)
        {
            changed |= SetScalar(4, authoredSpeed);
        }

        return changed;
    }

    private static bool Complete(object? value) =>
        value is List<object?> pi
        && pi.Count >= 5
        && pi[0] is float pending
        && float.IsFinite(pending)
        && pi[1] is List<object?> pos
        && FiniteTriple(pos)
        && pi[2] is List<object?> rotation
        && FiniteTriple(rotation)
        && pi[3] is float throttle
        && float.IsFinite(throttle)
        && pi[4] is float speed
        && float.IsFinite(speed);

    private static bool FiniteTriple(
        List<object?> values) =>
        values.Count >= 3
        && values[0] is float x
        && float.IsFinite(x)
        && values[1] is float y
        && float.IsFinite(y)
        && values[2] is float z
        && float.IsFinite(z);

    private static void Validate(
        CampaignPlayerInitState state)
    {
        if (!float.IsFinite(state.X)
            || !float.IsFinite(state.Y)
            || !float.IsFinite(state.Z)
            || !float.IsFinite(state.PitchDeg)
            || !float.IsFinite(state.YawDeg)
            || !float.IsFinite(state.RollDeg))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "spawn position and rotation must be finite");
        }

        if (!float.IsFinite(state.ThrottleFrac)
            || state.ThrottleFrac < 0f
            || state.ThrottleFrac > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "spawn throttle must be between 0 and 1");
        }

        if (!float.IsFinite(state.SpeedMps)
            || state.SpeedMps < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "spawn speed must be a non-negative finite value");
        }
    }

    private float Scalar(int index) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { 0, _valueIndex, index })!;

    private float Vector(
        int vectorIndex,
        int component) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[]
            {
                0,
                _valueIndex,
                vectorIndex,
                component,
            })!;

    private bool SetScalar(
        int index,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[] { 0, _valueIndex, index },
            value);

    private bool SetVector(
        int vectorIndex,
        int component,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[]
            {
                0,
                _valueIndex,
                vectorIndex,
                component,
            },
            value);
}

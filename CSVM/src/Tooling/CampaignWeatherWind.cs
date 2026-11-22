using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CSVM.Tooling;

/// <summary>The editable values of one mission weather <c>WIND</c> block.</summary>
public readonly record struct CampaignWeatherWindState(
    float StaticX,
    float StaticY,
    float StaticZ,
    float RandomMaxSpeed,
    float RandomAccel,
    float RandomAngVelDeg);

/// <summary>An exact-index typed view over one mission weather reader's <c>WIND</c> block.</summary>
public sealed class CampaignWeatherWind
{
    private const string ReaderName = "weather.json";
    private const string BlockKey = "WIND";
    private const string StaticVelocityKey = "STATIC_VELOCITY";
    private const string RandomMaxSpeedKey = "RANDOM_MAX_SPEED";
    private const string RandomAccelKey = "RANDOM_ACCEL";
    private const string RandomAngVelKey = "RANDOM_ANG_VEL";

    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _windIndex;
    private readonly int _staticVelocityIndex;
    private readonly int _randomMaxSpeedIndex;
    private readonly int _randomAccelIndex;
    private readonly int _randomAngVelIndex;

    private CampaignWeatherWind(
        CampaignMissionDocument document,
        int fileIndex,
        int windIndex,
        int staticVelocityIndex,
        int randomMaxSpeedIndex,
        int randomAccelIndex,
        int randomAngVelIndex)
    {
        _document = document;
        _fileIndex = fileIndex;
        _windIndex = windIndex;
        _staticVelocityIndex = staticVelocityIndex;
        _randomMaxSpeedIndex = randomMaxSpeedIndex;
        _randomAccelIndex = randomAccelIndex;
        _randomAngVelIndex = randomAngVelIndex;
    }

    /// <summary>The current typed view, read from the raw document on every access.</summary>
    public CampaignWeatherWindState State =>
        new(
            Vector(0),
            Vector(1),
            Vector(2),
            Scalar(_randomMaxSpeedIndex),
            Scalar(_randomAccelIndex),
            Scalar(_randomAngVelIndex));

    /// <summary>Binds the outer dictionary's last <c>WIND</c> list and each first valid immediate
    /// field value inside it, matching <c>WeatherState.Load</c>.</summary>
    public static bool TryBind(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignWeatherWind? binding,
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

        int windIndex =
            CampaignRawList.LastListValueIndex(
                inner,
                BlockKey);
        if (windIndex < 0)
        {
            problem = windIndex == -2
                ? $"'{BlockKey}' is a bare key rather than a value list"
                : $"'{BlockKey}' is absent";
            return false;
        }

        var wind =
            (List<object?>)inner[windIndex]!;
        int staticVelocityIndex =
            CampaignRawList.FirstFiniteVectorValueIndex(
                wind,
                StaticVelocityKey,
                3);
        int randomMaxSpeedIndex =
            CampaignRawList.FirstFiniteFloatValueIndex(
                wind,
                RandomMaxSpeedKey);
        int randomAccelIndex =
            CampaignRawList.FirstFiniteFloatValueIndex(
                wind,
                RandomAccelKey);
        int randomAngVelIndex =
            CampaignRawList.FirstFiniteFloatValueIndex(
                wind,
                RandomAngVelKey);
        if (staticVelocityIndex < 0
            || randomMaxSpeedIndex < 0
            || randomAccelIndex < 0
            || randomAngVelIndex < 0)
        {
            problem =
                $"'{BlockKey}' does not carry its complete finite four-field shape";
            return false;
        }

        binding = new CampaignWeatherWind(
            document,
            fileIndex,
            windIndex,
            staticVelocityIndex,
            randomMaxSpeedIndex,
            randomAccelIndex,
            randomAngVelIndex);
        problem = "";
        return true;
    }

    /// <summary>Writes all wind fields after validating them together. Returns whether any raw
    /// float changed.</summary>
    public bool Apply(CampaignWeatherWindState state)
    {
        Validate(state);
        bool changed = false;
        changed |= SetVector(0, state.StaticX);
        changed |= SetVector(1, state.StaticY);
        changed |= SetVector(2, state.StaticZ);
        changed |= SetScalar(
            _randomMaxSpeedIndex,
            state.RandomMaxSpeed);
        changed |= SetScalar(
            _randomAccelIndex,
            state.RandomAccel);
        changed |= SetScalar(
            _randomAngVelIndex,
            state.RandomAngVelDeg);
        return changed;
    }

    private static void Validate(
        CampaignWeatherWindState state)
    {
        if (!float.IsFinite(state.StaticX)
            || !float.IsFinite(state.StaticY)
            || !float.IsFinite(state.StaticZ))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "static wind velocity must be finite");
        }

        if (!float.IsFinite(state.RandomMaxSpeed)
            || state.RandomMaxSpeed < 0f
            || !float.IsFinite(state.RandomAccel)
            || state.RandomAccel < 0f
            || !float.IsFinite(state.RandomAngVelDeg)
            || state.RandomAngVelDeg < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                "random wind speed, acceleration, and turn rate must be non-negative finite values");
        }
    }

    private float Scalar(int valueIndex) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { 0, _windIndex, valueIndex })!;

    private float Vector(int component) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[]
            {
                0,
                _windIndex,
                _staticVelocityIndex,
                component,
            })!;

    private bool SetScalar(
        int valueIndex,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[] { 0, _windIndex, valueIndex },
            value);

    private bool SetVector(
        int component,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[]
            {
                0,
                _windIndex,
                _staticVelocityIndex,
                component,
            },
            value);
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CSVM.Tooling;

/// <summary>The editable raw values of one mission weather fog zone.</summary>
public readonly record struct CampaignWeatherFogZoneState(
    float ColorR,
    float ColorG,
    float ColorB,
    float Near,
    float Far,
    float Low,
    float High);

/// <summary>Exact-index typed views over the runtime fog zones in one weather reader.</summary>
public sealed class CampaignWeatherFogZones
{
    private const string ReaderName = "weather.json";
    private const string ColorKey = "FOG_COLOR";
    private const string RangesKey = "FOG_RANGES";
    private const string AltitudeKey = "FOG_ALTITUDE";

    private readonly List<CampaignWeatherFogZone> _zones;

    private CampaignWeatherFogZones(
        List<CampaignWeatherFogZone> zones)
    {
        _zones = zones;
    }

    /// <summary>Runtime zone blocks in raw file order, excluding software-renderer twins.</summary>
    public IReadOnlyList<CampaignWeatherFogZone> Zones =>
        _zones;

    /// <summary>Discovers zones and binds the last list value for each field, matching
    /// <c>WeatherState.Load</c>.</summary>
    public static bool TryBind(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignWeatherFogZones? binding,
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

        var zones = new List<CampaignWeatherFogZone>();
        var names = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i + 1 < inner.Count; i++)
        {
            if (inner[i] is not string name
                || inner[i + 1] is not List<object?>
                || !IsRuntimeZone(name)
                || !names.Add(name))
            {
                continue;
            }

            int zoneIndex =
                CampaignRawList.LastListValueIndex(
                    inner,
                    name);
            if (zoneIndex < 0)
            {
                problem = zoneIndex == -2
                    ? $"'{name}' ends as a bare key rather than a value list"
                    : $"'{name}' is absent";
                return false;
            }

            var zone =
                (List<object?>)inner[zoneIndex]!;
            int colorIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    ColorKey);
            int rangesIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    RangesKey);
            int altitudeIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    AltitudeKey);
            if (!CompleteVector(zone, colorIndex, 3)
                || !CompleteVector(zone, rangesIndex, 2)
                || !CompleteVector(zone, altitudeIndex, 2))
            {
                problem =
                    $"'{name}' does not carry complete finite fog color, range, and altitude lists";
                return false;
            }

            var entry = new CampaignWeatherFogZone(
                document,
                fileIndex,
                zoneIndex,
                colorIndex,
                rangesIndex,
                altitudeIndex,
                name);
            string? stateProblem =
                entry.StateProblem(entry.State);
            if (stateProblem != null)
            {
                problem = $"'{name}' {stateProblem}";
                return false;
            }

            zones.Add(entry);
        }

        if (zones.Count == 0)
        {
            problem = $"'{ReaderName}' carries no runtime ZONE blocks";
            return false;
        }

        binding = new CampaignWeatherFogZones(zones);
        problem = "";
        return true;
    }

    private static bool IsRuntimeZone(string key) =>
        key.StartsWith(
            "ZONE",
            StringComparison.OrdinalIgnoreCase)
        && key.Length > 4
        && char.IsDigit(key[4]);

    private static bool CompleteVector(
        List<object?> values,
        int valueIndex,
        int count)
    {
        if (valueIndex < 0
            || values[valueIndex] is not List<object?> vector
            || vector.Count < count)
        {
            return false;
        }

        for (int i = 0; i < count; i++)
        {
            if (vector[i] is not float value
                || !float.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>One dynamically named weather zone's exact-index fog fields.</summary>
public sealed class CampaignWeatherFogZone
{
    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _zoneIndex;
    private readonly int _colorIndex;
    private readonly int _rangesIndex;
    private readonly int _altitudeIndex;

    internal CampaignWeatherFogZone(
        CampaignMissionDocument document,
        int fileIndex,
        int zoneIndex,
        int colorIndex,
        int rangesIndex,
        int altitudeIndex,
        string name)
    {
        _document = document;
        _fileIndex = fileIndex;
        _zoneIndex = zoneIndex;
        _colorIndex = colorIndex;
        _rangesIndex = rangesIndex;
        _altitudeIndex = altitudeIndex;
        Name = name;
    }

    /// <summary>The zone key using its first spelling in the raw file.</summary>
    public string Name { get; }

    /// <summary>Whether the current raw color uses the 0-255 encoding.</summary>
    public bool UsesByteColor =>
        CampaignWeatherColor.UsesByteEncoding(
            Color(0),
            Color(1),
            Color(2));

    /// <summary>The current authored values, including raw color components.</summary>
    public CampaignWeatherFogZoneState State =>
        new(
            Color(0),
            Color(1),
            Color(2),
            Range(0),
            Range(1),
            Altitude(0),
            Altitude(1));

    /// <summary>Writes all typed fog fields after validating them together. Returns whether any
    /// raw float changed.</summary>
    public bool Apply(CampaignWeatherFogZoneState state)
    {
        string? problem = StateProblem(state);
        if (problem != null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                problem);
        }

        bool changed = false;
        changed |= SetColor(0, state.ColorR);
        changed |= SetColor(1, state.ColorG);
        changed |= SetColor(2, state.ColorB);
        changed |= SetRange(0, state.Near);
        changed |= SetRange(1, state.Far);
        changed |= SetAltitude(0, state.Low);
        changed |= SetAltitude(1, state.High);
        return changed;
    }

    internal string? StateProblem(
        CampaignWeatherFogZoneState state)
    {
        string? colorProblem =
            CampaignWeatherColor.Problem(
                state.ColorR,
                state.ColorG,
                state.ColorB,
                UsesByteColor);
        if (colorProblem != null)
        {
            return $"fog {colorProblem}";
        }

        if (!float.IsFinite(state.Near)
            || !float.IsFinite(state.Far)
            || state.Near < 0f
            || state.Far <= state.Near)
        {
            return "fog distance must be finite, non-negative, and increase from near to far";
        }

        if (!float.IsFinite(state.Low)
            || !float.IsFinite(state.High)
            || state.High <= state.Low)
        {
            return "fog altitude must be finite and increase from low to high";
        }

        return null;
    }

    private float Color(int component) =>
        Vector(_colorIndex, component);

    private float Range(int component) =>
        Vector(_rangesIndex, component);

    private float Altitude(int component) =>
        Vector(_altitudeIndex, component);

    private float Vector(
        int valueIndex,
        int component) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[]
            {
                0,
                _zoneIndex,
                valueIndex,
                component,
            })!;

    private bool SetColor(
        int component,
        float value) =>
        SetVector(
            _colorIndex,
            component,
            value);

    private bool SetRange(
        int component,
        float value) =>
        SetVector(
            _rangesIndex,
            component,
            value);

    private bool SetAltitude(
        int component,
        float value) =>
        SetVector(
            _altitudeIndex,
            component,
            value);

    private bool SetVector(
        int valueIndex,
        int component,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[]
            {
                0,
                _zoneIndex,
                valueIndex,
                component,
            },
            value);
}

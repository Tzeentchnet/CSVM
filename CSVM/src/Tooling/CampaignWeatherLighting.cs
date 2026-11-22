using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CSVM.Tooling;

/// <summary>The editable clip and sunlight values of one mission weather zone.</summary>
public readonly record struct CampaignWeatherLightingState(
    float? ClipFar,
    float? SunDiffuse,
    float? SunAmbient,
    CampaignWeatherColorState? SunColorDiffuse,
    CampaignWeatherColorState? SunColorAmbient,
    bool? SunBicolored,
    float? SunPitch,
    float? SunYaw,
    float? SunRoll);

/// <summary>Exact-index typed views over the runtime lighting zones in one weather reader.</summary>
public sealed class CampaignWeatherLightingZones
{
    private const string ReaderName = "weather.json";
    private const string ClipRangesKey = "CLIP_RANGES";
    private const string SunDiffuseKey = "SUNLIGHT_DIFFUSE";
    private const string SunAmbientKey = "SUNLIGHT_AMBIENT";
    private const string SunColorDiffuseKey =
        "SUNLIGHT_COLOR_DIFFUSE";
    private const string SunColorAmbientKey =
        "SUNLIGHT_COLOR_AMBIENT";
    private const string SunBicoloredKey =
        "SUNLIGHT_BICOLORED";
    private const string SunOrientationKey =
        "SUNLIGHT_ORIENTATION";

    private readonly List<CampaignWeatherLightingZone> _zones;

    private CampaignWeatherLightingZones(
        List<CampaignWeatherLightingZone> zones)
    {
        _zones = zones;
    }

    /// <summary>Runtime zone blocks in raw file order, excluding software-renderer twins.</summary>
    public IReadOnlyList<CampaignWeatherLightingZone> Zones =>
        _zones;

    /// <summary>Discovers zones and binds each field's last list, matching
    /// <c>WeatherState.Load</c>.</summary>
    public static bool TryBind(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignWeatherLightingZones? binding,
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

        var zones =
            new List<CampaignWeatherLightingZone>();
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
            int clipRangesIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    ClipRangesKey);
            int sunDiffuseIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    SunDiffuseKey);
            int sunAmbientIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    SunAmbientKey);
            int sunColorDiffuseIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    SunColorDiffuseKey);
            int sunColorAmbientIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    SunColorAmbientKey);
            int sunBicoloredIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    SunBicoloredKey);
            int sunOrientationIndex =
                CampaignRawList.LastListValueIndex(
                    zone,
                    SunOrientationKey);

            string? shapeProblem =
                ShapeProblem(
                    zone,
                    clipRangesIndex,
                    sunDiffuseIndex,
                    sunAmbientIndex,
                    sunColorDiffuseIndex,
                    sunColorAmbientIndex,
                    sunBicoloredIndex,
                    sunOrientationIndex);
            if (shapeProblem != null)
            {
                problem =
                    $"'{name}' {shapeProblem}";
                return false;
            }

            var entry =
                new CampaignWeatherLightingZone(
                    document,
                    fileIndex,
                    zoneIndex,
                    clipRangesIndex,
                    sunDiffuseIndex,
                    sunAmbientIndex,
                    sunColorDiffuseIndex,
                    sunColorAmbientIndex,
                    sunBicoloredIndex,
                    sunOrientationIndex,
                    name);
            string? stateProblem =
                entry.StateProblem(entry.State);
            if (stateProblem != null)
            {
                problem =
                    $"'{name}' {stateProblem}";
                return false;
            }

            zones.Add(entry);
        }

        if (zones.Count == 0)
        {
            problem =
                $"'{ReaderName}' carries no runtime ZONE blocks";
            return false;
        }

        binding =
            new CampaignWeatherLightingZones(zones);
        problem = "";
        return true;
    }

    private static bool IsRuntimeZone(string key) =>
        key.StartsWith(
            "ZONE",
            StringComparison.OrdinalIgnoreCase)
        && key.Length > 4
        && char.IsDigit(key[4]);

    private static string? ShapeProblem(
        List<object?> zone,
        int clipRangesIndex,
        int sunDiffuseIndex,
        int sunAmbientIndex,
        int sunColorDiffuseIndex,
        int sunColorAmbientIndex,
        int sunBicoloredIndex,
        int sunOrientationIndex)
    {
        if (!OptionalVector(
                zone,
                clipRangesIndex,
                2,
                1))
        {
            return
                $"has malformed selected {ClipRangesKey}";
        }

        if (!OptionalVector(
                zone,
                sunDiffuseIndex,
                1,
                0))
        {
            return
                $"has malformed selected {SunDiffuseKey}";
        }

        if (!OptionalVector(
                zone,
                sunAmbientIndex,
                1,
                0))
        {
            return
                $"has malformed selected {SunAmbientKey}";
        }

        if (!OptionalVector(
                zone,
                sunColorDiffuseIndex,
                3,
                0,
                1,
                2))
        {
            return
                $"has malformed selected {SunColorDiffuseKey}";
        }

        if (!OptionalVector(
                zone,
                sunColorAmbientIndex,
                3,
                0,
                1,
                2))
        {
            return
                $"has malformed selected {SunColorAmbientKey}";
        }

        if (!OptionalVector(
                zone,
                sunBicoloredIndex,
                1,
                0))
        {
            return
                $"has malformed selected {SunBicoloredKey}";
        }

        if (!OptionalOrientation(
                zone,
                sunOrientationIndex))
        {
            return
                $"has malformed selected {SunOrientationKey}";
        }

        return null;
    }

    private static bool OptionalVector(
        List<object?> values,
        int valueIndex,
        int minimumCount,
        params int[] components)
    {
        if (valueIndex < 0)
        {
            return true;
        }

        if (values[valueIndex] is not List<object?> vector
            || vector.Count < minimumCount)
        {
            return false;
        }

        foreach (int component in components)
        {
            if (vector[component] is not float value
                || !float.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool OptionalOrientation(
        List<object?> values,
        int valueIndex)
    {
        if (!OptionalVector(
                values,
                valueIndex,
                2,
                0,
                1))
        {
            return false;
        }

        if (valueIndex < 0
            || values[valueIndex] is not List<object?> vector
            || vector.Count < 3
            || vector[2] is not float roll)
        {
            return true;
        }

        return float.IsFinite(roll);
    }
}

/// <summary>One dynamically named zone's exact-index clip and sunlight fields.</summary>
public sealed class CampaignWeatherLightingZone
{
    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _zoneIndex;
    private readonly int _clipRangesIndex;
    private readonly int _sunDiffuseIndex;
    private readonly int _sunAmbientIndex;
    private readonly int _sunColorDiffuseIndex;
    private readonly int _sunColorAmbientIndex;
    private readonly int _sunBicoloredIndex;
    private readonly int _sunOrientationIndex;

    internal CampaignWeatherLightingZone(
        CampaignMissionDocument document,
        int fileIndex,
        int zoneIndex,
        int clipRangesIndex,
        int sunDiffuseIndex,
        int sunAmbientIndex,
        int sunColorDiffuseIndex,
        int sunColorAmbientIndex,
        int sunBicoloredIndex,
        int sunOrientationIndex,
        string name)
    {
        _document = document;
        _fileIndex = fileIndex;
        _zoneIndex = zoneIndex;
        _clipRangesIndex = clipRangesIndex;
        _sunDiffuseIndex = sunDiffuseIndex;
        _sunAmbientIndex = sunAmbientIndex;
        _sunColorDiffuseIndex = sunColorDiffuseIndex;
        _sunColorAmbientIndex = sunColorAmbientIndex;
        _sunBicoloredIndex = sunBicoloredIndex;
        _sunOrientationIndex = sunOrientationIndex;
        Name = name;
    }

    /// <summary>The zone key using its first spelling in the raw file.</summary>
    public string Name { get; }

    /// <summary>Whether the selected diffuse color uses the 0-255 encoding.</summary>
    public bool? DiffuseUsesByteColor =>
        ColorUsesByteEncoding(
            _sunColorDiffuseIndex);

    /// <summary>Whether the selected ambient color uses the 0-255 encoding.</summary>
    public bool? AmbientUsesByteColor =>
        ColorUsesByteEncoding(
            _sunColorAmbientIndex);

    /// <summary>The current authored values. Missing runtime-fallback fields remain null.</summary>
    public CampaignWeatherLightingState State =>
        new(
            Value(_clipRangesIndex, 1),
            Value(_sunDiffuseIndex, 0),
            Value(_sunAmbientIndex, 0),
            Color(_sunColorDiffuseIndex),
            Color(_sunColorAmbientIndex),
            Bicolored(),
            Value(_sunOrientationIndex, 0),
            Value(_sunOrientationIndex, 1),
            Roll());

    /// <summary>Writes all existing typed fields after validating them together. Returns whether
    /// any raw float changed.</summary>
    public bool Apply(
        CampaignWeatherLightingState state)
    {
        string? problem =
            StateProblem(state);
        if (problem != null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                problem);
        }

        return _document.BatchLeaves("Edit lighting", () =>
        {
            bool changed = false;
            changed |= SetOptional(
                _clipRangesIndex,
                1,
                state.ClipFar);
            changed |= SetOptional(
                _sunDiffuseIndex,
                0,
                state.SunDiffuse);
            changed |= SetOptional(
                _sunAmbientIndex,
                0,
                state.SunAmbient);
            changed |= SetColor(
                _sunColorDiffuseIndex,
                state.SunColorDiffuse);
            changed |= SetColor(
                _sunColorAmbientIndex,
                state.SunColorAmbient);
            changed |= SetBicolored(
                state.SunBicolored);
            changed |= SetOptional(
                _sunOrientationIndex,
                0,
                state.SunPitch);
            changed |= SetOptional(
                _sunOrientationIndex,
                1,
                state.SunYaw);
            if (HasRoll())
            {
                changed |= SetOptional(
                    _sunOrientationIndex,
                    2,
                    state.SunRoll);
            }

            return changed;
        });
    }

    internal string? StateProblem(
        CampaignWeatherLightingState state)
    {
        string? presenceProblem =
            PresenceProblem(state);
        if (presenceProblem != null)
        {
            return presenceProblem;
        }

        if (state.ClipFar is float clipFar
            && (!float.IsFinite(clipFar)
                || clipFar <= 0f))
        {
            return
                "clip far must be finite and positive";
        }

        if (state.SunDiffuse is float diffuse
            && (!float.IsFinite(diffuse)
                || diffuse < 0f))
        {
            return
                "sun diffuse must be finite and non-negative";
        }

        if (state.SunAmbient is float ambient
            && (!float.IsFinite(ambient)
                || ambient < 0f))
        {
            return
                "sun ambient must be finite and non-negative";
        }

        string? diffuseColorProblem =
            ColorProblem(
                state.SunColorDiffuse,
                DiffuseUsesByteColor);
        if (diffuseColorProblem != null)
        {
            return
                $"sun diffuse {diffuseColorProblem}";
        }

        string? ambientColorProblem =
            ColorProblem(
                state.SunColorAmbient,
                AmbientUsesByteColor);
        if (ambientColorProblem != null)
        {
            return
                $"sun ambient {ambientColorProblem}";
        }

        if (!OptionalFinite(state.SunPitch)
            || !OptionalFinite(state.SunYaw)
            || !OptionalFinite(state.SunRoll))
        {
            return
                "sun orientation must be finite";
        }

        return null;
    }

    private static string? ColorProblem(
        CampaignWeatherColorState? color,
        bool? usesByteEncoding)
    {
        if (!color.HasValue)
        {
            return null;
        }

        return CampaignWeatherColor.Problem(
            color.Value.R,
            color.Value.G,
            color.Value.B,
            usesByteEncoding!.Value);
    }

    private static bool OptionalFinite(
        float? value) =>
        !value.HasValue
        || float.IsFinite(value.Value);

    private string? PresenceProblem(
        CampaignWeatherLightingState state)
    {
        if (state.ClipFar.HasValue
            != (_clipRangesIndex >= 0))
        {
            return
                "clip far presence cannot change";
        }

        if (state.SunDiffuse.HasValue
            != (_sunDiffuseIndex >= 0))
        {
            return
                "sun diffuse presence cannot change";
        }

        if (state.SunAmbient.HasValue
            != (_sunAmbientIndex >= 0))
        {
            return
                "sun ambient presence cannot change";
        }

        if (state.SunColorDiffuse.HasValue
            != (_sunColorDiffuseIndex >= 0))
        {
            return
                "sun diffuse color presence cannot change";
        }

        if (state.SunColorAmbient.HasValue
            != (_sunColorAmbientIndex >= 0))
        {
            return
                "sun ambient color presence cannot change";
        }

        if (state.SunBicolored.HasValue
            != (_sunBicoloredIndex >= 0))
        {
            return
                "sun bicolored presence cannot change";
        }

        bool hasOrientation =
            _sunOrientationIndex >= 0;
        if (state.SunPitch.HasValue
                != hasOrientation
            || state.SunYaw.HasValue
                != hasOrientation)
        {
            return
                "sun orientation presence cannot change";
        }

        if (state.SunRoll.HasValue != HasRoll())
        {
            return
                "sun roll presence cannot change";
        }

        return null;
    }

    private bool? ColorUsesByteEncoding(
        int valueIndex)
    {
        CampaignWeatherColorState? color =
            Color(valueIndex);
        return color.HasValue
            ? CampaignWeatherColor.UsesByteEncoding(
                color.Value.R,
                color.Value.G,
                color.Value.B)
            : null;
    }

    private CampaignWeatherColorState? Color(
        int valueIndex)
    {
        if (valueIndex < 0)
        {
            return null;
        }

        return new CampaignWeatherColorState(
            RequiredValue(valueIndex, 0),
            RequiredValue(valueIndex, 1),
            RequiredValue(valueIndex, 2));
    }

    private bool? Bicolored()
    {
        float? value =
            Value(_sunBicoloredIndex, 0);
        return value.HasValue
            ? value.Value != 0f
            : null;
    }

    private float? Roll()
    {
        if (!HasRoll())
        {
            return null;
        }

        return RequiredValue(
            _sunOrientationIndex,
            2);
    }

    private bool HasRoll()
    {
        if (_sunOrientationIndex < 0)
        {
            return false;
        }

        object? orientation =
            _document.ValueAt(
                _fileIndex,
                new[]
                {
                    0,
                    _zoneIndex,
                    _sunOrientationIndex,
                });
        return orientation is List<object?> vector
            && vector.Count >= 3
            && vector[2] is float;
    }

    private float? Value(
        int valueIndex,
        int component) =>
        valueIndex >= 0
            ? RequiredValue(
                valueIndex,
                component)
            : null;

    private float RequiredValue(
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
        int valueIndex,
        CampaignWeatherColorState? color)
    {
        if (!color.HasValue)
        {
            return false;
        }

        bool changed = false;
        changed |= SetVector(
            valueIndex,
            0,
            color.Value.R);
        changed |= SetVector(
            valueIndex,
            1,
            color.Value.G);
        changed |= SetVector(
            valueIndex,
            2,
            color.Value.B);
        return changed;
    }

    private bool SetBicolored(bool? value)
    {
        if (!value.HasValue
            || Bicolored() == value)
        {
            return false;
        }

        return SetVector(
            _sunBicoloredIndex,
            0,
            value.Value ? 1f : 0f);
    }

    private bool SetOptional(
        int valueIndex,
        int component,
        float? value) =>
        value.HasValue
        && SetVector(
            valueIndex,
            component,
            value.Value);

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

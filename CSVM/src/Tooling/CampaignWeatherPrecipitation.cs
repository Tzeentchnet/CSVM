using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CSVM.Tooling;

/// <summary>The two precipitation kinds consumed by the weather runtime.</summary>
public enum CampaignWeatherPrecipitationKind
{
    Rain,
    Snow,
}

/// <summary>The editable values of one existing mission precipitation definition.</summary>
public readonly record struct CampaignWeatherPrecipitationState(
    CampaignWeatherPrecipitationKind Kind,
    CampaignWeatherColorState Color,
    float WindDirDeg,
    float WindVel,
    float Gravity,
    int? Particles,
    float AlphaGradientX,
    float AlphaGradientY);

/// <summary>An exact-index typed view over top-level mission precipitation fields.</summary>
public sealed class CampaignWeatherPrecipitation
{
    private const string ReaderName = "weather.json";
    private const string TypeKey = "TYPE";
    private const string ColorKey = "COLOR";
    private const string WindDirKey = "WIND_DIR";
    private const string WindVelKey = "WIND_VEL";
    private const string GravityKey = "GRAVITY";
    private const string ParticlesKey = "PARTICLES";
    private const string AlphaGradientKey = "ALPHA_GRADIENT";
    private const float IntUpperBound = 2147483648f;

    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _typeIndex;
    private readonly int _colorIndex;
    private readonly int _windDirIndex;
    private readonly int _windVelIndex;
    private readonly int _gravityIndex;
    private readonly int _particlesIndex;
    private readonly int _alphaGradientIndex;

    private CampaignWeatherPrecipitation(
        CampaignMissionDocument document,
        int fileIndex,
        int typeIndex,
        int colorIndex,
        int windDirIndex,
        int windVelIndex,
        int gravityIndex,
        int particlesIndex,
        int alphaGradientIndex)
    {
        _document = document;
        _fileIndex = fileIndex;
        _typeIndex = typeIndex;
        _colorIndex = colorIndex;
        _windDirIndex = windDirIndex;
        _windVelIndex = windVelIndex;
        _gravityIndex = gravityIndex;
        _particlesIndex = particlesIndex;
        _alphaGradientIndex = alphaGradientIndex;
    }

    /// <summary>The current typed view, read from the raw document on every access.</summary>
    public CampaignWeatherPrecipitationState State =>
        new(
            Kind(),
            new CampaignWeatherColorState(
                Color(0),
                Color(1),
                Color(2)),
            Scalar(_windDirIndex),
            Scalar(_windVelIndex),
            Scalar(_gravityIndex),
            Particles(),
            AlphaGradient(0),
            AlphaGradient(1));

    /// <summary>Whether the current raw color uses the 0-255 encoding.</summary>
    public bool UsesByteColor =>
        CampaignWeatherColor.UsesByteEncoding(
            Color(0),
            Color(1),
            Color(2));

    /// <summary>Binds the first runtime-selected top-level value for every precipitation key.
    /// <c>PARTICLES</c> remains optional, matching <c>WeatherState.ParsePrecip</c>.</summary>
    public static bool TryBind(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignWeatherPrecipitation? binding,
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

        int typeIndex =
            CampaignRawList.FirstStringValueIndex(
                inner,
                TypeKey);
        if (typeIndex < 0)
        {
            problem = $"'{TypeKey}' is absent or has no immediate string value";
            return false;
        }

        if (!TryKind(
                (string)inner[typeIndex]!,
                out _))
        {
            problem = $"'{TypeKey}' must be RAIN or SNOW";
            return false;
        }

        int colorIndex =
            CampaignRawList.FirstListValueIndex(
                inner,
                ColorKey);
        int windDirIndex =
            CampaignRawList.FirstFloatValueIndex(
                inner,
                WindDirKey);
        int windVelIndex =
            CampaignRawList.FirstFloatValueIndex(
                inner,
                WindVelKey);
        int gravityIndex =
            CampaignRawList.FirstFloatValueIndex(
                inner,
                GravityKey);
        int particlesIndex =
            CampaignRawList.FirstFloatValueIndex(
                inner,
                ParticlesKey);
        int alphaGradientIndex =
            CampaignRawList.FirstVectorValueIndex(
                inner,
                AlphaGradientKey,
                2);
        if (colorIndex < 0
            || windDirIndex < 0
            || windVelIndex < 0
            || gravityIndex < 0
            || alphaGradientIndex < 0)
        {
            problem =
                "precipitation does not carry its complete runtime field shape";
            return false;
        }

        if (!CompleteColor(
                inner,
                colorIndex))
        {
            problem =
                $"'{ColorKey}' is present but is not a complete finite color triple";
            return false;
        }

        if (particlesIndex >= 0
            && !TryParticleCount(
                (float)inner[particlesIndex]!,
                out _))
        {
            problem =
                $"'{ParticlesKey}' is not a finite runtime integer";
            return false;
        }

        var candidate = new CampaignWeatherPrecipitation(
            document,
            fileIndex,
            typeIndex,
            colorIndex,
            windDirIndex,
            windVelIndex,
            gravityIndex,
            particlesIndex,
            alphaGradientIndex);
        string? stateProblem =
            candidate.StateProblem(candidate.State);
        if (stateProblem != null)
        {
            problem = stateProblem;
            return false;
        }

        binding = candidate;
        problem = "";
        return true;
    }

    /// <summary>Writes every existing field after validating the complete state. Returns whether
    /// any raw string or float changed.</summary>
    public bool Apply(
        CampaignWeatherPrecipitationState state)
    {
        string? problem = StateProblem(state);
        if (problem != null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                problem);
        }

        return _document.BatchLeaves("Edit precipitation", () =>
        {
            bool changed = false;
            changed |= SetKind(state.Kind);
            changed |= SetColor(0, state.Color.R);
            changed |= SetColor(1, state.Color.G);
            changed |= SetColor(2, state.Color.B);
            changed |= SetScalar(
                _windDirIndex,
                state.WindDirDeg);
            changed |= SetScalar(
                _windVelIndex,
                state.WindVel);
            changed |= SetScalar(
                _gravityIndex,
                state.Gravity);
            if (state.Particles is { } particles)
            {
                changed |= SetParticles(particles);
            }

            changed |= SetAlphaGradient(
                0,
                state.AlphaGradientX);
            changed |= SetAlphaGradient(
                1,
                state.AlphaGradientY);
            return changed;
        });
    }

    private static bool CompleteColor(
        List<object?> inner,
        int colorIndex)
    {
        var color =
            (List<object?>)inner[colorIndex]!;
        return color.Count >= 3
            && color[0] is float r
            && float.IsFinite(r)
            && color[1] is float g
            && float.IsFinite(g)
            && color[2] is float b
            && float.IsFinite(b);
    }

    private static bool TryKind(
        string raw,
        out CampaignWeatherPrecipitationKind kind)
    {
        if (raw.Equals(
                "RAIN",
                StringComparison.OrdinalIgnoreCase))
        {
            kind = CampaignWeatherPrecipitationKind.Rain;
            return true;
        }

        if (raw.Equals(
                "SNOW",
                StringComparison.OrdinalIgnoreCase))
        {
            kind = CampaignWeatherPrecipitationKind.Snow;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TryParticleCount(
        float raw,
        out int particles)
    {
        if (!float.IsFinite(raw)
            || raw < int.MinValue
            || raw >= IntUpperBound)
        {
            particles = default;
            return false;
        }

        particles = (int)raw;
        return true;
    }

    private string? StateProblem(
        CampaignWeatherPrecipitationState state)
    {
        if (state.Kind is not CampaignWeatherPrecipitationKind.Rain
            and not CampaignWeatherPrecipitationKind.Snow)
        {
            return "precipitation TYPE must be RAIN or SNOW";
        }

        string? colorProblem =
            CampaignWeatherColor.Problem(
                state.Color.R,
                state.Color.G,
                state.Color.B,
                UsesByteColor);
        if (colorProblem != null)
        {
            return $"precipitation {colorProblem}";
        }

        if (!float.IsFinite(state.WindDirDeg)
            || !float.IsFinite(state.WindVel)
            || !float.IsFinite(state.Gravity)
            || !float.IsFinite(state.AlphaGradientX)
            || !float.IsFinite(state.AlphaGradientY))
        {
            return "precipitation scalars and alpha gradient must be finite";
        }

        if (state.Particles.HasValue != (_particlesIndex >= 0))
        {
            return "PARTICLES presence cannot change";
        }

        return null;
    }

    private CampaignWeatherPrecipitationKind Kind()
    {
        string raw =
            (string)_document.ValueAt(
                _fileIndex,
                new[] { 0, _typeIndex })!;
        return TryKind(
            raw,
            out CampaignWeatherPrecipitationKind kind)
                ? kind
                : throw new InvalidOperationException(
                    "bound precipitation TYPE is no longer RAIN or SNOW");
    }

    private int? Particles()
    {
        if (_particlesIndex < 0)
        {
            return null;
        }

        float raw =
            Scalar(_particlesIndex);
        return TryParticleCount(
            raw,
            out int particles)
                ? particles
                : throw new InvalidOperationException(
                    "bound precipitation PARTICLES is no longer a runtime integer");
    }

    private float Scalar(int valueIndex) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { 0, valueIndex })!;

    private float Color(int component) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { 0, _colorIndex, component })!;

    private float AlphaGradient(int component) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { 0, _alphaGradientIndex, component })!;

    private bool SetKind(
        CampaignWeatherPrecipitationKind kind)
    {
        if (Kind() == kind)
        {
            return false;
        }

        string raw =
            kind == CampaignWeatherPrecipitationKind.Rain
                ? "RAIN"
                : "SNOW";
        return _document.SetLeaf(
            _fileIndex,
            new[] { 0, _typeIndex },
            raw);
    }

    private bool SetScalar(
        int valueIndex,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[] { 0, valueIndex },
            value);

    private bool SetColor(
        int component,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[] { 0, _colorIndex, component },
            value);

    private bool SetParticles(int particles)
    {
        if (Particles() == particles)
        {
            return false;
        }

        return SetScalar(
            _particlesIndex,
            particles);
    }

    private bool SetAlphaGradient(
        int component,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[] { 0, _alphaGradientIndex, component },
            value);
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CSVM.Effects;
using CSVM.Mech3;
using Godot;

namespace CSVM.Tooling;

/// <summary>One labelled altitude threshold in the campaign weather summary.</summary>
public readonly record struct CampaignWeatherBandMarker(
    string Name,
    float Altitude,
    float Position);

/// <summary>One sampled strip of the cloud-band plot, from bottom to top.</summary>
public readonly record struct CampaignWeatherBandSample(
    float Position,
    float Amount,
    Color? Color);

/// <summary>The plot-ready fog distances for one selected runtime zone.</summary>
public readonly record struct CampaignWeatherFogPlot(
    float Near,
    float Far,
    float ClipFar,
    float NearPosition,
    float FarPosition,
    float ClipPosition);

/// <summary>The plot-ready cloud-band values derived from <see cref="WeatherState"/>.</summary>
public sealed record CampaignWeatherBandPlot(
    bool HasBand,
    IReadOnlyList<CampaignWeatherBandMarker> Markers,
    IReadOnlyList<CampaignWeatherBandSample> Samples,
    float ProbePosition);

/// <summary>Read-only weather labels and plots for one zone and altitude probe.</summary>
public sealed record CampaignWeatherSummary(
    string CloudLabel,
    string ProbeLabel,
    string ZoneLabel,
    string WindLabel,
    string PrecipitationLabel,
    string LightingLabel,
    string? SelectedZone,
    float Altitude,
    float WhiteoutAmount,
    Color? WhiteoutColor,
    int BandCameraState,
    CampaignWeatherBandPlot BandPlot,
    CampaignWeatherFogPlot? FogPlot);

/// <summary>Runtime-derived read-only diagnostics over one editable mission weather reader.</summary>
public sealed class CampaignWeatherDiagnostics
{
    private const int BandSampleCount = 32;

    private readonly WeatherState _weather;

    private CampaignWeatherDiagnostics(
        WeatherState weather)
    {
        _weather = weather;
    }

    /// <summary>The runtime zone names in authored file order.</summary>
    public IReadOnlyList<string> ZoneNames =>
        _weather.ZoneNames;

    /// <summary>The default altitude probe, at the band midpoint when one exists.</summary>
    public float DefaultAltitude =>
        _weather.HasCloudBand
            ? _weather.CloudBandCentre
            : 0f;

    /// <summary>Loads the current editable document through the runtime weather reader.</summary>
    public static bool TryCreate(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignWeatherDiagnostics? diagnostics,
        out string problem)
    {
        diagnostics = null;
        WeatherState? weather =
            WeatherState.Load(
                new ZrdrSet(
                    "campaign editor weather",
                    document.Files));
        if (weather == null)
        {
            problem =
                "'weather.json' is absent or malformed";
            return false;
        }

        diagnostics =
            new CampaignWeatherDiagnostics(weather);
        problem = "";
        return true;
    }

    /// <summary>Builds one summary without loading chapter geometry or fog-volume shapes.</summary>
    public CampaignWeatherSummary Summary(
        string? requestedZone,
        float altitude)
    {
        if (!float.IsFinite(altitude))
        {
            throw new ArgumentOutOfRangeException(
                nameof(altitude),
                "Altitude must be finite.");
        }

        string? selectedZone =
            SelectedZone(requestedZone);
        float whiteout =
            _weather.WhiteoutAmount(altitude);
        Color? whiteoutColor =
            _weather.WhiteoutColor(altitude);
        int cameraState =
            _weather.CameraWeatherState(
                new Vector3(0f, altitude, 0f),
                false,
                Array.Empty<FogVolumeBox>());

        WeatherState.ZoneWeather? zone =
            selectedZone != null
                ? _weather.Zone(selectedZone)
                : null;
        return new CampaignWeatherSummary(
            CloudLabel(),
            ProbeLabel(
                altitude,
                whiteout,
                whiteoutColor,
                cameraState),
            ZoneLabel(selectedZone, zone),
            WindLabel(),
            PrecipitationLabel(),
            LightingLabel(selectedZone, zone),
            selectedZone,
            altitude,
            whiteout,
            whiteoutColor,
            cameraState,
            BandPlot(altitude),
            FogPlot(zone));
    }

    private static string ProbeLabel(
        float altitude,
        float whiteout,
        Color? color,
        int cameraState)
    {
        string colorText =
            color.HasValue
                ? $"#{color.Value.ToHtml(false)}"
                : "not authored";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Altitude probe: {Number(altitude)} m; base whiteout {Number(whiteout * 100f)}%; color {colorText}; band-only camera state {cameraState}. State 3 requires chapter fog volumes.");
    }

    private static string ZoneLabel(
        string? name,
        WeatherState.ZoneWeather? zone)
    {
        if (name == null || !zone.HasValue)
        {
            return "Selected zone: absent.";
        }

        WeatherState.ZoneWeather value =
            zone.Value;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Selected zone: {name}; fog #{value.FogColor.ToHtml(false)}, distance {Number(value.FogNear)} to {Number(value.FogFar)} m, altitude {Number(value.FogLow)} to {Number(value.FogHigh)} m; clip far {Number(value.ClipFar)} m (informational).");
    }

    private static string LightingLabel(
        string? name,
        WeatherState.ZoneWeather? zone)
    {
        if (name == null || !zone.HasValue)
        {
            return "Lighting: no runtime zone.";
        }

        WeatherState.ZoneWeather value =
            zone.Value;
        string bicolored =
            value.SunBicolored
                ? "yes"
                : "no";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Lighting: world {Number(value.WorldLight)}, diffuse {Number(value.SunDiffuse)} #{value.SunColorDiffuse.ToHtml(false)}, ambient {Number(value.SunAmbient)} #{value.SunColorAmbient.ToHtml(false)}, bicolored {bicolored}; shading orientation ({Number(Mathf.RadToDeg(value.SunOrientation.X))}, {Number(Mathf.RadToDeg(value.SunOrientation.Y))}, {Number(Mathf.RadToDeg(value.SunOrientation.Z))}) deg.");
    }

    private static CampaignWeatherBandMarker Marker(
        string name,
        float altitude,
        float bottom,
        float top) =>
        new(
            name,
            altitude,
            Position(altitude, bottom, top));

    private static CampaignWeatherFogPlot? FogPlot(
        WeatherState.ZoneWeather? zone)
    {
        if (!zone.HasValue)
        {
            return null;
        }

        WeatherState.ZoneWeather value =
            zone.Value;
        float scale =
            MathF.Max(
                1f,
                MathF.Max(
                    value.FogFar,
                    value.ClipFar));
        return new CampaignWeatherFogPlot(
            value.FogNear,
            value.FogFar,
            value.ClipFar,
            Mathf.Clamp(
                value.FogNear / scale,
                0f,
                1f),
            Mathf.Clamp(
                value.FogFar / scale,
                0f,
                1f),
            Mathf.Clamp(
                value.ClipFar / scale,
                0f,
                1f));
    }

    private static float Position(
        float value,
        float bottom,
        float top) =>
        Mathf.Clamp(
            (value - bottom)
            / (top - bottom),
            0f,
            1f);

    private static string Number(
        float value) =>
        value.ToString(
            "0.###",
            CultureInfo.InvariantCulture);

    private string? SelectedZone(
        string? requested)
    {
        if (requested != null)
        {
            foreach (string zone in _weather.ZoneNames)
            {
                if (zone.Equals(
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return zone;
                }
            }
        }

        return _weather.ZoneNames.Count > 0
            ? _weather.ZoneNames[0]
            : null;
    }

    private string CloudLabel()
    {
        if (!_weather.HasCloudBand)
        {
            return "Cloud band: absent.";
        }

        float coreTop =
            _weather.CloudBandCentre
            + (_weather.CloudThickness * 0.5f);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Cloud band: {Number(_weather.CloudBottom)} to {Number(_weather.CloudTop)} m; opaque core {Number(_weather.CloudCoreBottom)} to {Number(coreTop)} m; midpoint {Number(_weather.CloudBandCentre)} m.");
    }

    private string WindLabel() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Wind: static ({Number(_weather.WindStatic.X)}, {Number(_weather.WindStatic.Y)}, {Number(_weather.WindStatic.Z)}) m/s; random maximum {Number(_weather.WindRandomMaxSpeed)} m/s, acceleration {Number(_weather.WindRandomAccel)}, turn {Number(_weather.WindRandomAngVel)} deg/s.");

    private string PrecipitationLabel()
    {
        if (_weather.Precip is not { } precip)
        {
            return "Precipitation: absent.";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Precipitation: {precip.Kind.ToString().ToLowerInvariant()}, color #{precip.Color.ToHtml(false)}, drift {Number(precip.WindDir)} deg at {Number(precip.WindVel)}, gravity {Number(precip.Gravity)}, particles {precip.Particles}, alpha ({Number(precip.AlphaGradient.X)}, {Number(precip.AlphaGradient.Y)}).");
    }

    private CampaignWeatherBandPlot BandPlot(
        float altitude)
    {
        if (!_weather.HasCloudBand)
        {
            return new CampaignWeatherBandPlot(
                false,
                Array.Empty<CampaignWeatherBandMarker>(),
                Array.Empty<CampaignWeatherBandSample>(),
                0f);
        }

        float bottom =
            _weather.CloudBottom;
        float top =
            _weather.CloudTop;
        float coreBottom =
            _weather.CloudCoreBottom;
        float centre =
            _weather.CloudBandCentre;
        float coreTop =
            centre
            + (_weather.CloudThickness * 0.5f);
        var markers =
            new[]
            {
                Marker("bottom", bottom, bottom, top),
                Marker(
                    "core bottom / state 2",
                    coreBottom,
                    bottom,
                    top),
                Marker("midpoint", centre, bottom, top),
                Marker(
                    "core top",
                    coreTop,
                    bottom,
                    top),
                Marker("top", top, bottom, top),
            };
        var samples =
            new CampaignWeatherBandSample[
                BandSampleCount];
        for (int i = 0;
             i < samples.Length;
             i++)
        {
            float position =
                (i + 0.5f)
                / samples.Length;
            float sampleAltitude =
                Mathf.Lerp(
                    bottom,
                    top,
                    position);
            samples[i] =
                new CampaignWeatherBandSample(
                    position,
                    _weather.WhiteoutAmount(
                        sampleAltitude),
                    _weather.WhiteoutColor(
                        sampleAltitude));
        }

        return new CampaignWeatherBandPlot(
            true,
            markers,
            samples,
            Position(altitude, bottom, top));
    }
}

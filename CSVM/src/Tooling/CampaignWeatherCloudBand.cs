using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CSVM.Tooling;

/// <summary>The editable geometry and optional colors of one cloud whiteout band.</summary>
public readonly record struct CampaignWeatherCloudBandState(
    float Top,
    float Bottom,
    float Thickness,
    CampaignWeatherColorState? TopColor = null,
    CampaignWeatherColorState? BottomColor = null);

/// <summary>An exact-index typed view over one mission weather <c>CLOUD_COVER</c> block.</summary>
public sealed class CampaignWeatherCloudBand
{
    private const string ReaderName = "weather.json";
    private const string BlockKey = "CLOUD_COVER";
    private const string TopKey = "TOP";
    private const string BottomKey = "BOTTOM";
    private const string ThicknessKey = "THICKNESS";
    private const string TopColorKey = "TOP_COLOR";
    private const string BottomColorKey = "BOTTOM_COLOR";

    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _cloudIndex;
    private readonly int _topIndex;
    private readonly int _bottomIndex;
    private readonly int _thicknessIndex;
    private readonly int _topColorIndex;
    private readonly int _bottomColorIndex;

    private CampaignWeatherCloudBand(
        CampaignMissionDocument document,
        int fileIndex,
        int cloudIndex,
        int topIndex,
        int bottomIndex,
        int thicknessIndex,
        int topColorIndex,
        int bottomColorIndex)
    {
        _document = document;
        _fileIndex = fileIndex;
        _cloudIndex = cloudIndex;
        _topIndex = topIndex;
        _bottomIndex = bottomIndex;
        _thicknessIndex = thicknessIndex;
        _topColorIndex = topColorIndex;
        _bottomColorIndex = bottomColorIndex;
    }

    /// <summary>The current authored band geometry and any existing optional colors.</summary>
    public CampaignWeatherCloudBandState State =>
        new(
            Scalar(_topIndex),
            Scalar(_bottomIndex),
            Scalar(_thicknessIndex),
            Color(_topColorIndex),
            Color(_bottomColorIndex));

    /// <summary>Whether an existing top color uses raw 0-255 components.</summary>
    public bool TopColorUsesByteEncoding =>
        _topColorIndex >= 0
        && ColorUsesByteEncoding(_topColorIndex);

    /// <summary>Whether an existing bottom color uses raw 0-255 components.</summary>
    public bool BottomColorUsesByteEncoding =>
        _bottomColorIndex >= 0
        && ColorUsesByteEncoding(_bottomColorIndex);

    /// <summary>Binds the outer dictionary's last cloud list, each first valid immediate scalar,
    /// and each first immediate color list inside it, matching <c>WeatherState.Load</c>.</summary>
    public static bool TryBind(
        CampaignMissionDocument document,
        [NotNullWhen(true)] out CampaignWeatherCloudBand? binding,
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

        int cloudIndex =
            CampaignRawList.LastListValueIndex(
                inner,
                BlockKey);
        if (cloudIndex < 0)
        {
            problem = cloudIndex == -2
                ? $"'{BlockKey}' is a bare key rather than a value list"
                : $"'{BlockKey}' is absent";
            return false;
        }

        var cloud =
            (List<object?>)inner[cloudIndex]!;
        int topIndex =
            CampaignRawList.FirstFiniteFloatValueIndex(
                cloud,
                TopKey);
        int bottomIndex =
            CampaignRawList.FirstFiniteFloatValueIndex(
                cloud,
                BottomKey);
        int thicknessIndex =
            CampaignRawList.FirstFiniteFloatValueIndex(
                cloud,
                ThicknessKey);
        int topColorIndex =
            CampaignRawList.FirstListValueIndex(
                cloud,
                TopColorKey);
        int bottomColorIndex =
            CampaignRawList.FirstListValueIndex(
                cloud,
                BottomColorKey);
        if (topIndex < 0
            || bottomIndex < 0
            || thicknessIndex < 0)
        {
            problem =
                $"'{BlockKey}' does not carry its complete finite three-field geometry";
            return false;
        }

        string? colorShapeProblem =
            ColorShapeProblem(
                cloud,
                topColorIndex,
                TopColorKey)
            ?? ColorShapeProblem(
                cloud,
                bottomColorIndex,
                BottomColorKey);
        if (colorShapeProblem != null)
        {
            problem = colorShapeProblem;
            return false;
        }

        var candidate = new CampaignWeatherCloudBand(
            document,
            fileIndex,
            cloudIndex,
            topIndex,
            bottomIndex,
            thicknessIndex,
            topColorIndex,
            bottomColorIndex);
        string? stateProblem =
            candidate.StateProblem(candidate.State);
        if (stateProblem != null)
        {
            problem = $"'{BlockKey}' {stateProblem}";
            return false;
        }

        binding = candidate;
        problem = "";
        return true;
    }

    /// <summary>Writes the band and existing optional colors after validating them together.
    /// Returns whether any raw float changed.</summary>
    public bool Apply(CampaignWeatherCloudBandState state)
    {
        string? problem = StateProblem(state);
        if (problem != null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                problem);
        }

        return _document.BatchLeaves("Edit cloud band", () =>
        {
            bool changed = false;
            changed |= SetScalar(_topIndex, state.Top);
            changed |= SetScalar(_bottomIndex, state.Bottom);
            changed |= SetScalar(_thicknessIndex, state.Thickness);
            if (state.TopColor is { } topColor)
            {
                changed |= SetColor(
                    _topColorIndex,
                    topColor);
            }

            if (state.BottomColor is { } bottomColor)
            {
                changed |= SetColor(
                    _bottomColorIndex,
                    bottomColor);
            }

            return changed;
        });
    }

    private static string? ColorShapeProblem(
        List<object?> cloud,
        int colorIndex,
        string key)
    {
        if (colorIndex < 0)
        {
            return null;
        }

        var color =
            (List<object?>)cloud[colorIndex]!;
        if (color.Count < 3
            || color[0] is not float r
            || !float.IsFinite(r)
            || color[1] is not float g
            || !float.IsFinite(g)
            || color[2] is not float b
            || !float.IsFinite(b))
        {
            return $"'{key}' is present but is not a complete finite color triple";
        }

        return null;
    }

    private string? StateProblem(
        CampaignWeatherCloudBandState state)
    {
        if (!float.IsFinite(state.Top)
            || !float.IsFinite(state.Bottom)
            || state.Top <= state.Bottom)
        {
            return "band edges must be finite and TOP must be above BOTTOM";
        }

        float span = state.Top - state.Bottom;
        if (!float.IsFinite(state.Thickness)
            || state.Thickness < 0f
            || state.Thickness > span)
        {
            return "THICKNESS must be finite and between zero and the full band depth";
        }

        string? topProblem =
            ColorProblem(
                state.TopColor,
                _topColorIndex,
                TopColorUsesByteEncoding,
                TopColorKey);
        if (topProblem != null)
        {
            return topProblem;
        }

        return ColorProblem(
            state.BottomColor,
            _bottomColorIndex,
            BottomColorUsesByteEncoding,
            BottomColorKey);
    }

    private string? ColorProblem(
        CampaignWeatherColorState? color,
        int colorIndex,
        bool usesByteEncoding,
        string key)
    {
        bool exists = colorIndex >= 0;
        if (color.HasValue != exists)
        {
            return $"{key} presence cannot change";
        }

        if (color is not { } value)
        {
            return null;
        }

        string? problem =
            CampaignWeatherColor.Problem(
                value.R,
                value.G,
                value.B,
                usesByteEncoding);
        return problem == null
            ? null
            : $"{key} {problem}";
    }

    private CampaignWeatherColorState? Color(
        int valueIndex)
    {
        if (valueIndex < 0)
        {
            return null;
        }

        return new CampaignWeatherColorState(
            ColorComponent(valueIndex, 0),
            ColorComponent(valueIndex, 1),
            ColorComponent(valueIndex, 2));
    }

    private bool ColorUsesByteEncoding(
        int valueIndex) =>
        CampaignWeatherColor.UsesByteEncoding(
            ColorComponent(valueIndex, 0),
            ColorComponent(valueIndex, 1),
            ColorComponent(valueIndex, 2));

    private float ColorComponent(
        int valueIndex,
        int component) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[]
            {
                0,
                _cloudIndex,
                valueIndex,
                component,
            })!;

    private bool SetColor(
        int valueIndex,
        CampaignWeatherColorState color)
    {
        bool changed = false;
        changed |= SetColorComponent(
            valueIndex,
            0,
            color.R);
        changed |= SetColorComponent(
            valueIndex,
            1,
            color.G);
        changed |= SetColorComponent(
            valueIndex,
            2,
            color.B);
        return changed;
    }

    private bool SetColorComponent(
        int valueIndex,
        int component,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[]
            {
                0,
                _cloudIndex,
                valueIndex,
                component,
            },
            value);

    private float Scalar(int valueIndex) =>
        (float)_document.ValueAt(
            _fileIndex,
            new[] { 0, _cloudIndex, valueIndex })!;

    private bool SetScalar(
        int valueIndex,
        float value) =>
        _document.SetLeaf(
            _fileIndex,
            new[] { 0, _cloudIndex, valueIndex },
            value);
}

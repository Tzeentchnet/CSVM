using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CSVM.Tooling;

/// <summary>The editable geometry of one mission's cloud whiteout band.</summary>
public readonly record struct CampaignWeatherCloudBandState(
    float Top,
    float Bottom,
    float Thickness);

/// <summary>An exact-index typed view over one mission weather <c>CLOUD_COVER</c> block.</summary>
public sealed class CampaignWeatherCloudBand
{
    private const string ReaderName = "weather.json";
    private const string BlockKey = "CLOUD_COVER";
    private const string TopKey = "TOP";
    private const string BottomKey = "BOTTOM";
    private const string ThicknessKey = "THICKNESS";

    private readonly CampaignMissionDocument _document;
    private readonly int _fileIndex;
    private readonly int _cloudIndex;
    private readonly int _topIndex;
    private readonly int _bottomIndex;
    private readonly int _thicknessIndex;

    private CampaignWeatherCloudBand(
        CampaignMissionDocument document,
        int fileIndex,
        int cloudIndex,
        int topIndex,
        int bottomIndex,
        int thicknessIndex)
    {
        _document = document;
        _fileIndex = fileIndex;
        _cloudIndex = cloudIndex;
        _topIndex = topIndex;
        _bottomIndex = bottomIndex;
        _thicknessIndex = thicknessIndex;
    }

    /// <summary>The current authored band geometry.</summary>
    public CampaignWeatherCloudBandState State =>
        new(
            Scalar(_topIndex),
            Scalar(_bottomIndex),
            Scalar(_thicknessIndex));

    /// <summary>Binds the outer dictionary's last cloud list and each first valid immediate
    /// scalar inside it, matching <c>WeatherState.Load</c>.</summary>
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
        if (topIndex < 0
            || bottomIndex < 0
            || thicknessIndex < 0)
        {
            problem =
                $"'{BlockKey}' does not carry its complete finite three-field geometry";
            return false;
        }

        var candidate = new CampaignWeatherCloudBand(
            document,
            fileIndex,
            cloudIndex,
            topIndex,
            bottomIndex,
            thicknessIndex);
        string? stateProblem =
            StateProblem(candidate.State);
        if (stateProblem != null)
        {
            problem = $"'{BlockKey}' {stateProblem}";
            return false;
        }

        binding = candidate;
        problem = "";
        return true;
    }

    /// <summary>Writes the band geometry after validating it together. Returns whether any raw
    /// float changed.</summary>
    public bool Apply(CampaignWeatherCloudBandState state)
    {
        string? problem = StateProblem(state);
        if (problem != null)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                problem);
        }

        bool changed = false;
        changed |= SetScalar(_topIndex, state.Top);
        changed |= SetScalar(_bottomIndex, state.Bottom);
        changed |= SetScalar(_thicknessIndex, state.Thickness);
        return changed;
    }

    private static string? StateProblem(
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

        return null;
    }

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

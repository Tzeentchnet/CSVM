using System;

namespace CSVM.Tooling;

/// <summary>One weather color in its authored raw encoding.</summary>
public readonly record struct CampaignWeatherColorState(
    float R,
    float G,
    float B);

/// <summary>Shared validation for editable weather color triples.</summary>
internal static class CampaignWeatherColor
{
    internal static bool UsesByteEncoding(
        float r,
        float g,
        float b) =>
        r > 1f
        || g > 1f
        || b > 1f;

    internal static string? Problem(
        float r,
        float g,
        float b,
        bool usesByteEncoding)
    {
        if (!float.IsFinite(r)
            || !float.IsFinite(g)
            || !float.IsFinite(b))
        {
            return "color components must be finite";
        }

        float max =
            MathF.Max(
                r,
                MathF.Max(g, b));
        float min =
            MathF.Min(
                r,
                MathF.Min(g, b));
        if (usesByteEncoding)
        {
            bool black = max == 0f;
            if (min < 0f
                || max > 255f
                || (max <= 1f && !black))
            {
                return "0-255 color must stay within that encoding and cannot become an ambiguous all-0-to-1 triple";
            }
        }
        else if (min < 0f || max > 1f)
        {
            return "normalized color components must stay between 0 and 1";
        }

        return null;
    }
}

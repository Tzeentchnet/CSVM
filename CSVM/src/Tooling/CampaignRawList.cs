using System;
using System.Collections.Generic;

namespace CSVM.Tooling;

/// <summary>Shared exact-index lookups over raw alternating reader lists.</summary>
internal static class CampaignRawList
{
    // Mirrors ZrdrDict's alternating walk, including the last duplicate and bare-key behavior.
    internal static int LastListValueIndex(
        List<object?> values,
        string wanted)
    {
        int found = -1;
        int i = 0;
        while (i < values.Count)
        {
            if (values[i] is not string key)
            {
                i++;
                continue;
            }

            if (i + 1 < values.Count
                && values[i + 1] is List<object?>)
            {
                if (key.Equals(
                        wanted,
                        StringComparison.OrdinalIgnoreCase))
                {
                    found = i + 1;
                }

                i += 2;
            }
            else
            {
                if (key.Equals(
                        wanted,
                        StringComparison.OrdinalIgnoreCase))
                {
                    found = -2;
                }

                i++;
            }
        }

        return found;
    }

    internal static int FirstFiniteFloatValueIndex(
        List<object?> values,
        string wanted)
    {
        for (int i = 0; i + 1 < values.Count; i++)
        {
            if (values[i] is string key
                && key.Equals(
                    wanted,
                    StringComparison.OrdinalIgnoreCase)
                && values[i + 1] is float value
                && float.IsFinite(value))
            {
                return i + 1;
            }
        }

        return -1;
    }

    internal static int FirstListValueIndex(
        List<object?> values,
        string wanted)
    {
        for (int i = 0; i + 1 < values.Count; i++)
        {
            if (values[i] is string key
                && key.Equals(
                    wanted,
                    StringComparison.OrdinalIgnoreCase)
                && values[i + 1] is List<object?>)
            {
                return i + 1;
            }
        }

        return -1;
    }

    internal static int FirstFiniteVectorValueIndex(
        List<object?> values,
        string wanted,
        int componentCount)
    {
        for (int i = 0; i + 1 < values.Count; i++)
        {
            if (values[i] is not string key
                || !key.Equals(
                    wanted,
                    StringComparison.OrdinalIgnoreCase)
                || values[i + 1] is not List<object?> vector
                || vector.Count < componentCount)
            {
                continue;
            }

            bool finite = true;
            for (int component = 0;
                 component < componentCount;
                 component++)
            {
                finite &= vector[component] is float value
                    && float.IsFinite(value);
            }

            if (finite)
            {
                return i + 1;
            }
        }

        return -1;
    }
}

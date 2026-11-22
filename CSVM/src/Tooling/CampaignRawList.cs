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
}

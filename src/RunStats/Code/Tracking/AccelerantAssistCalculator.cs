using System;

namespace RunStats.Tracking;

public static class AccelerantAssistCalculator
{
    public static bool TryCalculate(
        PoisonDamageAllocation allocation,
        ulong sponsorNetId,
        out long assistedDamage)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        assistedDamage = 0;
        try
        {
            foreach (var entry in allocation.PlayerDamage)
            {
                if (entry.Key != sponsorNetId)
                {
                    assistedDamage = checked(assistedDamage + entry.Value);
                }
            }

            return true;
        }
        catch (OverflowException)
        {
            assistedDamage = 0;
            return false;
        }
    }
}

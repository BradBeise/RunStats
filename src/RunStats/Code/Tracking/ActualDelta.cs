using System;

namespace RunStats.Tracking;

public readonly record struct SignedDelta(long Gained, long Lost)
{
    public static SignedDelta Between(int previous, int current)
    {
        var delta = (long)current - previous;
        return delta switch
        {
            > 0 => new SignedDelta(delta, 0),
            < 0 => new SignedDelta(0, -delta),
            _ => new SignedDelta(0, 0)
        };
    }
}

public static class ActualDelta
{
    public static long Positive(int previous, int current) => Math.Max((long)current - previous, 0);

    public static long ResolvedDamage(int unblockedDamage) => Math.Max(unblockedDamage, 0);
}

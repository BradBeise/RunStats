namespace RunStats.Models;

public static class StatSemantics
{
    public static bool IsSigned(StatKind kind) =>
        kind is StatKind.AssistedDamage or StatKind.AssistedDamagePrevented;

    public static bool IsValidTotal(StatKind kind, long value) =>
        IsSigned(kind) || value >= 0;

    public static bool IsValidMutation(StatKind kind, long amount) =>
        IsSigned(kind) ? amount != 0 : amount > 0;
}

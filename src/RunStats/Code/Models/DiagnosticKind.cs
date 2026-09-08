namespace RunStats.Models;

public enum DiagnosticKind
{
    AmbiguousAssistedDamage = 0,
    AmbiguousAssistedDamagePrevented = 1,
    UnsupportedDamageSource = 2,
    UnsupportedHealingSource = 3,
    DuplicateEventSuppressed = 4
}

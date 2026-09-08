using System.Collections.Generic;

namespace RunStats.Tracking;

public sealed class KillCreditLedger
{
    private readonly HashSet<object> _creditedCreatures = new(ReferenceEqualityComparer.Instance);

    public bool TryCredit(object creatureToken) => _creditedCreatures.Add(creatureToken);

    public void Clear() => _creditedCreatures.Clear();
}

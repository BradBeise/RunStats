using System;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace RunStats.Integration;

internal sealed class CreatureSubscription : IDisposable
{
    private readonly Creature _creature;
    private readonly Action<int, int> _blockChanged;
    private readonly Action<int, int> _currentHpChanged;
    private readonly Action<int, int> _maxHpChanged;
    private bool _disposed;

    public CreatureSubscription(Creature creature)
    {
        _creature = creature;
        _blockChanged = (previous, current) => RunStatsRuntime.OnBlockChanged(creature, previous, current);
        _currentHpChanged = (previous, current) => RunStatsRuntime.OnCurrentHpChanged(creature, previous, current);
        _maxHpChanged = (previous, current) => RunStatsRuntime.OnMaxHpChanged(creature, previous, current);

        creature.BlockChanged += _blockChanged;
        creature.CurrentHpChanged += _currentHpChanged;
        creature.MaxHpChanged += _maxHpChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _creature.BlockChanged -= _blockChanged;
        _creature.CurrentHpChanged -= _currentHpChanged;
        _creature.MaxHpChanged -= _maxHpChanged;
        _disposed = true;
    }
}

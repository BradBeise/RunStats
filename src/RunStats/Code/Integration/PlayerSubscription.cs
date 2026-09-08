using System;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace RunStats.Integration;

internal sealed class PlayerSubscription : IDisposable
{
    private readonly Player _player;
    private readonly Action<RelicModel> _relicObtained;
    private readonly Action<PotionModel> _potionProcured;
    private bool _disposed;

    public PlayerSubscription(Player player)
    {
        _player = player;
        _relicObtained = relic => RunStatsRuntime.OnRelicObtained(player, relic);
        _potionProcured = potion => RunStatsRuntime.OnPotionObtained(player, potion);

        player.RelicObtained += _relicObtained;
        player.PotionProcured += _potionProcured;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _player.RelicObtained -= _relicObtained;
        _player.PotionProcured -= _potionProcured;
        _disposed = true;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using RunStats.Models;

namespace RunStats.Tracking;

public sealed record DoomKillCredit(
    object EnemyToken,
    IReadOnlyDictionary<ulong, long> Applied,
    IReadOnlyDictionary<ulong, long> DirectDamage,
    long UnattributedApplied,
    int HpBefore);

public sealed class DoomStatTracker
{
    private sealed class EnemyContributions
    {
        public int ObservedAmount;
        public long UnattributedApplied;
        public Dictionary<ulong, long> Applied { get; } = new();
        public Dictionary<ulong, long> DirectDamage { get; } = new();
    }

    private readonly RunStatsState _state;
    private readonly CoreCombatTracker _combatTracker;
    private readonly Dictionary<object, EnemyContributions> _enemies =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _completedKills = new(ReferenceEqualityComparer.Instance);

    public DoomStatTracker(RunStatsState state, CoreCombatTracker combatTracker)
    {
        _state = state;
        _combatTracker = combatTracker;
    }

    public void ObserveAmount(object enemy, int previous, int current, ulong? contributor)
    {
        if (previous < 0 || current < 0)
        {
            return;
        }

        if (!_enemies.TryGetValue(enemy, out var entry))
        {
            entry = new EnemyContributions
            {
                ObservedAmount = previous,
                UnattributedApplied = previous
            };
        }
        else if (entry.ObservedAmount != previous)
        {
            // A missed mutation makes this enemy's provenance unsafe to use.
            _enemies.Remove(enemy);
            return;
        }

        var added = (long)current - previous;
        if (added > 0 && contributor is > 0)
        {
            entry.Applied.TryGetValue(contributor.Value, out var prior);
            entry.Applied[contributor.Value] = checked(prior + added);
            _state.TryApply(StatMutation.Add(contributor.Value, StatKind.DoomApplied, added));
        }
        else if (added > 0)
        {
            entry.UnattributedApplied = checked(entry.UnattributedApplied + added);
        }

        entry.ObservedAmount = current;
        if (current == 0)
        {
            entry.Applied.Clear();
            entry.UnattributedApplied = 0;
        }
        _enemies[enemy] = entry;
    }

    public void RecordDirectDamage(object enemy, ulong? player, long actualDamage)
    {
        if (player is not > 0 || actualDamage <= 0)
        {
            return;
        }

        if (!_enemies.TryGetValue(enemy, out var entry))
        {
            entry = new EnemyContributions();
            _enemies.Add(enemy, entry);
        }

        entry.DirectDamage.TryGetValue(player.Value, out var prior);
        entry.DirectDamage[player.Value] = checked(prior + actualDamage);
    }

    public DoomKillCredit CaptureKill(object enemy, int hpBefore)
    {
        _enemies.TryGetValue(enemy, out var entry);
        return new DoomKillCredit(
            enemy,
            new Dictionary<ulong, long>(entry?.Applied ?? new Dictionary<ulong, long>()),
            new Dictionary<ulong, long>(entry?.DirectDamage ?? new Dictionary<ulong, long>()),
            entry?.UnattributedApplied ?? 0,
            hpBefore);
    }

    public void CompleteKill(DoomKillCredit credit, long hpRemoved, CombatRoomKind roomKind, ulong entropy)
    {
        if (!_completedKills.Add(credit.EnemyToken))
        {
            return;
        }
        _enemies.Remove(credit.EnemyToken);
        var totalDoom = (BigInteger)credit.UnattributedApplied;
        foreach (var amount in credit.Applied.Values)
        {
            totalDoom += amount;
        }
        if (totalDoom > 0 && hpRemoved > 0)
        {
            var participants = credit.Applied
                .Select(pair => new DamageShare(pair.Key, pair.Value, false))
                .ToList();
            if (credit.UnattributedApplied > 0)
            {
                participants.Add(new DamageShare(0, credit.UnattributedApplied, true));
            }
            long distributed = 0;
            foreach (var share in participants)
            {
                var product = (BigInteger)hpRemoved * share.Weight;
                share.Damage = (long)(product / totalDoom);
                share.Remainder = product % totalDoom;
                distributed += share.Damage;
            }
            foreach (var share in participants
                         .OrderByDescending(share => share.Remainder)
                         .ThenBy(share => share.Unattributed)
                         .ThenBy(share => share.PlayerNetId)
                         .Take(checked((int)(hpRemoved - distributed))))
            {
                share.Damage++;
            }
            foreach (var share in participants.Where(share => !share.Unattributed && share.Damage > 0))
            {
                _state.TryApply(StatMutation.Add(share.PlayerNetId, StatKind.DamageDealt, share.Damage));
            }
        }

        var candidates = credit.Applied.Where(pair => pair.Value > 0).ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var mostDoom = candidates.Max(pair => pair.Value);
        var tied = candidates.Where(pair => pair.Value == mostDoom).Select(pair => pair.Key).ToArray();
        var mostDamage = tied.Max(player => credit.DirectDamage.GetValueOrDefault(player));
        tied = tied.Where(player => credit.DirectDamage.GetValueOrDefault(player) == mostDamage)
            .OrderBy(player => player).ToArray();
        var winner = tied[(int)(entropy % (ulong)tied.Length)];
        _combatTracker.RecordAttributedKill(winner, credit.EnemyToken, roomKind);
    }

    public void Remove(object enemy)
    {
        if (_enemies.TryGetValue(enemy, out var entry))
        {
            entry.ObservedAmount = 0;
            entry.Applied.Clear();
            entry.UnattributedApplied = 0;
        }
    }

    public void Clear()
    {
        _enemies.Clear();
        _completedKills.Clear();
    }

    private sealed class DamageShare
    {
        public DamageShare(ulong playerNetId, long weight, bool unattributed)
        {
            PlayerNetId = playerNetId;
            Weight = weight;
            Unattributed = unattributed;
        }

        public ulong PlayerNetId { get; }
        public long Weight { get; }
        public bool Unattributed { get; }
        public long Damage { get; set; }
        public BigInteger Remainder { get; set; }
    }
}

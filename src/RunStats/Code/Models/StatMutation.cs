namespace RunStats.Models;

public readonly record struct StatMutation(
    ulong PlayerNetId,
    StatKind Kind,
    long Amount,
    string? SourceId = null)
{
    public static StatMutation Add(ulong playerNetId, StatKind kind, long amount = 1) =>
        new(playerNetId, kind, amount);

    public static StatMutation CardPlayed(ulong playerNetId, string cardId, long amount = 1) =>
        new(playerNetId, StatKind.CardsPlayed, amount, cardId);
}

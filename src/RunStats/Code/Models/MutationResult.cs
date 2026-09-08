namespace RunStats.Models;

public enum MutationResult
{
    Applied = 0,
    NoActiveRun = 1,
    UnknownPlayer = 2,
    InvalidStat = 3,
    InvalidAmount = 4,
    InvalidSource = 5,
    Overflow = 6
}

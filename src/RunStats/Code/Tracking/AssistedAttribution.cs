namespace RunStats.Tracking;

public static class AssistedAttribution
{
    public static bool TryGetTeammate(
        ContributorResult contributor,
        ulong beneficiaryNetId,
        out ulong contributorNetId)
    {
        contributorNetId = 0;
        if (contributor.Resolution != ContributorResolution.Unique ||
            contributor.PlayerNetId == beneficiaryNetId)
        {
            return false;
        }

        contributorNetId = contributor.PlayerNetId;
        return true;
    }
}

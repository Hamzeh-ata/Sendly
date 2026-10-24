namespace Sendly.Application.Services;

public class DiscoveryYieldSignals
{
    public int RequestedProspects { get; set; }

    public int QualifiedProspects { get; set; }

    public int UniqueTargetBusinesses { get; set; }

    public int OfficialDomainVerified { get; set; }

    public int AttributedEmails { get; set; }

    public int HtmlFetchesSucceeded { get; set; }

    public int FetchesAttempted { get; set; }

    public int IrrelevantEntities { get; set; }

    public int EntitiesClassified { get; set; }

    public int Duplicates { get; set; }

    public int CandidatesProcessed { get; set; }

    public int FreshSourceUrls { get; set; }

    public int PriorSourceUrls { get; set; }

    public int SourceFailures { get; set; }

    public int SourcesAttempted { get; set; }

    public int AssetRejections { get; set; }

    public int DirectoryEntities { get; set; }

    public int UsefulPendingUrls { get; set; }

    public int UrlsDiscovered { get; set; }

    public double QualifiedYieldPercent
        => RequestedProspects <= 0
            ? 100
            : Math.Round(QualifiedProspects * 100.0 / RequestedProspects, 1);

    public double HtmlFetchSuccessPercent
        => FetchesAttempted <= 0
            ? 100
            : Math.Round(HtmlFetchesSucceeded * 100.0 / FetchesAttempted, 1);

    public double IrrelevantEntityPercent
        => EntitiesClassified <= 0
            ? 0
            : Math.Round(IrrelevantEntities * 100.0 / EntitiesClassified, 1);

    public double DuplicatePercent
        => CandidatesProcessed <= 0
            ? 0
            : Math.Round(Duplicates * 100.0 / CandidatesProcessed, 1);

    public double FreshSourcePercent
    {
        get
        {
            var total = FreshSourceUrls + PriorSourceUrls;
            return total <= 0 ? 0 : Math.Round(FreshSourceUrls * 100.0 / total, 1);
        }
    }

    public double SourceFailurePercent
        => SourcesAttempted <= 0
            ? 0
            : Math.Round(SourceFailures * 100.0 / SourcesAttempted, 1);

    public double AssetRejectionPercent
        => UrlsDiscovered <= 0
            ? 0
            : Math.Round(AssetRejections * 100.0 / Math.Max(UrlsDiscovered + AssetRejections, 1), 1);

    public bool IsUnderperforming(int minFetchesBeforeJudge = 8)
    {
        if (FetchesAttempted < minFetchesBeforeJudge && UrlsDiscovered < minFetchesBeforeJudge)
        {
            return false;
        }

        if (RequestedProspects > 0 && QualifiedProspects == 0 && FetchesAttempted >= minFetchesBeforeJudge)
        {
            return true;
        }

        if (QualifiedYieldPercent < 20 && FetchesAttempted >= minFetchesBeforeJudge)
        {
            return true;
        }

        if (AttributedEmails == 0 && UniqueTargetBusinesses > 0 && FetchesAttempted >= minFetchesBeforeJudge)
        {
            return true;
        }

        if (HtmlFetchSuccessPercent < 50 && FetchesAttempted >= minFetchesBeforeJudge)
        {
            return true;
        }

        if (IrrelevantEntityPercent >= 40 && EntitiesClassified >= 5)
        {
            return true;
        }

        if (DuplicatePercent >= 40 && CandidatesProcessed >= 5)
        {
            return true;
        }

        if (FreshSourcePercent < 25 && PriorSourceUrls >= 10)
        {
            return true;
        }

        return false;
    }
}

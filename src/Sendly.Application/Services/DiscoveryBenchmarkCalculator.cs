namespace Sendly.Application.Services;

public class DiscoveryBenchmarkSample
{
    public string CompanyName { get; set; } = string.Empty;

    public string? WebsiteDomain { get; set; }

    public bool IsRelevant { get; set; } = true;

    public bool HasPublicContact { get; set; }
}

public class DiscoveryBenchmarkObservation
{
    public string CompanyName { get; set; } = string.Empty;

    public string? WebsiteDomain { get; set; }

    public bool CorrectlyIdentified { get; set; }

    public bool Qualified { get; set; }

    public bool LiveWebsiteVerified { get; set; }

    public bool PublicContactWithEvidence { get; set; }

    public bool IsDuplicate { get; set; }

    public bool IsFalsePositive { get; set; }
}

public class DiscoveryBenchmarkResult
{
    public string BenchmarkName { get; set; } = string.Empty;

    public int ReferenceSampleSize { get; set; }

    public int UniqueCompaniesDiscovered { get; set; }

    public int CorrectlyIdentified { get; set; }

    public int QualifiedCompanies { get; set; }

    public int LiveWebsitesVerified { get; set; }

    public int PublicContactsWithEvidence { get; set; }

    public int DuplicateCount { get; set; }

    public int FalsePositiveCount { get; set; }

    public double? Recall { get; set; }

    public double Precision { get; set; }

    public double DuplicateRate { get; set; }

    public double FalsePositiveRate { get; set; }

    public long ElapsedMilliseconds { get; set; }

    public string? Limitations { get; set; }

    public DateTimeOffset EvaluatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class DiscoveryBenchmarkCalculator
{
    public static DiscoveryBenchmarkResult Evaluate(
        string benchmarkName,
        IReadOnlyList<DiscoveryBenchmarkSample> reference,
        IReadOnlyList<DiscoveryBenchmarkObservation> observations,
        long elapsedMilliseconds,
        string? limitations,
        bool recallIsDefined)
    {
        var unique = observations.Count(x => !x.IsDuplicate);
        var correctlyIdentified = observations.Count(x => x.CorrectlyIdentified && !x.IsDuplicate);
        var qualified = observations.Count(x => x.Qualified && !x.IsDuplicate);
        var liveVerified = observations.Count(x => x.LiveWebsiteVerified && !x.IsDuplicate);
        var contacts = observations.Count(x => x.PublicContactWithEvidence && !x.IsDuplicate);
        var duplicates = observations.Count(x => x.IsDuplicate);
        var falsePositives = observations.Count(x => x.IsFalsePositive && !x.IsDuplicate);
        var relevantReference = reference.Count(x => x.IsRelevant);

        double? recall = null;
        if (recallIsDefined && relevantReference > 0)
        {
            recall = Math.Round(correctlyIdentified / (double)relevantReference, 4);
        }

        var precisionDenominator = Math.Max(unique, 1);
        return new DiscoveryBenchmarkResult
        {
            BenchmarkName = benchmarkName,
            ReferenceSampleSize = reference.Count,
            UniqueCompaniesDiscovered = unique,
            CorrectlyIdentified = correctlyIdentified,
            QualifiedCompanies = qualified,
            LiveWebsitesVerified = liveVerified,
            PublicContactsWithEvidence = contacts,
            DuplicateCount = duplicates,
            FalsePositiveCount = falsePositives,
            Recall = recall,
            Precision = Math.Round(correctlyIdentified / (double)precisionDenominator, 4),
            DuplicateRate = Math.Round(duplicates / (double)Math.Max(observations.Count, 1), 4),
            FalsePositiveRate = Math.Round(falsePositives / (double)precisionDenominator, 4),
            ElapsedMilliseconds = elapsedMilliseconds,
            Limitations = limitations,
            EvaluatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

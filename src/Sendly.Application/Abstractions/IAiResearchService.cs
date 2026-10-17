namespace Sendly.Application.Abstractions;

public class AiClassificationRequest
{
    public string ProjectName { get; set; } = string.Empty;

    public string? ProjectDescription { get; set; }

    public string? ResearchInstructions { get; set; }

    public List<string> Keywords { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public List<string> Countries { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<string> TargetRoles { get; set; } = [];

    public string CompanyName { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? Country { get; set; }

    public string? City { get; set; }

    public string? Industry { get; set; }

    public string? CompanyDescription { get; set; }

    public string? CompanySize { get; set; }

    public string? ContactName { get; set; }

    public string? ContactRole { get; set; }

    public string? Email { get; set; }

    public string? WhatsApp { get; set; }

    public string? SourceUrl { get; set; }

    public string? ResearchNotes { get; set; }
}

public class AiQualificationResult
{
    public bool IsRelevant { get; set; }

    public bool IsQualified { get; set; }

    public int QualificationScore { get; set; }

    public int? EmailConfidenceScore { get; set; }

    public int? WhatsAppConfidenceScore { get; set; }

    public string? Industry { get; set; }

    public string? CompanySize { get; set; }

    public string? ContactRole { get; set; }

    public string Summary { get; set; } = string.Empty;

    public bool NeedsReview { get; set; }
}

public class AiTemplateRecommendationRequest
{
    public string? Industry { get; set; }

    public string? CompanySize { get; set; }

    public string? CompanyName { get; set; }

    public string? ContactRole { get; set; }

    public string? Country { get; set; }

    public List<AiTemplateOption> Templates { get; set; } = [];
}

public class AiTemplateOption
{
    public Guid TemplateId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; }

    public string? MatchIndustry { get; set; }

    public string? MatchCompanySize { get; set; }

    public string? MatchNotes { get; set; }
}

public class AiTemplateRecommendationResult
{
    public Guid? TemplateId { get; set; }

    public string? Reason { get; set; }

    public int Confidence { get; set; }
}

public class AiPersonalizationResult
{
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AiSourceStructureRequest
{
    public string ProjectName { get; set; } = string.Empty;

    public string? ProjectDescription { get; set; }

    public string? ResearchInstructions { get; set; }

    public List<string> Keywords { get; set; } = [];

    public List<string> TargetRoles { get; set; } = [];

    public string SourceUrl { get; set; } = string.Empty;

    public string? SourceName { get; set; }

    public string? HintCompanyName { get; set; }

    public string? HintCountry { get; set; }

    public string? HintIndustry { get; set; }

    public string PageText { get; set; } = string.Empty;

    public List<string> ExtractedEmails { get; set; } = [];

    public List<string> ExtractedPhones { get; set; } = [];
}

public interface IAiResearchService
{
    bool IsAvailable { get; }

    Task<AiQualificationResult> QualifyAsync(
        AiClassificationRequest request,
        CancellationToken cancellationToken);

    Task<AiTemplateRecommendationResult> RecommendTemplateAsync(
        AiTemplateRecommendationRequest request,
        CancellationToken cancellationToken);

    Task<AiPersonalizationResult> ExtractPersonalizationAsync(
        AiClassificationRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProspectCandidate>> StructureFromSourceTextAsync(
        AiSourceStructureRequest request,
        CancellationToken cancellationToken);
}

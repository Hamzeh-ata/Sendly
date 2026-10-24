using Sendly.Application.Abstractions;

namespace Sendly.Application.Services;

public class RuleBasedAiResearchService : IAiResearchService
{
    public bool IsAvailable => true;

    public Task<AiQualificationResult> QualifyAsync(
        AiClassificationRequest request,
        CancellationToken cancellationToken)
    {
        var score = 40;
        var matchedKeywords = 0;
        var companyText = BuildCompanyText(request);

        foreach (var keyword in request.Keywords)
        {
            if (ContainsIgnoreCase(companyText, keyword))
            {
                matchedKeywords++;
                score += 8;
            }
        }

        foreach (var excluded in request.ExcludedKeywords)
        {
            if (ContainsIgnoreCase(companyText, excluded))
            {
                score -= 25;
            }
        }

        if (MatchesCountry(request))
        {
            score += 15;
        }
        else if (request.Countries.Count > 0 && !string.IsNullOrWhiteSpace(request.Country))
        {
            score -= 20;
        }

        if (MatchesIndustry(request))
        {
            score += 10;
        }

        foreach (var excludedIndustry in request.ExcludedIndustries)
        {
            if (ContainsIgnoreCase(request.Industry, excludedIndustry)
                || ContainsIgnoreCase(companyText, excludedIndustry))
            {
                score -= 30;
            }
        }

        if (MatchesRole(request))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(request.SourceUrl) || !string.IsNullOrWhiteSpace(request.Website))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(request.CompanyDescription))
        {
            score += 5;
        }

        score = Math.Clamp(score, 0, 100);
        var isRelevant = matchedKeywords > 0 || MatchesIndustry(request) || request.Keywords.Count == 0;
        var isQualified = isRelevant && score >= 70;
        var needsReview = isRelevant && !isQualified && score >= 40;

        var summary = BuildSummary(request, isQualified, isRelevant, score);

        return Task.FromResult(new AiQualificationResult
        {
            IsRelevant = isRelevant,
            IsQualified = isQualified,
            NeedsReview = needsReview,
            QualificationScore = score,
            EmailConfidenceScore = string.IsNullOrWhiteSpace(request.Email) ? null : EstimateEmailConfidence(request),
            WhatsAppConfidenceScore = string.IsNullOrWhiteSpace(request.WhatsApp) ? null : 60,
            Industry = request.Industry,
            CompanySize = request.CompanySize,
            ContactRole = request.ContactRole,
            Summary = summary
        });
    }

    public Task<AiTemplateRecommendationResult> RecommendTemplateAsync(
        AiTemplateRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Templates.Count == 0)
        {
            return Task.FromResult(new AiTemplateRecommendationResult
            {
                Confidence = 0,
                Reason = "No project templates are configured."
            });
        }

        AiTemplateOption? best = null;
        var bestScore = -1;
        foreach (var template in request.Templates)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(template.MatchIndustry)
                && ContainsIgnoreCase(request.Industry, template.MatchIndustry))
            {
                score += 50;
            }

            if (!string.IsNullOrWhiteSpace(template.MatchCompanySize)
                && ContainsIgnoreCase(request.CompanySize, template.MatchCompanySize))
            {
                score += 30;
            }

            if (!string.IsNullOrWhiteSpace(template.Category)
                && (ContainsIgnoreCase(request.Industry, template.Category)
                    || ContainsIgnoreCase(template.Name, request.Industry)))
            {
                score += 20;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = template;
            }
        }

        best ??= request.Templates[0];
        var confidence = bestScore <= 0 ? 40 : Math.Clamp(bestScore, 40, 95);
        var reason = bestScore <= 0
            ? $"Defaulted to template '{best.Name}'."
            : $"Matched template '{best.Name}' using industry and company size signals.";

        return Task.FromResult(new AiTemplateRecommendationResult
        {
            TemplateId = best.TemplateId,
            Reason = reason,
            Confidence = confidence
        });
    }

    public Task<AiPersonalizationResult> ExtractPersonalizationAsync(
        AiClassificationRequest request,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(values, "CompanyName", request.CompanyName);
        AddIfPresent(values, "ContactName", request.ContactName);
        AddIfPresent(values, "Industry", request.Industry);
        AddIfPresent(values, "City", request.City);
        AddIfPresent(values, "Country", request.Country);
        AddIfPresent(values, "ContactRole", request.ContactRole);
        AddIfPresent(values, "Email", request.Email);
        return Task.FromResult(new AiPersonalizationResult { Values = values });
    }

    public Task<IReadOnlyList<ProspectCandidate>> StructureFromSourceTextAsync(
        AiSourceStructureRequest request,
        CancellationToken cancellationToken)
    {
        var companyName = NullIfBlank(request.HintCompanyName);
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return Task.FromResult<IReadOnlyList<ProspectCandidate>>([]);
        }

        var primaryEmail = request.ExtractedEmails.Count > 0 ? request.ExtractedEmails[0] : null;
        var primaryPhone = request.ExtractedPhones.Count > 0 ? request.ExtractedPhones[0] : null;
        var notes = primaryEmail is null && primaryPhone is null
            ? "Structured from crawled page content. No email or phone was found on this page."
            : "Structured from crawled page content. Contact details were extracted from the source page only.";
        var candidate = new ProspectCandidate
        {
            CompanyName = companyName,
            Website = request.SourceUrl,
            Country = NullIfBlank(request.HintCountry),
            Industry = NullIfBlank(request.HintIndustry),
            CompanyDescription = NullIfBlank(Truncate(request.PageText, 400)),
            Email = primaryEmail,
            AdditionalEmails = request.ExtractedEmails.Skip(1).ToList(),
            Phone = primaryPhone,
            AdditionalPhones = request.ExtractedPhones.Skip(1).ToList(),
            WhatsApp = primaryPhone,
            SourceUrl = request.SourceUrl,
            SourceType = Domain.Enums.ProspectSourceType.Website,
            SourceName = NullIfBlank(request.SourceName) ?? "Website crawler",
            ResearchNotes = notes
        };

        return Task.FromResult<IReadOnlyList<ProspectCandidate>>(
            ProspectCandidateDeduplicator.DeduplicateAndMerge([candidate]));
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildCompanyText(AiClassificationRequest request)
    {
        return string.Join(
            ' ',
            new[]
            {
                request.CompanyName,
                request.Industry,
                request.CompanyDescription,
                request.ResearchNotes,
                request.Website
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static bool MatchesCountry(AiClassificationRequest request)
    {
        if (request.Countries.Count == 0)
        {
            return true;
        }

        var host = Domain.Services.UrlCanonicalizer.ExtractDomain(request.Website)
                   ?? Domain.Services.UrlCanonicalizer.ExtractDomain(request.SourceUrl);
        if (!Domain.Services.CountryScopeHelper.HostMatchesCountries(host, request.Countries))
        {
            return false;
        }

        var inferred = Domain.Services.CountryScopeHelper.InferCountryFromHost(host);
        if (inferred is not null)
        {
            return Domain.Services.CountryScopeHelper.CountryMatches(inferred, request.Countries);
        }

        if (!string.IsNullOrWhiteSpace(request.Country)
            && Domain.Services.CountryScopeHelper.CountryMatches(request.Country, request.Countries))
        {
            return true;
        }

        return Domain.Services.CountryScopeHelper.TextMentionsCountry(
            $"{request.CompanyName} {request.CompanyDescription} {request.City} {request.Country}",
            request.Countries);
    }

    private static bool MatchesIndustry(AiClassificationRequest request)
    {
        if (request.Industries.Count == 0)
        {
            return false;
        }

        foreach (var industry in request.Industries)
        {
            if (ContainsIgnoreCase(request.Industry, industry)
                || ContainsIgnoreCase(request.CompanyDescription, industry)
                || ContainsIgnoreCase(request.CompanyName, industry))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesRole(AiClassificationRequest request)
    {
        if (request.TargetRoles.Count == 0 || string.IsNullOrWhiteSpace(request.ContactRole))
        {
            return false;
        }

        foreach (var role in request.TargetRoles)
        {
            if (ContainsIgnoreCase(request.ContactRole, role))
            {
                return true;
            }
        }

        return false;
    }

    private static int EstimateEmailConfidence(AiClassificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return 0;
        }

        var score = 55;
        if (!string.IsNullOrWhiteSpace(request.Website)
            && request.Email.Contains('@')
            && request.Website.Contains('.', StringComparison.Ordinal))
        {
            var host = request.Email.Split('@').LastOrDefault() ?? string.Empty;
            if (ContainsIgnoreCase(request.Website, host))
            {
                score = 85;
            }
        }

        return score;
    }

    private static string BuildSummary(
        AiClassificationRequest request,
        bool isQualified,
        bool isRelevant,
        int score)
    {
        if (!isRelevant)
        {
            return "Rejected because the company does not match the project keywords or industries.";
        }

        if (isQualified)
        {
            var countryPart = string.IsNullOrWhiteSpace(request.Country)
                ? "the target market"
                : request.Country;
            var industryPart = string.IsNullOrWhiteSpace(request.Industry)
                ? "the target industry"
                : request.Industry;
            return $"Matched because the company appears relevant to {industryPart} and operates in {countryPart} (score {score}).";
        }

        return $"Needs review because relevance signals were partial (score {score}).";
    }

    private static bool ContainsIgnoreCase(string? haystack, string? needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIfPresent(Dictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value.Trim();
        }
    }
}

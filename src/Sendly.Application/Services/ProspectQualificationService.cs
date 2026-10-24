using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class QualificationEvaluationRequest
{
    public Guid ProjectId { get; set; }

    public Guid ResearchRunId { get; set; }

    public ProspectCandidate Candidate { get; set; } = new();

    public ResearchQueryVersion? QueryVersion { get; set; }

    public bool RequireEmail { get; set; } = true;

    public int MinQualificationScore { get; set; } = 70;

    public List<string> Keywords { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public List<string> Countries { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<ResearchEvidence> EvidenceItems { get; set; } = [];
}

public class QualificationEvaluationResult
{
    public bool IsQualified { get; set; }

    public bool IsEmailReady { get; set; }

    public bool IsContactable { get; set; }

    public bool IsIdentityVerified { get; set; }

    public int Score { get; set; }

    public string Summary { get; set; } = string.Empty;

    public List<QualificationCriterionResult> Criteria { get; set; } = [];

    public List<string> MissingCriteria { get; set; } = [];

    public List<string> MatchingCriteria { get; set; } = [];
}

public class ProspectQualificationService
{
    public QualificationEvaluationResult Evaluate(QualificationEvaluationRequest request)
    {
        var criteria = new List<QualificationCriterionResult>();
        var candidate = request.Candidate;
        var version = request.QueryVersion;

        AddWebsiteCriterion(criteria, request, candidate, version);
        AddEmailCriterion(criteria, request, candidate, version);
        AddEntityTypeCriterion(criteria, request, candidate);
        AddIdentityCriterion(criteria, request, candidate);
        AddCountryCriterion(criteria, request, candidate);
        AddIndustryCriterion(criteria, request, candidate);
        AddVendorOperatorCriterion(criteria, request, candidate);
        AddContactAttributionCriterion(criteria, request, candidate);
        AddExclusionCriterion(criteria, request, candidate);
        AddKeywordCriterion(criteria, request, candidate);
        AddProjectHardConstraintCriterion(criteria, request, candidate);

        var mandatoryFailed = criteria.Any(x => x.IsMandatory && !x.Passed);
        var countryIncomplete = criteria.Any(x =>
            x.CriterionKey == "targetCountry" && !x.Passed);
        var score = ComputeScore(criteria);
        if (mandatoryFailed || countryIncomplete)
        {
            score = Math.Min(score, request.MinQualificationScore - 1);
        }

        var matching = criteria.Where(x => x.Passed).Select(x => x.CriterionKey).ToList();
        var missing = criteria.Where(x => !x.Passed).Select(x => x.CriterionKey).ToList();

        var hasEmail = criteria.Any(x => x.CriterionKey == "publicBusinessEmail" && x.Passed);
        var attributionOk = !criteria.Any(x =>
            x.CriterionKey == "contactAttribution" && x.IsMandatory && !x.Passed);
        var identityOk = criteria.Any(x => x.CriterionKey == "companyIdentity" && x.Passed);
        var requireEmail = version?.RequirePublicBusinessEmail ?? request.RequireEmail;
        var hardConstraintsOk = !criteria.Any(x =>
            (x.CriterionKey is "targetIndustry" or "targetCountry" or "exclusionCriteria" or "vendorNotOperator" or "projectHardConstraints")
            && x.IsMandatory
            && !x.Passed);
        var isQualified = !mandatoryFailed && !countryIncomplete && score >= request.MinQualificationScore;
        if (requireEmail && !hasEmail)
        {
            isQualified = false;
        }

        if (!hardConstraintsOk)
        {
            isQualified = false;
        }

        var summary = BuildSummary(isQualified, score, matching, missing);

        return new QualificationEvaluationResult
        {
            IsQualified = isQualified,
            IsEmailReady = isQualified && hasEmail && attributionOk,
            IsContactable = hasEmail && attributionOk,
            IsIdentityVerified = identityOk,
            Score = score,
            Summary = summary,
            Criteria = criteria,
            MatchingCriteria = matching,
            MissingCriteria = missing
        };
    }

    private static void AddWebsiteCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate,
        ResearchQueryVersion? version)
    {
        var required = version?.RequireOfficialWebsite ?? true;
        var normalizedWebsite = ProspectNormalizer.NormalizeWebsite(candidate.Website);
        var websiteHost = UrlCanonicalizer.ExtractDomain(normalizedWebsite)
                          ?? UrlCanonicalizer.ExtractDomain(candidate.Website);
        var websiteIsDirectoryHost = SystemDirectoryCatalog.IsSeedOrDirectoryHost(websiteHost);
        var hasWebsite = !string.IsNullOrWhiteSpace(normalizedWebsite) && !websiteIsDirectoryHost;
        var directoryDiscoveryPendingWebsite = candidate.SourceType == ProspectSourceType.Directory
                                               && candidate.NameConfidence >= 55
                                               && !hasWebsite;
        criteria.Add(CreateCriterion(
            request,
            "officialWebsite",
            "Official website",
            required && !directoryDiscoveryPendingWebsite,
            hasWebsite,
            hasWebsite ? 20 : 0,
            hasWebsite
                ? "Official website URL was found."
                : directoryDiscoveryPendingWebsite
                    ? "Official website not yet verified; directory organization retained for continued investigation."
                    : websiteIsDirectoryHost
                        ? "Website resolves to a directory, encyclopedia, or aggregator host rather than an official business domain."
                        : "Official website URL is missing.",
            FindEvidenceIds(request.EvidenceItems, "canonicalUrl", "pageTitle")));
    }

    private static void AddEmailCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate,
        ResearchQueryVersion? version)
    {
        var gatesOutreach = version?.RequirePublicBusinessEmail ?? request.RequireEmail;
        var hasEmail = EmailAddressParser.IsAcceptableBusinessEmail(candidate.Email)
                       || candidate.AdditionalEmails.Any(x => EmailAddressParser.IsAcceptableBusinessEmail(x))
                       || candidate.AttributedEmails.Any(x => EmailAddressParser.IsAcceptableBusinessEmail(x.Value));
        criteria.Add(CreateCriterion(
            request,
            "publicBusinessEmail",
            "Public business email",
            mandatory: false,
            hasEmail,
            hasEmail ? 25 : 0,
            hasEmail
                ? "Public business email was extracted from crawled pages."
                : gatesOutreach
                    ? "No public business email was found (gates outreach readiness, not target relevance)."
                    : "No public business email was found.",
            FindEvidenceIds(request.EvidenceItems, "email")));
    }

    private static void AddCountryCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        if (request.Countries.Count == 0
            && string.IsNullOrWhiteSpace(request.QueryVersion?.TargetCountry))
        {
            return;
        }

        var targets = new List<string>(request.Countries);
        if (!string.IsNullOrWhiteSpace(request.QueryVersion?.TargetCountry))
        {
            targets.Add(request.QueryVersion.TargetCountry);
        }

        var gate = OrganizationCountryGate.Evaluate(candidate, targets);
        candidate.CountryMatchReasonCode = gate.ReasonCode;
        if (!string.IsNullOrWhiteSpace(gate.InferredCountry)
            && (string.IsNullOrWhiteSpace(candidate.Country)
                || gate.State == OrganizationCountryMatchState.Mismatch))
        {
            candidate.Country = gate.InferredCountry;
        }

        if (gate.Evidence.Count > 0 && string.IsNullOrWhiteSpace(candidate.CountryEvidenceSummary))
        {
            candidate.CountryEvidenceSummary = string.Join("; ", gate.Evidence.Take(5));
            candidate.CountryEvidenceSource ??= "organization_country_gate";
        }

        var passed = gate.Passed;
        var mandatory = gate.State != OrganizationCountryMatchState.NeedsReview;
        var reason = gate.Passed
            ? $"{gate.Explanation} Evidence: {string.Join("; ", gate.Evidence.Take(3))}."
            : gate.State == OrganizationCountryMatchState.NeedsReview
                ? $"{gate.Explanation} ({gate.ReasonCode}). Marked NeedsReview rather than qualified."
                : $"{gate.Explanation} ({gate.ReasonCode}).";

        criteria.Add(CreateCriterion(
            request,
            "targetCountry",
            "Target country",
            mandatory,
            passed,
            passed ? 20 : 0,
            reason,
            []));
    }

    private static void AddEntityTypeCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        var entityType = candidate.EntityType;
        var reasonCode = candidate.EntityTypeReasonCode;
        if (entityType == ResearchEntityType.Unknown && candidate.EntityTypeConfidence == 0)
        {
            var classified = EntityClassifier.Classify(
                candidate.CompanyName,
                candidate.CompanyName,
                candidate.CompanyDescription,
                candidate.Website ?? candidate.SourceUrl);
            entityType = classified.EntityType;
            reasonCode = classified.ReasonCode;
            candidate.EntityType = classified.EntityType;
            candidate.EntityTypeConfidence = classified.Confidence;
            candidate.EntityTypeReasonCode = classified.ReasonCode;
        }

        var passed = EntityClassifier.IsAllowedTarget(entityType, ResearchEntityType.TargetBusiness);
        criteria.Add(CreateCriterion(
            request,
            "entityType",
            "Entity type",
            true,
            passed,
            passed ? 20 : 0,
            passed
                ? $"Entity type '{entityType}' is an allowed target."
                : $"Entity type '{entityType}' is not an allowed target business ({reasonCode ?? "entity.unknown"}).",
            []));
    }

    private static void AddIdentityCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        var normalizedWebsite = ProspectNormalizer.NormalizeWebsite(candidate.Website);
        var websiteHost = UrlCanonicalizer.ExtractDomain(normalizedWebsite)
                          ?? UrlCanonicalizer.ExtractDomain(candidate.Website);
        var websiteIsDirectoryHost = SystemDirectoryCatalog.IsSeedOrDirectoryHost(websiteHost);
        var hasWebsite = !string.IsNullOrWhiteSpace(normalizedWebsite) && !websiteIsDirectoryHost;

        var nameConfidence = candidate.NameConfidence;
        var nameReason = candidate.NameReasonCode;
        if (nameConfidence == 0 && string.IsNullOrWhiteSpace(nameReason))
        {
            if (CompanyIdentityResolver.IsGenericName(candidate.CompanyName))
            {
                nameConfidence = 10;
                nameReason = "identity.generic_or_missing";
            }
            else if (hasWebsite)
            {
                nameConfidence = 70;
                nameReason = "identity.named_with_website";
            }
            else
            {
                nameConfidence = 45;
                nameReason = "identity.generic_or_missing";
            }

            candidate.NameConfidence = nameConfidence;
            candidate.NameReasonCode = nameReason;
        }

        var directoryDiscoveryPendingWebsite = candidate.SourceType == ProspectSourceType.Directory
                                               && nameConfidence >= 55
                                               && !CompanyIdentityResolver.IsGenericName(candidate.CompanyName)
                                               && !hasWebsite;
        var passed = !CompanyIdentityResolver.IsGenericName(candidate.CompanyName)
                     && nameConfidence >= 55
                     && (hasWebsite || directoryDiscoveryPendingWebsite);
        criteria.Add(CreateCriterion(
            request,
            "companyIdentity",
            "Company identity",
            true,
            passed,
            passed ? 15 : 0,
            passed
                ? directoryDiscoveryPendingWebsite
                    ? $"Directory organization identity accepted pending official website ({nameReason ?? "identity.listing"})."
                    : $"Company name accepted ({nameReason ?? "identity.ok"})."
                : websiteIsDirectoryHost
                    ? "Company identity rejected because evidence came from a directory or encyclopedia host."
                    : $"Company identity is weak or generic ({nameReason ?? "identity.missing"}).",
            FindEvidenceIds(request.EvidenceItems, "pageTitle", "canonicalUrl")));
    }

    private static void AddContactAttributionCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Email) && candidate.AdditionalEmails.Count == 0)
        {
            return;
        }

        EnsureContactAttribution(candidate);

        var rejected = candidate.ContactAttributionState == ContactAttributionState.Rejected;
        var directoryOnly = candidate.ContactAttributionState == ContactAttributionState.DirectoryListing;
        var crossDomain = candidate.ContactAttributionState == ContactAttributionState.CrossDomainEvidence;
        var unverifiedWeak = candidate.ContactAttributionState == ContactAttributionState.Unverified
                             && candidate.ContactAttributionConfidence < 50;
        var passed = !rejected && !directoryOnly && !crossDomain && !unverifiedWeak;
        criteria.Add(CreateCriterion(
            request,
            "contactAttribution",
            "Contact attribution",
            true,
            passed,
            passed ? 10 : 0,
            passed
                ? $"Contact attribution accepted ({candidate.ContactAttributionReasonCode ?? "contact.ok"})."
                : $"Contact attribution failed ({candidate.ContactAttributionReasonCode ?? "contact.rejected"}).",
            FindEvidenceIds(request.EvidenceItems, "email")));
    }

    private static void AddVendorOperatorCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        var evidence = $"{candidate.CompanyName} {candidate.CompanyDescription} {candidate.ResearchNotes}";
        var isVendor = ProjectEligibilityPolicy.LooksLikeVendorNotOperator(evidence, request.Industries);
        criteria.Add(CreateCriterion(
            request,
            "vendorNotOperator",
            "Operating business (not vendor)",
            true,
            !isVendor,
            isVendor ? 0 : 10,
            isVendor
                ? "Rejected as a vendor/software seller for the industry rather than an operating target business."
                : "No vendor-vs-operator conflict detected.",
            FindEvidenceIds(request.EvidenceItems, "metaDescription", "pageTitle")));
    }

    private static void AddProjectHardConstraintCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        var spec = new ResearchSpecification
        {
            ApprovedIndustries = request.Industries.ToList(),
            ExcludedIndustries = request.ExcludedIndustries.ToList(),
            ExcludedKeywords = request.ExcludedKeywords.ToList(),
            TargetCountries = request.Countries.ToList(),
            RequireOfficialWebsite = request.QueryVersion?.RequireOfficialWebsite ?? true,
            RequirePublicBusinessEmail = request.QueryVersion?.RequirePublicBusinessEmail ?? request.RequireEmail
        };
        var decision = ProjectEligibilityPolicy.EvaluateCandidateEvidence(
            candidate.CompanyName,
            candidate.CompanyDescription,
            candidate.Website ?? candidate.SourceUrl,
            candidate.ResearchNotes,
            candidate.CountryEvidenceSummary ?? candidate.Country,
            spec);
        var passed = decision.IsEligible
                     || decision.Disposition == ResearchCandidateDisposition.NeedsVerification
                        && decision.ReasonCode == "eligibility.country_unverified";
        if (decision.Disposition is ResearchCandidateDisposition.RejectedWrongIndustry
            or ResearchCandidateDisposition.RejectedExcludedCategory
            or ResearchCandidateDisposition.RejectedVendorNotOperator)
        {
            passed = false;
        }

        if (decision.ReasonCode == "eligibility.country_unverified")
        {
            passed = false;
        }

        criteria.Add(CreateCriterion(
            request,
            "projectHardConstraints",
            "Project hard constraints",
            true,
            passed,
            passed ? 15 : 0,
            decision.Explanation,
            FindEvidenceIds(request.EvidenceItems, "metaDescription", "pageTitle", "canonicalUrl")));
    }

    private static void AddIndustryCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        var industries = request.Industries
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (industries.Count == 0)
        {
            return;
        }

        var classification = IndustryEvidenceClassifier.Classify(
            candidate.CompanyName,
            candidate.CompanyName,
            candidate.CompanyDescription,
            candidate.ResearchNotes,
            industries,
            request.ExcludedIndustries);
        if (classification.MatchedApprovedIndustry
            && string.IsNullOrWhiteSpace(candidate.Industry))
        {
            candidate.Industry = classification.AssignedIndustry;
        }

        var passed = classification.MatchedApprovedIndustry && !classification.IsExcluded;
        criteria.Add(CreateCriterion(
            request,
            "targetIndustry",
            "Target industry",
            true,
            passed,
            passed ? 20 : 0,
            passed
                ? classification.Explanation
                : classification.Explanation,
            FindEvidenceIds(request.EvidenceItems, "metaDescription", "pageTitle")));
    }

    private static void AddExclusionCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        var exclusions = new List<string>(request.ExcludedKeywords);
        exclusions.AddRange(request.ExcludedIndustries);
        if (request.QueryVersion?.ExclusionCriteriaJson is not null)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(request.QueryVersion.ExclusionCriteriaJson);
                if (parsed is not null)
                {
                    exclusions.AddRange(parsed);
                }
            }
            catch (JsonException)
            {
            }
        }

        var classified = ExclusionEvidenceClassifier.Classify(
            candidate.CompanyName,
            candidate.CompanyName,
            candidate.CompanyDescription,
            candidate.Website ?? candidate.SourceUrl,
            request.ExcludedIndustries,
            exclusions,
            request.Industries);
        var passed = !classified.IsExcluded;
        if (exclusions.Count == 0 && passed)
        {
            return;
        }

        criteria.Add(CreateCriterion(
            request,
            "exclusionCriteria",
            "Exclusion criteria",
            true,
            passed,
            passed ? 10 : 0,
            passed ? "No exclusion criteria matched." : classified.Explanation,
            []));
    }

    private static void AddKeywordCriterion(
        List<QualificationCriterionResult> criteria,
        QualificationEvaluationRequest request,
        ProspectCandidate candidate)
    {
        if (request.Keywords.Count == 0)
        {
            return;
        }

        var haystack = $"{candidate.Industry} {candidate.CompanyDescription} {candidate.CompanyName} {candidate.ResearchNotes}";
        var matched = request.Keywords
            .Where(k => !string.IsNullOrWhiteSpace(k) && haystack.Contains(k, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matched.Count == 0)
        {
            var synonymHit = IndustrySynonymCatalog.MatchIndustry(haystack, request.Keywords);
            if (!string.IsNullOrWhiteSpace(synonymHit))
            {
                matched.Add(synonymHit);
            }
        }

        var passed = matched.Count > 0;
        criteria.Add(CreateCriterion(
            request,
            "keywords",
            "Keywords",
            false,
            passed,
            passed ? Math.Min(15, matched.Count * 5) : 0,
            passed ? $"Matched keywords: {string.Join(", ", matched)}" : "No research keywords matched.",
            []));
    }

    private static void EnsureContactAttribution(ProspectCandidate candidate)
    {
        if (candidate.ContactAttributionState != ContactAttributionState.Unverified
            && candidate.ContactAttributionConfidence > 0)
        {
            return;
        }

        var emails = new List<string>();
        if (!string.IsNullOrWhiteSpace(candidate.Email))
        {
            emails.Add(candidate.Email);
        }

        emails.AddRange(candidate.AdditionalEmails.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (emails.Count == 0)
        {
            return;
        }

        var websiteHost = UrlCanonicalizer.ExtractDomain(candidate.Website);
        var pageIsDirectory = SystemDirectoryCatalog.IsSeedOrDirectoryHost(websiteHost)
                              || SystemDirectoryCatalog.IsSeedOrDirectoryHost(candidate.SourceUrl);
        var attributed = ContactAttributionHelper.AttributeBest(
            emails,
            candidate.ContactEvidenceUrl ?? candidate.SourceUrl ?? candidate.Website,
            candidate.Website,
            pageIsDirectory);
        candidate.ContactAttributionState = attributed.State;
        candidate.ContactAttributionConfidence = attributed.Confidence;
        candidate.ContactAttributionReasonCode = attributed.ReasonCode;
        candidate.ContactEvidenceUrl ??= attributed.EvidenceUrl;
        candidate.ContactPageDomain ??= attributed.PageDomain;
    }

    private static QualificationCriterionResult CreateCriterion(
        QualificationEvaluationRequest request,
        string key,
        string label,
        bool mandatory,
        bool passed,
        int score,
        string reason,
        List<Guid> evidenceIds)
    {
        return new QualificationCriterionResult
        {
            ProjectId = request.ProjectId,
            ResearchRunId = request.ResearchRunId,
            CriterionKey = key,
            CriterionLabel = label,
            IsMandatory = mandatory,
            Passed = passed,
            ScoreContribution = score,
            Reason = reason,
            EvidenceIdsJson = evidenceIds.Count == 0
                ? null
                : JsonSerializer.Serialize(evidenceIds),
            EvaluatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static List<Guid> FindEvidenceIds(List<ResearchEvidence> evidence, params string[] factTypes)
    {
        var ids = new List<Guid>();
        foreach (var item in evidence)
        {
            foreach (var type in factTypes)
            {
                if (string.Equals(item.FactType, type, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(item.Id);
                    break;
                }
            }
        }

        return ids;
    }

    private static int ComputeScore(List<QualificationCriterionResult> criteria)
    {
        if (criteria.Count == 0)
        {
            return 0;
        }

        var earned = criteria.Sum(x => x.ScoreContribution);
        var max = criteria.Sum(x => Math.Max(x.ScoreContribution, x.IsMandatory ? 20 : 10));
        if (max <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Round(earned * 100.0 / max), 0, 100);
    }

    private static string BuildSummary(
        bool isQualified,
        int score,
        List<string> matching,
        List<string> missing)
    {
        if (isQualified)
        {
            return $"Qualified with score {score}. Matched: {string.Join(", ", matching)}.";
        }

        if (missing.Count == 0)
        {
            return $"Not qualified. Score {score} was below the minimum threshold.";
        }

        return $"Not qualified. Score {score}. Missing: {string.Join(", ", missing)}.";
    }
}

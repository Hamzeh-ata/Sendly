using Sendly.Domain.Services;

namespace Sendly.Application.Services;

public class CompanyIdentityEvidence
{
    public string? StructuredOrganizationName { get; set; }

    public string? OpenGraphSiteName { get; set; }

    public string? OpenGraphTitle { get; set; }

    public string? PriorOrganizationName { get; set; }

    public string? PageHeading { get; set; }

    public UrlPagePurpose PagePurpose { get; set; } = UrlPagePurpose.Unknown;

    public bool HasSameDomainEmail { get; set; }

    public int CorroboratingFirstPartyPages { get; set; }
}

public class CompanyIdentityResult
{
    public string? CompanyName { get; set; }

    public int NameConfidence { get; set; }

    public string? OfficialDomain { get; set; }

    public string ReasonCode { get; set; } = "identity.missing";

    public string Explanation { get; set; } = "Company identity could not be resolved.";

    public bool IsAcceptable => !string.IsNullOrWhiteSpace(CompanyName) && NameConfidence >= 55;

    public List<string> EvidenceCodes { get; set; } = [];
}

public static class CompanyIdentityResolver
{
    private static readonly HashSet<string> GenericTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "homepage", "index", "welcome", "about", "about us", "contact", "contact us",
        "services", "products", "blog", "news", "login", "sign in", "privacy", "terms",
        "schools", "school", "directory", "search", "faq", "support", "careers", "jobs",
        "find a school", "employment opportunities", "all schools", "types of school",
        "vacancies", "opportunities", "recruitment", "locations", "location",
        "online admission form", "admission form", "registration form", "application form",
        "enquiry form", "inquiry form", "get in touch", "reach us", "write to us",
        "cookie policy", "privacy policy", "terms of use", "terms and conditions",
        "about the schools", "about the school", "our schools", "our school",
        "let's connect", "lets connect", "let’s connect and", "connect with us",
        "team", "career", "careers", "district", "inspire", "mission", "vision",
        "membership", "board of directors", "staff", "committees", "events", "store",
        "partners", "charities", "company search", "renew membership", "digital badging",
        "my account", "search for:", "search for", "industry research", "ways to give",
        "book a visit", "book a visit →", "book a visit ->", "schedule a visit",
        "by local moms for local moms", "executive team", "leadership team",
        "get direction", "get directions", "preschools", "join nais", "jobs at nais",
        "legal and privacy information", "academic calendar", "api and bulk pricing",
        "pricing", "entity verification", "team partners careers", "programs",
        "advanced search", "academia", "government", "purpose",
        "important information and who we are", "resources",
        "work with us", "meet the team", "legal news and tips",
        "الصفحة الرئيسية", "الرئيسية", "اتصل بنا", "تواصل معنا", "من نحن", "عن الشركة",
        "ابحث عن مدرسة", "دليل المدارس", "المدارس", "الوظائف", "فرص العمل",
        "نموذج القبول", "نموذج التسجيل", "الوظائف الإدارية", "الأرقام التي نفخر بها",
        "أرقام نفخر بها", "مرحبا بكم", "مرحبًا بكم", "accueil", "islam", "facebook", "instagram",
        "linkedin", "twitter", "youtube", "tiktok", "wikipedia", "google", "show posts by this member only"
    };

    private static readonly string[] PageChromeSuffixes =
    [
        "contact us", "contact", "about us", "about", "home", "homepage",
        "get in touch", "locations", "location", "team", "careers", "jobs",
        "admissions", "admission", "enquire", "inquiry", "enquiry",
        "privacy", "terms", "login", "sign in",
        "اتصل بنا", "تواصل معنا", "من نحن", "الصفحة الرئيسية", "الرئيسية"
    ];

    private static readonly string[] FormTitleHints =
    [
        "admission form", "registration form", "application form", "enquiry form",
        "inquiry form", "online form", "submit form", "booking form"
    ];

    public static bool IsGenericName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var trimmed = name.Trim();
        if (trimmed.Length < 2)
        {
            return true;
        }

        if (GenericTitles.Contains(trimmed))
        {
            return true;
        }

        if (LooksLikeFormTitle(trimmed)
            || LooksLikeSectionOrMarketingHeading(trimmed)
            || trimmed.StartsWith("a guide to", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("list of", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("best ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("top ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("the best", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("the 10", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("the 15", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("find a ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("how is ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("study in ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("welcome to", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("مرحبًا بكم", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("مرحبا بكم", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("أفضل ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(" near me", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(" — compare", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("compare by", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(" employment", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("opportunities", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(" staff jobs", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(" jobs in ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(" schools in ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(" companies in ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(" businesses in ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("locations", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("available in new zealand", StringComparison.OrdinalIgnoreCase)
            || LooksLikeUiChrome(trimmed)
            || ProspectJunkClassifier.LooksLikeLegalHeading(trimmed)
            || ProspectJunkClassifier.LooksLikeMarketplaceCopy(trimmed)
            || ProspectJunkClassifier.LooksLikeNavigationLabel(trimmed)
            || ProspectJunkClassifier.LooksLikeSentence(trimmed)
            || EntityClassifier.IsListicleName(trimmed)
            || EntityClassifier.IsTopicArticleName(trimmed))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeSectionOrMarketingHeading(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("connect and", StringComparison.Ordinal)
            || lower.Contains("let's connect", StringComparison.Ordinal)
            || lower.Contains("lets connect", StringComparison.Ordinal)
            || lower.Contains("about the school", StringComparison.Ordinal)
            || lower.Contains("numbers we", StringComparison.Ordinal)
            || lower.Contains("we are proud", StringComparison.Ordinal)
            || lower.Contains("نفخر", StringComparison.Ordinal)
            || lower.Contains("الأرقام", StringComparison.Ordinal)
            || lower.Contains("الوظائف", StringComparison.Ordinal)
            || lower.StartsWith("admin ", StringComparison.Ordinal)
            || lower.Contains("administrative", StringComparison.Ordinal)
            || lower.Contains("apply now", StringComparison.Ordinal)
            || lower.Contains("join our team", StringComparison.Ordinal)
            || lower.StartsWith("book a ", StringComparison.Ordinal)
            || lower.StartsWith("schedule a ", StringComparison.Ordinal)
            || lower.Contains(" →", StringComparison.Ordinal)
            || lower.Contains("->", StringComparison.Ordinal)
            || lower.StartsWith("by local ", StringComparison.Ordinal)
            || lower.Contains(" for local ", StringComparison.Ordinal)
            || lower.Equals("team", StringComparison.Ordinal)
            || lower.Equals("career", StringComparison.Ordinal)
            || lower.Equals("careers", StringComparison.Ordinal)
            || lower.Equals("district", StringComparison.Ordinal)
            || lower.Contains("history careers contact", StringComparison.Ordinal)
            || lower.StartsWith("about issa", StringComparison.Ordinal)
            || lower.StartsWith("about the issa", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeFormTitle(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var hint in FormTitleHints)
        {
            if (lower.Equals(hint, StringComparison.Ordinal)
                || lower.EndsWith(" " + hint, StringComparison.Ordinal)
                || lower.StartsWith(hint + " ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return lower.Contains(" form", StringComparison.Ordinal)
               && (lower.Contains("admission", StringComparison.Ordinal)
                   || lower.Contains("registration", StringComparison.Ordinal)
                   || lower.Contains("application", StringComparison.Ordinal)
                   || lower.Contains("enquiry", StringComparison.Ordinal)
                   || lower.Contains("inquiry", StringComparison.Ordinal));
    }

    private static bool LooksLikeUiChrome(string name)
    {
        if (name.Contains('#')
            || name.Contains("alt=", StringComparison.OrdinalIgnoreCase)
            || name.Contains("websocket", StringComparison.OrdinalIgnoreCase)
            || name.Contains("your connection is not", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("get ", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("join ", StringComparison.OrdinalIgnoreCase)
            || name.Contains("حقل السجل", StringComparison.OrdinalIgnoreCase)
            || name.Contains("نوع المستفيد", StringComparison.OrdinalIgnoreCase)
            || name.Contains("الاستعلام عن", StringComparison.OrdinalIgnoreCase)
            || name.Contains("اتفاقية مستوى", StringComparison.OrdinalIgnoreCase)
            || name.Contains("service level", StringComparison.OrdinalIgnoreCase)
            || name.Contains("register school", StringComparison.OrdinalIgnoreCase)
            || name.Contains("registration request", StringComparison.OrdinalIgnoreCase)
            || name.Contains("sign in", StringComparison.OrdinalIgnoreCase)
            || name.Contains("log in", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = name.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 8 && name.Length >= 40)
        {
            var shortTokens = tokens.Count(t => t.Length <= 3);
            if (shortTokens >= tokens.Length / 2)
            {
                return true;
            }
        }

        return false;
    }

    public static CompanyIdentityResult Resolve(
        string? title,
        string? excerpt,
        string? normalizedDomain,
        string? canonicalUrl,
        string? structuredOrganizationName = null,
        string? openGraphSiteName = null,
        string? openGraphTitle = null)
    {
        return Resolve(
            title,
            excerpt,
            normalizedDomain,
            canonicalUrl,
            new CompanyIdentityEvidence
            {
                StructuredOrganizationName = structuredOrganizationName,
                OpenGraphSiteName = openGraphSiteName,
                OpenGraphTitle = openGraphTitle
            });
    }

    public static CompanyIdentityResult Resolve(
        string? title,
        string? excerpt,
        string? normalizedDomain,
        string? canonicalUrl,
        CompanyIdentityEvidence evidence)
    {
        var domain = UrlCanonicalizer.ExtractDomain(normalizedDomain)
                     ?? UrlCanonicalizer.ExtractDomain(canonicalUrl);
        var evidenceCodes = new List<string>();

        if (SystemDirectoryCatalog.IsSeedOrDirectoryHost(domain)
            || SystemDirectoryCatalog.IsSeedOrDirectoryHost(canonicalUrl))
        {
            return new CompanyIdentityResult
            {
                OfficialDomain = null,
                ReasonCode = "identity.directory_host",
                Explanation = "Page host is a directory, encyclopedia, or aggregator and cannot supply an official business identity.",
                EvidenceCodes = ["identity.directory_host"]
            };
        }

        var structured = CleanCandidateName(evidence.StructuredOrganizationName);
        if (!string.IsNullOrWhiteSpace(structured))
        {
            evidenceCodes.Add("identity.json_ld_organization");
            var confidence = 92;
            if (evidence.CorroboratingFirstPartyPages > 0 || evidence.HasSameDomainEmail)
            {
                confidence = 95;
                evidenceCodes.Add(evidence.HasSameDomainEmail
                    ? "identity.same_domain_email"
                    : "identity.multi_page_corroboration");
            }

            return Accept(
                structured,
                confidence,
                domain,
                "identity.json_ld_organization",
                "Organization name taken from JSON-LD Organization/LocalBusiness structured data.",
                evidenceCodes);
        }

        var prior = CleanCandidateName(evidence.PriorOrganizationName);
        if (!string.IsNullOrWhiteSpace(prior)
            && evidence.PagePurpose is UrlPagePurpose.Contact
                or UrlPagePurpose.About
                or UrlPagePurpose.Team
                or UrlPagePurpose.Location
                or UrlPagePurpose.FranchiseLocation)
        {
            evidenceCodes.Add("identity.prior_first_party");
            if (evidence.HasSameDomainEmail)
            {
                evidenceCodes.Add("identity.same_domain_email");
            }

            return Accept(
                prior,
                evidence.HasSameDomainEmail ? 88 : 82,
                domain,
                "identity.prior_first_party",
                "Organization name reused from prior first-party pages on the same official domain.",
                evidenceCodes);
        }

        var ogSite = CleanCandidateName(evidence.OpenGraphSiteName);
        if (!string.IsNullOrWhiteSpace(ogSite))
        {
            evidenceCodes.Add("identity.open_graph_site_name");
            return Accept(
                ogSite,
                78,
                domain,
                "identity.open_graph_site_name",
                "Organization name taken from Open Graph site_name metadata.",
                evidenceCodes);
        }

        var heading = CleanCandidateName(evidence.PageHeading);
        if (!string.IsNullOrWhiteSpace(heading)
            && CorroboratesBeyondSelf(heading, excerpt, domain, evidence.PageHeading))
        {
            evidenceCodes.Add("identity.page_heading");
            return Accept(
                heading,
                72,
                domain,
                "identity.page_heading",
                "Organization name taken from a meaningful page heading and corroborated by page evidence.",
                evidenceCodes);
        }

        var fromOgTitle = ExtractNameFromTitle(evidence.OpenGraphTitle);
        if (!string.IsNullOrWhiteSpace(fromOgTitle))
        {
            evidenceCodes.Add("identity.open_graph_title");
            return Accept(
                fromOgTitle,
                70,
                domain,
                "identity.open_graph_title",
                "Organization name taken from Open Graph title metadata.",
                evidenceCodes);
        }

        var fromTitle = ExtractNameFromTitle(title);
        if (!string.IsNullOrWhiteSpace(fromTitle))
        {
            var corroborated = Corroborates(fromTitle, excerpt, domain)
                               || evidence.HasSameDomainEmail
                               || evidence.CorroboratingFirstPartyPages > 0;
            if (corroborated)
            {
                evidenceCodes.Add("identity.title_corroborated");
                if (evidence.HasSameDomainEmail)
                {
                    evidenceCodes.Add("identity.same_domain_email");
                }

                return Accept(
                    fromTitle,
                    80,
                    domain,
                    "identity.title_corroborated",
                    "Organization name taken from title and corroborated by page evidence.",
                    evidenceCodes);
            }

            return new CompanyIdentityResult
            {
                CompanyName = fromTitle,
                NameConfidence = 45,
                OfficialDomain = domain,
                ReasonCode = "identity.title_only",
                Explanation = "Organization name taken from title without strong corroboration.",
                EvidenceCodes = ["identity.title_only"]
            };
        }

        if (!string.IsNullOrWhiteSpace(prior))
        {
            evidenceCodes.Add("identity.prior_first_party");
            return Accept(
                prior,
                evidence.HasSameDomainEmail || evidence.CorroboratingFirstPartyPages > 0 ? 80 : 60,
                domain,
                "identity.prior_first_party",
                "Organization name reused from prior first-party evidence on the same domain.",
                evidenceCodes);
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            var fromDomain = NameFromDomain(domain);
            if (!string.IsNullOrWhiteSpace(fromDomain) && !IsGenericName(fromDomain))
            {
                var strongContactEvidence = evidence.HasSameDomainEmail
                                            && evidence.PagePurpose is UrlPagePurpose.Contact
                                                or UrlPagePurpose.About
                                                or UrlPagePurpose.Team
                                            && !string.IsNullOrWhiteSpace(excerpt)
                                            && excerpt.Trim().Length >= 40;
                if (strongContactEvidence)
                {
                    evidenceCodes.Add("identity.domain_email_corroborated");
                    return Accept(
                        fromDomain,
                        62,
                        domain,
                        "identity.domain_email_corroborated",
                        "Organization identity inferred from official domain and corroborated by same-domain contact evidence on a first-party page.",
                        evidenceCodes);
                }

                return new CompanyIdentityResult
                {
                    CompanyName = fromDomain,
                    NameConfidence = 40,
                    OfficialDomain = domain,
                    ReasonCode = "identity.domain_fallback",
                    Explanation =
                        "Organization name inferred from domain only; insufficient for verified identity without corroboration.",
                    EvidenceCodes = ["identity.domain_fallback"]
                };
            }
        }

        return new CompanyIdentityResult
        {
            OfficialDomain = domain,
            ReasonCode = "identity.generic_or_missing",
            Explanation = "Page title was generic or missing and no reliable organization name was found.",
            EvidenceCodes = ["identity.generic_or_missing"]
        };
    }

    private static CompanyIdentityResult Accept(
        string name,
        int confidence,
        string? domain,
        string reasonCode,
        string explanation,
        List<string> evidenceCodes)
    {
        return new CompanyIdentityResult
        {
            CompanyName = name,
            NameConfidence = confidence,
            OfficialDomain = domain,
            ReasonCode = reasonCode,
            Explanation = explanation,
            EvidenceCodes = evidenceCodes
        };
    }

    private static string? CleanCandidateName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = StripPageChrome(value.Trim());
        if (string.IsNullOrWhiteSpace(cleaned) || IsGenericName(cleaned))
        {
            return null;
        }

        return cleaned;
    }

    private static string? ExtractNameFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cleaned = StripPageChrome(title);
        if (string.IsNullOrWhiteSpace(cleaned) || IsGenericName(cleaned))
        {
            return null;
        }

        return cleaned;
    }

    private static string? StripPageChrome(string title)
    {
        var parts = title.Split(['|', '-', '–', '·', ':', '—'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        string? best = null;
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (IsPageChromeLabel(trimmed) || IsGenericName(trimmed) || LooksLikeFormTitle(trimmed))
            {
                continue;
            }

            if (best is null || trimmed.Length > best.Length)
            {
                best = trimmed;
            }
        }

        if (!string.IsNullOrWhiteSpace(best))
        {
            return best;
        }

        var fallback = parts[0].Trim();
        return IsPageChromeLabel(fallback) || LooksLikeFormTitle(fallback) ? null : fallback;
    }

    private static bool IsPageChromeLabel(string value)
    {
        foreach (var suffix in PageChromeSuffixes)
        {
            if (value.Equals(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Corroborates(string name, string? excerpt, string? domain)
        => CorroboratesBeyondSelf(name, excerpt, domain, selfText: null);

    private static bool CorroboratesBeyondSelf(
        string name,
        string? excerpt,
        string? domain,
        string? selfText)
    {
        var excerptText = excerpt ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selfText))
        {
            excerptText = excerptText.Replace(selfText, " ", StringComparison.OrdinalIgnoreCase);
        }

        var haystack = $"{excerptText} {domain}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(haystack.Trim()))
        {
            return false;
        }

        var tokens = name
            .Split([' ', '-', ',', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3)
            .Select(x => x.ToLowerInvariant())
            .Take(3)
            .ToList();

        if (tokens.Count == 0)
        {
            return haystack.Contains(name.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        }

        return tokens.Count(token => haystack.Contains(token, StringComparison.Ordinal)) >= Math.Max(1, tokens.Count / 2);
    }

    private static string? NameFromDomain(string domain)
    {
        var host = domain.Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var first = host.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first) || first.Length < 2)
        {
            return null;
        }

        return char.ToUpperInvariant(first[0]) + first[1..];
    }
}

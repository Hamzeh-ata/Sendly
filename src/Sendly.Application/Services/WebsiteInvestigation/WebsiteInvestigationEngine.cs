using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Services.WebsiteInvestigation;

public static class WebsiteInvestigationEngine
{
    public static PageInvestigationResult InvestigateFetchedPage(FetchedPageInvestigationRequest request)
    {
        var pageUrl = request.PageUrl;
        var domain = request.NormalizedDomain
                     ?? UrlCanonicalizer.ExtractDomain(pageUrl)
                     ?? "unknown";
        var isDirectoryHost = request.IsDirectorySeed
                              || SystemDirectoryCatalog.IsSeedOrDirectoryHost(domain)
                              || request.DiscoveryState.DomainInvestigationPlans.TryGetValue(domain, out var priorPlan)
                              && priorPlan.Role == InvestigationSiteRole.Directory;
        var pageKind = PageContentAnalyzer.Classify(
            pageUrl,
            request.Extracted,
            request.UrlPurpose,
            isDirectoryHost);

        var listings = ShouldExtractListings(pageKind, isDirectoryHost, request.UrlPurpose)
            ? DirectoryListingExtractor.Extract(request.Html, new Uri(pageUrl))
            : [];

        var siteRole = PageContentAnalyzer.InferSiteRole(
            request.IsDirectorySeed,
            isDirectoryHost,
            pageKind,
            listings.Count,
            request.UrlPurpose,
            request.SourceType);

        var plan = GetOrCreatePlan(request.DiscoveryState, domain);
        plan.Role = ResolveSiteRole(plan.Role, siteRole);
        plan.PagesInvestigated++;
        plan.RememberPageKind(pageKind);

        var result = new PageInvestigationResult
        {
            SiteRole = plan.Role,
            PageKind = pageKind,
            Listings = listings
        };

        foreach (var listing in listings)
        {
            MergeListingOrganization(request.DiscoveryState, plan, listing, pageUrl, result);
        }

        BuildFollowUps(request, plan, pageKind, listings, result);
        BuildOrganizationCandidatesFromFirstParty(request, plan, pageKind, result);

        result.CategoryLinksFound = result.FollowUps.Count(x => x.ExpectedPageKind == InvestigationPageKind.Category);
        result.ProfileLinksFound = result.FollowUps.Count(x => x.ExpectedPageKind == InvestigationPageKind.Profile)
                                   + listings.Count(x => !string.IsNullOrWhiteSpace(x.ProfileUrl));
        result.OfficialDomainsFound = result.FollowUps.Count(x => x.IsExternalOfficialDomain)
                                      + listings.Count(x => !string.IsNullOrWhiteSpace(x.OfficialWebsiteUrl));
        result.YieldedUsefulEvidence = listings.Count > 0
                                       || result.OrganizationCandidates.Count > 0
                                       || result.FollowUps.Count > 0
                                       || pageKind is InvestigationPageKind.Contact
                                           or InvestigationPageKind.About
                                           or InvestigationPageKind.Profile;
        if (result.YieldedUsefulEvidence)
        {
            plan.UsefulEvidencePages++;
        }

        if (plan.IsHighYield && plan.AdaptivePageAllowance < 20)
        {
            plan.AdaptivePageAllowance = Math.Max(plan.AdaptivePageAllowance, 12);
        }

        result.Summary = BuildSummary(result, plan);
        return result;
    }

    public static int ResolveAdaptiveHostBonus(DomainInvestigationPlan? plan)
    {
        if (plan is null)
        {
            return 0;
        }

        if (plan.Role == InvestigationSiteRole.Directory && plan.IsHighYield)
        {
            return 16;
        }

        if (plan.IsHighYield)
        {
            return 10;
        }

        if (plan.UsefulEvidencePages > 0)
        {
            return 6;
        }

        return 0;
    }

    private static void MergeListingOrganization(
        ResearchDiscoveryState state,
        DomainInvestigationPlan plan,
        ListingOrganization listing,
        string pageUrl,
        PageInvestigationResult result)
    {
        if (ProspectJunkClassifier.IsJunkOrganization(
                listing.Name,
                listing.ProfileUrl ?? listing.SourcePageUrl,
                listing.Address)
            || listing.Confidence < 50)
        {
            return;
        }

        var key = BuildOrganizationKey(listing.Name, listing.OfficialWebsiteUrl ?? listing.ProfileUrl);
        var org = GetOrCreateOrganization(state, key, listing.Name);
        org.Name = listing.Name;
        org.ProfileUrl ??= listing.ProfileUrl;
        org.OfficialWebsiteUrl ??= listing.OfficialWebsiteUrl;
        org.OfficialDomain ??= UrlCanonicalizer.ExtractDomain(listing.OfficialWebsiteUrl);
        org.SourceDirectoryUrl ??= pageUrl;
        org.AddEvidenceUrl(pageUrl);
        org.AddEvidenceUrl(listing.ProfileUrl);
        org.NameConfidence = Math.Max(org.NameConfidence, listing.Confidence);

        if (!string.IsNullOrWhiteSpace(listing.Email))
        {
            var attribution = ContactAttributionHelper.Attribute(
                listing.Email,
                listing.ProfileUrl ?? pageUrl,
                listing.OfficialWebsiteUrl,
                pageIsDirectory: string.IsNullOrWhiteSpace(listing.OfficialWebsiteUrl));
            if (attribution.State == ContactAttributionState.SameDomain
                || (attribution.State == ContactAttributionState.DirectoryListing
                    && !string.IsNullOrWhiteSpace(listing.OfficialWebsiteUrl)
                    && EmailMatchesWebsite(listing.Email, listing.OfficialWebsiteUrl)))
            {
                if (string.IsNullOrWhiteSpace(org.Email))
                {
                    org.Email = listing.Email;
                }
                else if (!string.Equals(org.Email, listing.Email, StringComparison.OrdinalIgnoreCase)
                         && !org.AdditionalEmails.Contains(listing.Email, StringComparer.OrdinalIgnoreCase))
                {
                    org.AdditionalEmails.Add(listing.Email);
                }

                org.Phase = OrganizationInvestigationPhase.ContactEvidenceCollected;
                result.EvidenceFacts.Add(new ExtractedPageFact
                {
                    FactType = "listingEmail",
                    FactValue = listing.Email,
                    ConfidenceKind = EvidenceConfidenceKind.Unverified,
                    SourceUrl = pageUrl,
                    ExtractionMethod = listing.ExtractionMethod
                });
            }
            else
            {
                result.EvidenceFacts.Add(new ExtractedPageFact
                {
                    FactType = "listingEmailRejected",
                    FactValue = listing.Email,
                    ConfidenceKind = EvidenceConfidenceKind.Unverified,
                    SourceUrl = pageUrl,
                    ExtractionMethod = attribution.ReasonCode
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(listing.Phone))
        {
            if (string.IsNullOrWhiteSpace(org.Phone))
            {
                org.Phone = listing.Phone;
            }
            else if (!string.Equals(org.Phone, listing.Phone, StringComparison.OrdinalIgnoreCase)
                     && !org.AdditionalPhones.Contains(listing.Phone, StringComparer.OrdinalIgnoreCase))
            {
                org.AdditionalPhones.Add(listing.Phone);
            }
        }

        if (org.Phase == OrganizationInvestigationPhase.Discovered)
        {
            org.Phase = OrganizationInvestigationPhase.IdentityEvidenceCollected;
        }

        if (org.NameConfidence >= 55 && !CompanyIdentityResolver.IsGenericName(org.Name))
        {
            org.Phase = OrganizationInvestigationPhase.IdentityVerified;
        }

        plan.OrganizationsDiscovered = state.OrganizationInvestigations.Values
            .Count(x => string.Equals(
                UrlCanonicalizer.ExtractDomain(x.SourceDirectoryUrl) ?? string.Empty,
                plan.Domain,
                StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    UrlCanonicalizer.ExtractDomain(x.ProfileUrl) ?? string.Empty,
                    plan.Domain,
                    StringComparison.OrdinalIgnoreCase));

        var candidate = BuildCandidateFromListing(listing, org);
        if (candidate is not null)
        {
            result.OrganizationCandidates.Add(candidate);
        }

        result.EvidenceFacts.Add(new ExtractedPageFact
        {
            FactType = "listingOrganization",
            FactValue = listing.Name,
            ConfidenceKind = EvidenceConfidenceKind.Unverified,
            SourceUrl = pageUrl,
            ExtractionMethod = listing.ExtractionMethod
        });

        if (!string.IsNullOrWhiteSpace(listing.ProfileUrl))
        {
            result.FollowUps.Add(new InvestigationFollowUp
            {
                Url = listing.ProfileUrl,
                ReasonCode = "investigation.profile_from_listing",
                Explanation = $"Queued profile page for organization '{listing.Name}'.",
                ExpectedPageKind = InvestigationPageKind.Profile,
                PriorityBoost = 25,
                OrganizationKey = key
            });
            plan.ProfilePagesQueued++;
            org.PendingPaths.Add("profile");
        }

        if (!string.IsNullOrWhiteSpace(listing.OfficialWebsiteUrl)
            && IsPlausibleExternalOfficialWebsite(listing.OfficialWebsiteUrl))
        {
            result.FollowUps.Add(new InvestigationFollowUp
            {
                Url = listing.OfficialWebsiteUrl,
                ReasonCode = "investigation.official_website_from_listing",
                Explanation = $"Queued official website for organization '{listing.Name}'.",
                ExpectedPageKind = InvestigationPageKind.Home,
                PriorityBoost = 30,
                OrganizationKey = key,
                IsExternalOfficialDomain = true
            });
            plan.OfficialDomainsQueued++;
            org.PendingPaths.Add("official_website");

            var contactGuess = AppendPath(listing.OfficialWebsiteUrl, "/contact");
            if (contactGuess is not null && IsPlausibleExternalOfficialWebsite(contactGuess))
            {
                result.FollowUps.Add(new InvestigationFollowUp
                {
                    Url = contactGuess,
                    ReasonCode = "investigation.official_contact_guess",
                    Explanation = $"Queued likely contact page on official domain for '{listing.Name}'.",
                    ExpectedPageKind = InvestigationPageKind.Contact,
                    PriorityBoost = 28,
                    OrganizationKey = key,
                    IsExternalOfficialDomain = true
                });
                org.PendingPaths.Add("official_contact");
            }
        }
    }

    private static bool IsPlausibleExternalOfficialWebsite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (SystemDirectoryCatalog.IsSeedOrDirectoryHost(uri.Host))
        {
            return false;
        }

        var path = uri.AbsolutePath.ToLowerInvariant();
        if (path.EndsWith(".pdf", StringComparison.Ordinal)
            || path.Contains("/auth/", StringComparison.Ordinal)
            || path.Contains("/login", StringComparison.Ordinal)
            || path.Contains("/assets/", StringComparison.Ordinal)
            || path.Contains("school-search", StringComparison.Ordinal)
            || path.Contains("create-account", StringComparison.Ordinal)
            || path.Contains("schoology", StringComparison.Ordinal)
            || path.Contains("powerschool", StringComparison.Ordinal))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("office.com", StringComparison.Ordinal)
            || host.Contains("youtu", StringComparison.Ordinal)
            || host.Contains("schoology.", StringComparison.Ordinal)
            || host.Contains("powerschool.", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static void BuildFollowUps(
        FetchedPageInvestigationRequest request,
        DomainInvestigationPlan plan,
        InvestigationPageKind pageKind,
        List<ListingOrganization> listings,
        PageInvestigationResult result)
    {
        var isDirectory = plan.Role == InvestigationSiteRole.Directory;
        if (request.Depth >= request.MaxDepth)
        {
            return;
        }

        var seen = new HashSet<string>(
            result.FollowUps.Select(x => x.Url),
            StringComparer.OrdinalIgnoreCase);

        foreach (var link in request.Extracted.SameOriginLinks.Take(40))
        {
            var kind = PageContentAnalyzer.ClassifyLink(link, isDirectory);
            if (!PageContentAnalyzer.IsInternalInvestigationTarget(kind)
                && !(isDirectory && kind == InvestigationPageKind.Unknown && LooksLikeDirectoryChild(link)))
            {
                continue;
            }

            if (isDirectory
                && kind == InvestigationPageKind.Unknown
                && !LooksLikeDirectoryChild(link))
            {
                continue;
            }

            if (plan.Role == InvestigationSiteRole.Organization
                && kind is InvestigationPageKind.Category or InvestigationPageKind.Listing or InvestigationPageKind.Pagination)
            {
                continue;
            }

            if (!seen.Add(link))
            {
                continue;
            }

            var reason = kind switch
            {
                InvestigationPageKind.Category => "investigation.category_page",
                InvestigationPageKind.Listing => "investigation.listing_page",
                InvestigationPageKind.Profile => "investigation.profile_page",
                InvestigationPageKind.Contact => "investigation.contact_page",
                InvestigationPageKind.About => "investigation.about_page",
                InvestigationPageKind.Team => "investigation.team_page",
                InvestigationPageKind.Location => "investigation.location_page",
                InvestigationPageKind.Pagination => "investigation.pagination",
                InvestigationPageKind.Services => "investigation.services_page",
                _ => "investigation.internal_followup"
            };

            result.FollowUps.Add(new InvestigationFollowUp
            {
                Url = link,
                ReasonCode = reason,
                Explanation = $"Queued {kind} page because investigation of {pageKind} indicated useful structure.",
                ExpectedPageKind = kind == InvestigationPageKind.Unknown && isDirectory
                    ? InvestigationPageKind.Profile
                    : kind,
                PriorityBoost = PriorityBoostFor(kind, isDirectory)
            });

            if (kind == InvestigationPageKind.Category)
            {
                plan.CategoryPagesQueued++;
            }

            if (kind == InvestigationPageKind.Profile)
            {
                plan.ProfilePagesQueued++;
            }
        }

        if (plan.Role == InvestigationSiteRole.Organization
            || (!isDirectory && pageKind is InvestigationPageKind.Home or InvestigationPageKind.About or InvestigationPageKind.Unknown))
        {
            foreach (var link in request.Extracted.SameOriginLinks
                         .Where(x => PageContentAnalyzer.ClassifyLink(x, false)
                             is InvestigationPageKind.Contact
                             or InvestigationPageKind.About
                             or InvestigationPageKind.Team
                             or InvestigationPageKind.Location)
                         .Take(8))
            {
                if (!seen.Add(link))
                {
                    continue;
                }

                var kind = PageContentAnalyzer.ClassifyLink(link, false);
                result.FollowUps.Add(new InvestigationFollowUp
                {
                    Url = link,
                    ReasonCode = "investigation.first_party_" + kind.ToString().ToLowerInvariant(),
                    Explanation = $"Queued first-party {kind} page for deeper contact/identity evidence.",
                    ExpectedPageKind = kind,
                    PriorityBoost = PriorityBoostFor(kind, false)
                });
            }
        }

        _ = listings;
    }

    private static void BuildOrganizationCandidatesFromFirstParty(
        FetchedPageInvestigationRequest request,
        DomainInvestigationPlan plan,
        InvestigationPageKind pageKind,
        PageInvestigationResult result)
    {
        if (plan.Role == InvestigationSiteRole.Directory || request.IsDirectorySeed)
        {
            return;
        }

        if (pageKind is InvestigationPageKind.Category or InvestigationPageKind.Listing or InvestigationPageKind.Pagination)
        {
            return;
        }

        var host = request.NormalizedDomain ?? UrlCanonicalizer.ExtractDomain(request.PageUrl);
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        var key = BuildOrganizationKey(
            request.DiscoveryState.HostOrganizationNames.GetValueOrDefault(host)
            ?? request.Extracted.OrganizationName
            ?? request.Extracted.OpenGraphSiteName
            ?? host,
            "https://" + host);

        var org = GetOrCreateOrganization(
            request.DiscoveryState,
            key,
            request.Extracted.OrganizationName
            ?? request.Extracted.OpenGraphSiteName
            ?? request.DiscoveryState.HostOrganizationNames.GetValueOrDefault(host)
            ?? host);
        org.OfficialDomain ??= host;
        org.OfficialWebsiteUrl ??= "https://" + host;
        org.AddEvidenceUrl(request.PageUrl);

        if (pageKind == InvestigationPageKind.Contact)
        {
            org.CompletedPaths.Add("contact");
            org.PendingPaths.Remove("contact");
            org.PendingPaths.Remove("official_contact");
        }

        if (pageKind == InvestigationPageKind.About)
        {
            org.CompletedPaths.Add("about");
            org.PendingPaths.Remove("about");
        }
    }

    private static ProspectCandidate? BuildCandidateFromListing(
        ListingOrganization listing,
        OrganizationInvestigationState org)
    {
        if (string.IsNullOrWhiteSpace(listing.Name)
            || ProspectJunkClassifier.IsJunkOrganization(listing.Name, listing.ProfileUrl ?? listing.SourcePageUrl))
        {
            return null;
        }

        if (listing.Confidence < 50)
        {
            return null;
        }

        var website = listing.OfficialWebsiteUrl;
        var officialDomain = UrlCanonicalizer.ExtractDomain(website);
        if (!string.IsNullOrWhiteSpace(officialDomain)
            && SystemDirectoryCatalog.IsSeedOrDirectoryHost(officialDomain))
        {
            website = null;
            officialDomain = null;
        }

        var entity = EntityClassifier.Classify(
            listing.Name,
            listing.Name,
            listing.Address,
            website ?? listing.ProfileUrl);
        if (!EntityClassifier.IsAllowedTarget(entity.EntityType, ResearchEntityType.TargetBusiness))
        {
            return null;
        }

        var email = org.Email ?? listing.Email;
        var attribution = ContactAttributionHelper.Attribute(
            email,
            listing.ProfileUrl ?? listing.SourcePageUrl,
            website,
            pageIsDirectory: string.IsNullOrWhiteSpace(website));

        if (!string.IsNullOrWhiteSpace(email)
            && attribution.State is not ContactAttributionState.SameDomain
                and not ContactAttributionState.Unverified
                and not ContactAttributionState.DirectoryListing)
        {
            email = null;
            attribution = ContactAttributionHelper.Attribute(null, listing.ProfileUrl, website, false);
        }

        var acceptedEmail = attribution.State is ContactAttributionState.SameDomain
            or ContactAttributionState.DirectoryListing
            ? email
            : null;

        var country = listing.Country
                      ?? OrganizationCountryGate.InferCountryFromUrl(listing.ProfileUrl)
                      ?? OrganizationCountryGate.InferCountryFromUrl(listing.SourcePageUrl)
                      ?? OrganizationCountryGate.InferCountryFromUrl(website);
        var countrySource = !string.IsNullOrWhiteSpace(listing.CountryEvidenceSource)
            ? listing.CountryEvidenceSource
            : country is null
                ? null
                : "listing_url_geography";

        var attributedEmails = BuildAttributedEmails(listing, org, acceptedEmail, website);
        var attributedPhones = BuildAttributedPhones(listing, org, website);
        var additionalEmails = attributedEmails
            .Where(x => !string.Equals(x.Value, acceptedEmail, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();
        var phone = listing.Phone ?? org.Phone;
        var additionalPhones = attributedPhones
            .Where(x => !string.Equals(x.Value, phone, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();

        return new ProspectCandidate
        {
            CompanyName = listing.Name,
            Website = website,
            OfficialDomain = officialDomain,
            Country = country,
            CountryEvidenceSource = countrySource,
            CountryEvidenceSummary = country is null
                ? null
                : $"{countrySource}:{country}",
            Email = acceptedEmail,
            AdditionalEmails = additionalEmails,
            AttributedEmails = attributedEmails,
            Phone = phone,
            AdditionalPhones = additionalPhones,
            AttributedPhones = attributedPhones,
            WhatsApp = listing.WhatsApp,
            Address = listing.Address,
            SourceUrl = listing.ProfileUrl ?? listing.SourcePageUrl,
            AdditionalSourceUrls = string.IsNullOrWhiteSpace(listing.SourcePageUrl)
                ? []
                : [listing.SourcePageUrl],
            SourceType = ProspectSourceType.Directory,
            SourceName = "DirectoryListingInvestigation",
            ResearchNotes = string.IsNullOrWhiteSpace(website)
                ? $"Directory listing '{listing.Name}' retained for continued investigation; official website not yet verified."
                : $"Extracted from directory listing via {listing.ExtractionMethod}.",
            EntityType = entity.EntityType,
            EntityTypeConfidence = entity.Confidence,
            EntityTypeReasonCode = entity.ReasonCode,
            NameConfidence = Math.Max(listing.Confidence, org.NameConfidence),
            NameReasonCode = "investigation.listing_name",
            ContactEvidenceUrl = attribution.EvidenceUrl,
            ContactPageDomain = attribution.PageDomain,
            ContactAttributionState = string.IsNullOrWhiteSpace(acceptedEmail)
                ? ContactAttributionState.Rejected
                : attribution.State,
            ContactAttributionConfidence = attribution.Confidence,
            ContactAttributionReasonCode = attribution.ReasonCode,
            City = listing.City
        };
    }

    private static List<Domain.Services.AttributedContact> BuildAttributedEmails(
        ListingOrganization listing,
        OrganizationInvestigationState org,
        string? primaryEmail,
        string? website)
    {
        var result = new List<Domain.Services.AttributedContact>();
        var source = listing.ProfileUrl ?? listing.SourcePageUrl ?? website;
        void Add(string? value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (result.Any(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            result.Add(new Domain.Services.AttributedContact
            {
                Value = value,
                Channel = "email",
                SourceUrl = source,
                DiscoveredAtUtc = DateTimeOffset.UtcNow,
                VerificationStatus = "extracted",
                Context = context,
                AttributionReasonCode = "contact.listing_or_org"
            });
        }

        Add(primaryEmail, "primary");
        Add(listing.Email, "listing");
        Add(org.Email, "organization_state");
        foreach (var extra in org.AdditionalEmails)
        {
            Add(extra, "organization_additional");
        }

        return result;
    }

    private static List<Domain.Services.AttributedContact> BuildAttributedPhones(
        ListingOrganization listing,
        OrganizationInvestigationState org,
        string? website)
    {
        var result = new List<Domain.Services.AttributedContact>();
        var source = listing.ProfileUrl ?? listing.SourcePageUrl ?? website;
        void Add(string? value, string context)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (result.Any(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            result.Add(new Domain.Services.AttributedContact
            {
                Value = value,
                Channel = "phone",
                SourceUrl = source,
                DiscoveredAtUtc = DateTimeOffset.UtcNow,
                VerificationStatus = "extracted",
                Context = context,
                AttributionReasonCode = "contact.listing_or_org"
            });
        }

        Add(listing.Phone, "listing");
        Add(org.Phone, "organization_state");
        foreach (var extra in org.AdditionalPhones)
        {
            Add(extra, "organization_additional");
        }

        return result;
    }

    private static InvestigationSiteRole ResolveSiteRole(
        InvestigationSiteRole current,
        InvestigationSiteRole observed)
    {
        if (observed == InvestigationSiteRole.Unknown)
        {
            return current;
        }

        if (current == InvestigationSiteRole.Directory
            || observed == InvestigationSiteRole.Directory)
        {
            return InvestigationSiteRole.Directory;
        }

        return observed;
    }

    private static DomainInvestigationPlan GetOrCreatePlan(ResearchDiscoveryState state, string domain)
    {
        if (state.DomainInvestigationPlans.TryGetValue(domain, out var existing))
        {
            return existing;
        }

        var plan = new DomainInvestigationPlan { Domain = domain };
        state.DomainInvestigationPlans[domain] = plan;
        return plan;
    }

    private static OrganizationInvestigationState GetOrCreateOrganization(
        ResearchDiscoveryState state,
        string key,
        string name)
    {
        if (state.OrganizationInvestigations.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var org = new OrganizationInvestigationState
        {
            OrganizationKey = key,
            Name = name
        };
        state.OrganizationInvestigations[key] = org;
        return org;
    }

    private static string BuildOrganizationKey(string name, string? websiteOrProfile)
    {
        var domain = UrlCanonicalizer.ExtractDomain(websiteOrProfile) ?? string.Empty;
        return (name.Trim().ToLowerInvariant() + "|" + domain.ToLowerInvariant());
    }

    private static bool ShouldExtractListings(
        InvestigationPageKind pageKind,
        bool isDirectoryHost,
        UrlPagePurpose urlPurpose)
    {
        if (urlPurpose == UrlPagePurpose.DirectoryListing)
        {
            return true;
        }

        return pageKind is InvestigationPageKind.Category
            or InvestigationPageKind.Listing
            or InvestigationPageKind.Pagination
            or InvestigationPageKind.Profile
            || (isDirectoryHost
                && pageKind is not InvestigationPageKind.Contact
                    and not InvestigationPageKind.About);
    }

    private static bool LooksLikeDirectoryChild(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2;
    }

    private static int PriorityBoostFor(InvestigationPageKind kind, bool isDirectory)
        => kind switch
        {
            InvestigationPageKind.Contact => 32,
            InvestigationPageKind.Profile => isDirectory ? 28 : 18,
            InvestigationPageKind.Category => 24,
            InvestigationPageKind.Listing => 26,
            InvestigationPageKind.About => 20,
            InvestigationPageKind.Team => 18,
            InvestigationPageKind.Location => 18,
            InvestigationPageKind.Pagination => 16,
            InvestigationPageKind.Services => 14,
            _ => 8
        };

    private static string? AppendPath(string baseUrl, string path)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
        {
            return null;
        }

        var relative = path.StartsWith('/') ? path.TrimStart('/') : path;
        if (!Uri.TryCreate(uri, relative, out var combined))
        {
            return null;
        }

        return combined.ToString().TrimEnd('/');
    }

    private static bool EmailMatchesWebsite(string email, string website)
    {
        var emailHost = email.Split('@').LastOrDefault()?.Trim().ToLowerInvariant();
        var websiteHost = UrlCanonicalizer.ExtractDomain(website);
        if (string.IsNullOrWhiteSpace(emailHost) || string.IsNullOrWhiteSpace(websiteHost))
        {
            return false;
        }

        return emailHost.Equals(websiteHost, StringComparison.OrdinalIgnoreCase)
               || websiteHost.EndsWith("." + emailHost, StringComparison.OrdinalIgnoreCase)
               || emailHost.EndsWith("." + websiteHost, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSummary(PageInvestigationResult result, DomainInvestigationPlan plan)
    {
        return $"role={plan.Role}; page={result.PageKind}; listings={result.Listings.Count}; "
               + $"followUps={result.FollowUps.Count}; candidates={result.OrganizationCandidates.Count}; "
               + $"orgs={plan.OrganizationsDiscovered}";
    }
}

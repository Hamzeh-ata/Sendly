using System.Net;
using System.Text.RegularExpressions;
using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Sendly.Infrastructure.Research;

public class WebsitePageCrawlResult
{
    public string Url { get; set; } = string.Empty;

    public string Html { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public List<string> Emails { get; set; } = [];

    public List<string> Phones { get; set; } = [];

    public List<string> DiscoveredUrls { get; set; } = [];
}

public class WebsitePageCrawler
{
    private static readonly Regex EmailRegex = new(
        @"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PhoneRegex = new(
        @"(\+?\d[\d\s\-()]{7,}\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex HrefRegex = new(
        @"href\s*=\s*[""']([^""'#]+)[""']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] PriorityPathHints =
    [
        "contact", "about", "team", "people", "support", "reach", "get-in-touch", "company"
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebsitePageCrawler> _logger;

    public WebsitePageCrawler(IHttpClientFactory httpClientFactory, ILogger<WebsitePageCrawler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebsitePageCrawlResult>> CrawlSiteAsync(
        Uri seedUri,
        int maxPages,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("ResearchCrawler");
        var queue = new Queue<Uri>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<WebsitePageCrawlResult>();
        queue.Enqueue(seedUri);

        while (queue.Count > 0 && results.Count < maxPages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            var key = NormalizeUrlKey(current);
            if (!visited.Add(key))
            {
                continue;
            }

            try
            {
                using var response = await client.GetAsync(current, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Website crawl failed {Url} status {Status}", current, (int)response.StatusCode);
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var text = HtmlToText(html);
                var emails = ExtractEmails(html);
                var phones = ExtractPhones(text);
                var discovered = DiscoverSameOriginLinks(html, current);
                results.Add(new WebsitePageCrawlResult
                {
                    Url = current.ToString(),
                    Html = html,
                    Text = text,
                    Emails = emails,
                    Phones = phones,
                    DiscoveredUrls = discovered.Select(x => x.ToString()).ToList()
                });

                foreach (var link in OrderLinks(discovered))
                {
                    var linkKey = NormalizeUrlKey(link);
                    if (!visited.Contains(linkKey))
                    {
                        queue.Enqueue(link);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Website crawl page failed for {Url}", current);
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<ProspectCandidate>> ExtractCandidatesAsync(
        IAiResearchService ai,
        ProspectSourceContext context,
        Uri seedUri,
        string? companyName,
        string? country,
        string? industry,
        string? sourceName,
        int maxPages,
        CancellationToken cancellationToken)
    {
        var pages = await CrawlSiteAsync(seedUri, maxPages, cancellationToken);
        var result = new List<ProspectCandidate>();
        if (pages.Count == 0)
        {
            return result;
        }

        foreach (var page in pages)
        {
            var structured = await ai.StructureFromSourceTextAsync(
                new AiSourceStructureRequest
                {
                    ProjectName = context.ProjectName,
                    ProjectDescription = context.Description,
                    ResearchInstructions = context.ResearchInstructions,
                    Keywords = context.Keywords,
                    TargetRoles = context.TargetRoles,
                    SourceUrl = page.Url,
                    SourceName = sourceName ?? "Company website",
                    HintCompanyName = companyName,
                    HintCountry = country,
                    HintIndustry = industry,
                    PageText = page.Text,
                    ExtractedEmails = page.Emails,
                    ExtractedPhones = page.Phones
                },
                cancellationToken);

            foreach (var candidate in structured)
            {
                if (!context.IncludeEmail)
                {
                    candidate.Email = null;
                    candidate.AdditionalEmails.Clear();
                }

                if (!context.IncludeWhatsApp)
                {
                    candidate.WhatsApp = null;
                }

                if (string.IsNullOrWhiteSpace(candidate.CompanyName))
                {
                    continue;
                }

                result.Add(candidate);
            }
        }

        if (result.Count == 0)
        {
            result.Add(new ProspectCandidate
            {
                CompanyName = string.IsNullOrWhiteSpace(companyName)
                    ? InferCompanyFromHost(seedUri)
                    : companyName.Trim(),
                Website = seedUri.GetLeftPart(UriPartial.Authority),
                Country = country,
                Industry = industry,
                SourceUrl = seedUri.ToString(),
                SourceType = Domain.Enums.ProspectSourceType.Website,
                SourceName = sourceName ?? "Company website",
                ResearchNotes = "Website confirmed. Contact details were not found on crawled pages."
            });
        }

        return ProspectCandidateDeduplicator.DeduplicateAndMerge(result);
    }

    private static string InferCompanyFromHost(Uri uri)
    {
        var host = uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? uri.Host : parts[0];
    }

    private static List<Uri> OrderLinks(List<Uri> links)
    {
        return links
            .OrderByDescending(IsPriorityLink)
            .ThenBy(x => x.AbsoluteUri.Length)
            .ToList();
    }

    private static bool IsPriorityLink(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        foreach (var hint in PriorityPathHints)
        {
            if (path.Contains(hint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<Uri> DiscoverSameOriginLinks(string html, Uri baseUri)
    {
        var result = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HrefRegex.Matches(html))
        {
            var raw = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (string.IsNullOrWhiteSpace(raw)
                || raw.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(baseUri, raw, out var absolute))
            {
                continue;
            }

            if (!string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            var key = NormalizeUrlKey(absolute);
            if (seen.Add(key))
            {
                result.Add(absolute);
            }
        }

        return result;
    }

    private static string NormalizeUrlKey(Uri uri)
    {
        return ProspectNormalizer.NormalizeUrl(uri.ToString())
               ?? uri.GetLeftPart(UriPartial.Path).ToLowerInvariant();
    }

    private static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutScripts = Regex.Replace(
            html,
            @"<script[\s\S]*?</script>|<style[\s\S]*?</style>",
            " ",
            RegexOptions.IgnoreCase);
        var text = TagRegex.Replace(withoutScripts, " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static List<string> ExtractEmails(string html)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EmailRegex.Matches(html))
        {
            var email = match.Value.Trim().TrimEnd('.', ',', ';');
            var normalized = ProspectNormalizer.NormalizeEmail(email);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                continue;
            }

            result.Add(email);
        }

        return result;
    }

    private static List<string> ExtractPhones(string text)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PhoneRegex.Matches(text))
        {
            var raw = match.Value.Trim();
            var normalized = ProspectNormalizer.NormalizePhone(raw);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                continue;
            }

            result.Add(raw);
        }

        return result;
    }
}

using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services;

public class DiscoverySourceExpansionService
{
    private readonly DiscoverySourceRegistryService _registry;
    private readonly ISafeHttpFetcher _fetcher;
    private readonly ResearchOptions _options;

    public DiscoverySourceExpansionService(
        DiscoverySourceRegistryService registry,
        ISafeHttpFetcher fetcher,
        IOptions<ResearchOptions> options)
    {
        _registry = registry;
        _fetcher = fetcher;
        _options = options.Value;
    }

    public async Task<int> EvaluateCandidateLinksAsync(
        IEnumerable<DiscoveredUrlCandidate> candidates,
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries,
        Guid? parentSourceId,
        int currentDepth,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableDiscoverySourceExpansion)
        {
            return 0;
        }

        if (currentDepth >= _options.MaxSourceExpansionDepth)
        {
            return 0;
        }

        var scored = new List<(DiscoveredUrlCandidate Candidate, SourceExpansionScore Score, string Canonical)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var canonical = UrlCanonicalizer.Canonicalize(candidate.Url);
            if (string.IsNullOrWhiteSpace(canonical) || !seen.Add(canonical))
            {
                continue;
            }

            if (!Uri.TryCreate(canonical, UriKind.Absolute, out var uri)
                || await SsrfGuard.IsBlockedUriAsync(uri, cancellationToken))
            {
                continue;
            }

            var score = SourceExpansionScorer.Score(
                canonical,
                candidate.LinkText,
                candidate.SurroundingContext,
                pageTitle: null,
                pageExcerpt: null,
                industries,
                countries);
            if (!score.LooksPromising && score.UrlSignal == 0)
            {
                continue;
            }

            scored.Add((candidate, score, canonical));
        }

        var registered = 0;
        foreach (var item in scored.OrderByDescending(x => x.Score.Total).Take(10))
        {
            var probe = await ProbeContentAsync(item.Canonical, industries, countries, cancellationToken);
            var combined = item.Score.Total + probe.ContentBonus;
            if (combined < 25)
            {
                continue;
            }

            var source = await _registry.TryRegisterExpandedSourceAsync(
                DeriveName(item.Candidate, item.Canonical),
                item.Canonical,
                DiscoverySourceType.IndustryAssociation,
                countries,
                industries,
                parentSourceId,
                $"Expanded from crawl depth {currentDepth}; score={combined}",
                cancellationToken);
            if (source is null)
            {
                continue;
            }

            await _registry.UpdateConfigurationAsync(
                source.Id,
                JsonSerializer.Serialize(new
                {
                    expansionScore = combined,
                    urlSignal = item.Score.UrlSignal,
                    linkTextSignal = item.Score.LinkTextSignal,
                    contentSignal = probe.ContentBonus,
                    probed = probe.Probed,
                    httpStatus = probe.HttpStatus
                }),
                cancellationToken);

            var useful = combined >= 35 || probe.ContentBonus >= 12;
            await _registry.PromoteCandidateIfUsefulAsync(
                source.Id,
                useful,
                useful ? null : "Expansion score below promotion threshold.",
                cancellationToken);
            if (useful)
            {
                registered++;
            }
        }

        return registered;
    }

    public async Task<int> EvaluateCandidateLinksAsync(
        IEnumerable<string> candidateUrls,
        IReadOnlyList<string> countries,
        IReadOnlyList<string> industries,
        Guid? parentSourceId,
        int currentDepth,
        CancellationToken cancellationToken)
    {
        var candidates = candidateUrls.Select(url => new DiscoveredUrlCandidate { Url = url });
        return await EvaluateCandidateLinksAsync(
            candidates,
            countries,
            industries,
            parentSourceId,
            currentDepth,
            cancellationToken);
    }

    public static bool LooksLikeDirectory(string url)
        => SourceExpansionScorer.LooksLikeDirectory(url);

    private async Task<(bool Probed, int HttpStatus, int ContentBonus)> ProbeContentAsync(
        string url,
        IReadOnlyList<string> industries,
        IReadOnlyList<string> countries,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return (false, 0, 0);
        }

        try
        {
            var fetch = await _fetcher.FetchAsync(new SafeHttpFetchRequest { Url = uri }, cancellationToken);
            if (!fetch.Succeeded || string.IsNullOrWhiteSpace(fetch.Body))
            {
                return (true, fetch.StatusCode ?? 0, 0);
            }

            var excerpt = fetch.Body.Length > 4000 ? fetch.Body[..4000] : fetch.Body;
            var score = SourceExpansionScorer.Score(
                url,
                linkText: null,
                surroundingContext: null,
                pageTitle: null,
                pageExcerpt: excerpt,
                industries,
                countries);
            return (true, fetch.StatusCode ?? 0, score.ContentSignal + score.IndustrySignal + score.GeographySignal);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (true, 0, 0);
        }
    }

    private static string DeriveName(DiscoveredUrlCandidate candidate, string canonical)
    {
        if (!string.IsNullOrWhiteSpace(candidate.LinkText))
        {
            return candidate.LinkText.Trim();
        }

        return Uri.TryCreate(canonical, UriKind.Absolute, out var uri) ? uri.Host : canonical;
    }
}

using Sendly.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Sendly.Application.Services;

public class CrawlRetentionService
{
    private readonly ISendlyDbContext _db;
    private readonly ILogger<CrawlRetentionService> _logger;

    public CrawlRetentionService(ISendlyDbContext db, ILogger<CrawlRetentionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<int> PurgeFullTextExcerptsAsync(
        Guid researchRunId,
        CancellationToken cancellationToken)
    {
        var pages = await _db.CrawledPages
            .Where(x => x.ResearchRunId == researchRunId && x.TextExcerpt != null)
            .ToListAsync(cancellationToken);

        foreach (var page in pages)
        {
            if (page.TextExcerpt is null)
            {
                continue;
            }

            page.TextExcerpt = page.TextExcerpt.Length <= 500
                ? page.TextExcerpt
                : page.TextExcerpt[..500];
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Retained truncated excerpts for {Count} crawled pages on run {ResearchRunId}",
            pages.Count,
            researchRunId);
        return pages.Count;
    }
}

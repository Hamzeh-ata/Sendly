using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Application.Tests;

public class KnownProspectReuseEvaluatorTests
{
    [Fact]
    public void Evaluate_skips_path_matched_contacted_site_but_not_sibling_paths()
    {
        var known = new Prospect
        {
            Id = Guid.NewGuid(),
            Website = "https://example.com/known-clean",
            Email = "info@example.com",
            Status = ProspectStatus.Contacted,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var index = KnownProspectReuseEvaluator.IndexProspects([known]);
        var now = DateTimeOffset.UtcNow;

        var skip = KnownProspectReuseEvaluator.Evaluate(
            "https://example.com/known-clean/contact",
            index,
            now,
            30);
        var fresh = KnownProspectReuseEvaluator.Evaluate(
            "https://example.com/new-clean",
            index,
            now,
            30);

        Assert.True(skip.SkipFetch);
        Assert.Equal("reuse.contacted_fresh", skip.ReasonCode);
        Assert.False(fresh.SkipFetch);
        Assert.Equal("reuse.unknown", fresh.ReasonCode);
    }

    [Fact]
    public void Evaluate_domain_root_prospect_covers_host_paths()
    {
        var known = new Prospect
        {
            Id = Guid.NewGuid(),
            Website = "https://acme.example",
            Email = "hello@acme.example",
            Status = ProspectStatus.Contacted,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var index = KnownProspectReuseEvaluator.IndexProspects([known]);

        var decision = KnownProspectReuseEvaluator.Evaluate(
            "https://acme.example/about",
            index,
            DateTimeOffset.UtcNow,
            30);

        Assert.True(decision.SkipFetch);
        Assert.Equal("reuse.contacted_fresh", decision.ReasonCode);
    }
}

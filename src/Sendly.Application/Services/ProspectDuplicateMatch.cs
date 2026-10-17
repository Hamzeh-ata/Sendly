using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Application.Services;

public class ProspectDuplicateMatch
{
    public ProspectDuplicateKind Kind { get; set; } = ProspectDuplicateKind.None;

    public Prospect? ExistingProspect { get; set; }

    public bool ShouldMerge { get; set; }

    public bool ShouldSkip { get; set; }

    public bool NeedsReview { get; set; }

    public static ProspectDuplicateMatch None()
    {
        return new ProspectDuplicateMatch();
    }

    public static ProspectDuplicateMatch Skip(ProspectDuplicateKind kind, Prospect? existing = null)
    {
        return new ProspectDuplicateMatch
        {
            Kind = kind,
            ExistingProspect = existing,
            ShouldSkip = true
        };
    }

    public static ProspectDuplicateMatch Merge(ProspectDuplicateKind kind, Prospect existing)
    {
        return new ProspectDuplicateMatch
        {
            Kind = kind,
            ExistingProspect = existing,
            ShouldMerge = true
        };
    }

    public static ProspectDuplicateMatch Review(ProspectDuplicateKind kind, Prospect? existing = null)
    {
        return new ProspectDuplicateMatch
        {
            Kind = kind,
            ExistingProspect = existing,
            ShouldMerge = existing is not null,
            NeedsReview = true
        };
    }
}

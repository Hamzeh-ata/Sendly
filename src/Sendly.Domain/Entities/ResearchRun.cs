using System.ComponentModel.DataAnnotations;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

public class ResearchRun
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    public ResearchRunStatus Status { get; set; } = ResearchRunStatus.Pending;

    public int RequestedProspectCount { get; set; }

    public bool IncludeEmail { get; set; } = true;

    public bool IncludeWhatsApp { get; set; }

    public int MinQualificationScore { get; set; } = 70;

    public int ProgressPercent { get; set; }

    public int CompaniesFound { get; set; }

    public int ContactsFound { get; set; }

    public int ValidEmails { get; set; }

    public int ValidWhatsApp { get; set; }

    public int DuplicatesFound { get; set; }

    public int RejectedCount { get; set; }

    public int QualifiedCount { get; set; }

    public int SearchQueriesExecuted { get; set; }

    public int SearchResultsReceived { get; set; }

    public int CandidatesDiscovered { get; set; }

    public int CandidatesAfterDeduplication { get; set; }

    public int CandidatesValidated { get; set; }

    public int ProspectsSaved { get; set; }

    public int ProspectsSkippedAsDuplicates { get; set; }

    public bool ResearchLimitsReached { get; set; }

    [MaxLength(500)]
    public string? ResearchLimitsReachedReason { get; set; }

    [MaxLength(8000)]
    public string? RejectionReasonsJson { get; set; }

    [MaxLength(4000)]
    public string? SearchProviderDiagnosticsJson { get; set; }

    [MaxLength(2000)]
    public string? Error { get; set; }

    [MaxLength(2000)]
    public string? Warning { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public ICollection<Prospect> Prospects { get; set; } = new List<Prospect>();
}

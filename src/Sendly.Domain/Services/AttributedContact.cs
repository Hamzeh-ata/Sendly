namespace Sendly.Domain.Services;

public class AttributedContact
{
    public string Value { get; set; } = string.Empty;

    public string Channel { get; set; } = "email";

    public string? SourceUrl { get; set; }

    public DateTimeOffset DiscoveredAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string VerificationStatus { get; set; } = "extracted";

    public string? Context { get; set; }

    public string? AttributionReasonCode { get; set; }

    public string? NormalizedValue { get; set; }

    public string? PageTitle { get; set; }

    public string? SurroundingText { get; set; }

    public int Confidence { get; set; }

    public string ReviewStatus { get; set; } = "unreviewed";
}

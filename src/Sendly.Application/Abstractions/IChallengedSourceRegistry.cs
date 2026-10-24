using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class ChallengedSourceSnapshot
{
    public string SourceKey { get; set; } = string.Empty;

    public HttpAccessResponseKind Kind { get; set; }

    public string Summary { get; set; } = string.Empty;

    public DateTimeOffset ChallengedAtUtc { get; set; }

    public DateTimeOffset DisabledUntilUtc { get; set; }

    public int ChallengeCount { get; set; }

    public int? HttpStatusCode { get; set; }
}

public interface IChallengedSourceRegistry
{
    bool IsTemporarilyDisabled(string sourceKey, DateTimeOffset nowUtc);

    void RecordChallenge(
        string sourceKey,
        HttpAccessResponseKind kind,
        string summary,
        DateTimeOffset nowUtc,
        int? httpStatusCode = null,
        TimeSpan? disableFor = null);

    void RecordSuccess(string sourceKey);

    IReadOnlyList<ChallengedSourceSnapshot> ListActive(DateTimeOffset nowUtc);

    IReadOnlyList<string> ListDisabledKeys(DateTimeOffset nowUtc);
}

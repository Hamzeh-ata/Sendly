namespace Sendly.Application.Abstractions;

public class OpenAiMapContactHintRequest
{
    public string CompanyName { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? Industry { get; set; }

    public string? Website { get; set; }

    public string? StreetAddress { get; set; }

    public List<string> MissingFields { get; set; } = [];

    public List<string> TargetCountries { get; set; } = [];
}

public class OpenAiMapContactHintResult
{
    public bool IsAvailable { get; set; }

    public string? OfficialWebsite { get; set; }

    public string? ContactPageUrl { get; set; }

    public List<string> SearchQueries { get; set; } = [];

    public string? Reason { get; set; }

    public string? ErrorSummary { get; set; }
}

public class OpenAiMapPageContactRequest
{
    public string CompanyName { get; set; } = string.Empty;

    public string PageUrl { get; set; } = string.Empty;

    public string? PageTitle { get; set; }

    public string? PageText { get; set; }

    public List<string> AllowedEmails { get; set; } = [];

    public List<string> AllowedPhones { get; set; } = [];

    public List<string> MissingFields { get; set; } = [];
}

public class OpenAiMapPageContactResult
{
    public bool IsAvailable { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Website { get; set; }

    public string? ContactPageUrl { get; set; }

    public string? Reason { get; set; }

    public string? ErrorSummary { get; set; }
}

public interface IOpenAiMapContactAssistant
{
    bool IsConfigured { get; }

    Task<OpenAiMapContactHintResult> SuggestContactTargetsAsync(
        OpenAiMapContactHintRequest request,
        CancellationToken cancellationToken);

    Task<OpenAiMapPageContactResult> ExtractContactsFromPageAsync(
        OpenAiMapPageContactRequest request,
        CancellationToken cancellationToken);
}

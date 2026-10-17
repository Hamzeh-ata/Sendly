using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Ai;

public class OpenAiResearchService : IAiResearchService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiOptions _options;
    private readonly RuleBasedAiResearchService _fallback;
    private readonly ILogger<OpenAiResearchService> _logger;

    public OpenAiResearchService(
        IHttpClientFactory httpClientFactory,
        IOptions<OpenAiOptions> options,
        RuleBasedAiResearchService fallback,
        ILogger<OpenAiResearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _fallback = fallback;
        _logger = logger;
    }

    public bool IsAvailable => _options.IsConfigured || _fallback.IsAvailable;

    public async Task<AiQualificationResult> QualifyAsync(
        AiClassificationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return await _fallback.QualifyAsync(request, cancellationToken);
        }

        try
        {
            var prompt = BuildQualificationPrompt(request);
            var content = await CompleteJsonAsync(prompt, cancellationToken);
            var parsed = ParseQualification(content);
            if (parsed is null)
            {
                return await _fallback.QualifyAsync(request, cancellationToken);
            }

            parsed.Industry = NullIfBlank(parsed.Industry) ?? NullIfBlank(request.Industry);
            parsed.CompanySize = NullIfBlank(parsed.CompanySize) ?? NullIfBlank(request.CompanySize);
            parsed.ContactRole = NullIfBlank(parsed.ContactRole) ?? NullIfBlank(request.ContactRole);
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                parsed.EmailConfidenceScore = null;
            }

            if (string.IsNullOrWhiteSpace(request.WhatsApp))
            {
                parsed.WhatsAppConfidenceScore = null;
            }

            if (string.IsNullOrWhiteSpace(parsed.Summary))
            {
                var fallback = await _fallback.QualifyAsync(request, cancellationToken);
                parsed.Summary = fallback.Summary;
            }

            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI qualification failed; using rule-based fallback");
            return await _fallback.QualifyAsync(request, cancellationToken);
        }
    }

    public async Task<AiTemplateRecommendationResult> RecommendTemplateAsync(
        AiTemplateRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured || request.Templates.Count == 0)
        {
            return await _fallback.RecommendTemplateAsync(request, cancellationToken);
        }

        try
        {
            var prompt = BuildTemplatePrompt(request);
            var content = await CompleteJsonAsync(prompt, cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            Guid? templateId = null;
            if (root.TryGetProperty("templateId", out var templateProp)
                && templateProp.ValueKind == JsonValueKind.String
                && Guid.TryParse(templateProp.GetString(), out var parsedId))
            {
                templateId = parsedId;
            }

            if (templateId is null || request.Templates.All(x => x.TemplateId != templateId))
            {
                return await _fallback.RecommendTemplateAsync(request, cancellationToken);
            }

            var reason = root.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString()
                : null;
            var confidence = root.TryGetProperty("confidence", out var confidenceProp)
                ? confidenceProp.GetInt32()
                : 70;

            return new AiTemplateRecommendationResult
            {
                TemplateId = templateId,
                Reason = string.IsNullOrWhiteSpace(reason)
                    ? "Recommended from project template matching rules."
                    : reason.Trim(),
                Confidence = Math.Clamp(confidence, 0, 100)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI template recommendation failed; using rule-based fallback");
            return await _fallback.RecommendTemplateAsync(request, cancellationToken);
        }
    }

    public async Task<AiPersonalizationResult> ExtractPersonalizationAsync(
        AiClassificationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return await _fallback.ExtractPersonalizationAsync(request, cancellationToken);
        }

        try
        {
            var prompt =
                $"""
                Extract personalization fields only from provided data. Never invent values.
                Company: {request.CompanyName}
                Contact: {request.ContactName}
                Role: {request.ContactRole}
                Industry: {request.Industry}
                City: {request.City}
                Country: {request.Country}
                Email: {request.Email}
                Return JSON object with optional keys: CompanyName, ContactName, ContactRole, Industry, City, Country, Email.
                Omit unknown keys.
                """;
            var content = await CompleteJsonAsync(prompt, cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values[prop.Name] = value.Trim();
                    }
                }
            }

            if (values.Count == 0)
            {
                return await _fallback.ExtractPersonalizationAsync(request, cancellationToken);
            }

            return new AiPersonalizationResult { Values = values };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI personalization failed; using rule-based fallback");
            return await _fallback.ExtractPersonalizationAsync(request, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProspectCandidate>> StructureFromSourceTextAsync(
        AiSourceStructureRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return await _fallback.StructureFromSourceTextAsync(request, cancellationToken);
        }

        try
        {
            var allowedEmails = string.Join(", ", request.ExtractedEmails);
            var allowedPhones = string.Join(", ", request.ExtractedPhones);
            var prompt =
                $"""
                Structure business candidates from this page.
                A real business with a website is a valid candidate even when email or WhatsApp is missing.
                Use ONLY emails and phones from the allowed lists. Never invent contact details.
                Prefer one candidate per company.
                Merge only when there is strong evidence (same domain, phone, email, or same company+city with supporting evidence).
                Project: {request.ProjectName}
                Description: {request.ProjectDescription}
                Research instructions: {request.ResearchInstructions}
                Keywords: {string.Join(", ", request.Keywords)}
                Target roles: {string.Join(", ", request.TargetRoles)}
                Source URL: {request.SourceUrl}
                Hint company: {request.HintCompanyName}
                Hint country: {request.HintCountry}
                Hint industry: {request.HintIndustry}
                Allowed emails: {allowedEmails}
                Allowed phones: {allowedPhones}
                Page text:
                {TrimForPrompt(request.PageText)}
                Return JSON with a candidates array of objects using keys companyName, contactName, contactRole, email, phone, whatsApp, industry, country, city, companyDescription.
                Include the business even when email and phone are empty.
                """;
            var content = await CompleteJsonAsync(prompt, cancellationToken);
            var parsed = ParseStructuredCandidates(request, content);
            if (parsed.Count == 0)
            {
                return await _fallback.StructureFromSourceTextAsync(request, cancellationToken);
            }

            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI source structuring failed; using rule-based fallback");
            return await _fallback.StructureFromSourceTextAsync(request, cancellationToken);
        }
    }

    private static List<ProspectCandidate> ParseStructuredCandidates(AiSourceStructureRequest request, string content)
    {
        var result = new List<ProspectCandidate>();
        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var emailSet = new HashSet<string>(request.ExtractedEmails, StringComparer.OrdinalIgnoreCase);
        var phoneSet = new HashSet<string>(request.ExtractedPhones.Select(NormalizeDigits), StringComparer.Ordinal);

        foreach (var item in candidates.EnumerateArray())
        {
            var email = ReadString(item, "email");
            var phone = ReadString(item, "phone") ?? ReadString(item, "whatsApp");
            if (!string.IsNullOrWhiteSpace(email) && !emailSet.Contains(email))
            {
                email = null;
            }

            if (!string.IsNullOrWhiteSpace(phone) && !phoneSet.Contains(NormalizeDigits(phone)))
            {
                phone = null;
            }

            var companyName = ReadString(item, "companyName")
                ?? request.HintCompanyName
                ?? InferCompanyFromSourceUrl(request.SourceUrl);
            if (string.IsNullOrWhiteSpace(companyName))
            {
                continue;
            }

            var hasContact = !string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(phone);
            result.Add(new ProspectCandidate
            {
                CompanyName = companyName,
                Website = request.SourceUrl,
                Country = ReadString(item, "country") ?? request.HintCountry,
                City = ReadString(item, "city"),
                Industry = ReadString(item, "industry") ?? request.HintIndustry,
                CompanyDescription = ReadString(item, "companyDescription"),
                ContactName = ReadString(item, "contactName"),
                ContactRole = ReadString(item, "contactRole"),
                Email = email,
                Phone = phone,
                WhatsApp = ReadString(item, "whatsApp") ?? phone,
                SourceUrl = request.SourceUrl,
                SourceType = ProspectSourceType.Website,
                SourceName = request.SourceName ?? "Website crawler",
                ResearchNotes = hasContact
                    ? "Structured from crawled page content. Contact details were constrained to extracted source values."
                    : "Structured from crawled page content. Business discovered without email or phone on this page."
            });
        }

        return result;
    }

    private static string TrimForPrompt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Length <= 6000 ? text : text[..6000];
    }

    private static string NormalizeDigits(string value)
    {
        var chars = value.Where(char.IsDigit).ToArray();
        return new string(chars);
    }

    private static string? InferCompanyFromSourceUrl(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : parts[0];
    }

    private async Task<string> CompleteJsonAsync(string userPrompt, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("OpenAI");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        if (!string.IsNullOrWhiteSpace(_options.OrganizationId))
        {
            httpRequest.Headers.Add("OpenAI-Organization", _options.OrganizationId);
        }

        if (!string.IsNullOrWhiteSpace(_options.ProjectId))
        {
            httpRequest.Headers.Add("OpenAI-Project", _options.ProjectId);
        }

        var payload = new
        {
            model = _options.Model,
            temperature = 0,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You classify and structure research data only. Never invent companies, people, emails, phone numbers, WhatsApp numbers, websites, or source URLs. Never assume slightly different company names are different businesses when website, phone, email, location, or description evidence suggests they are the same. Deduplicate to one prospect per real business and preserve all useful sources/contacts by merging. If a field is unknown, return null. Respond with JSON only."
                },
                new { role = "user", content = userPrompt }
            }
        };

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI request failed with status {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("OpenAI returned an empty response.");
        }

        return content;
    }

    private static string BuildQualificationPrompt(AiClassificationRequest request)
    {
        return
            $"""
            Classify this prospect against the project. Do not invent missing contact details.
            Prefer fewer strong fits over weak volume. Reject excluded industries regardless of score.
            Reject schools, universities, colleges, education directories, software vendors, and directory sites when they are not the operating business.
            Do not award points for evidence that was not found. Do not invent operational problems.
            If research instructions include a scoring rubric, follow it for qualificationScore.
            HIGH fits are typically 75+, MEDIUM 50-74, LOW/REJECT below 50.
            A distinct operating business in an approved industry with weak contactability may be needsReview=true rather than isQualified=true.
            If evidence is weak that this is a distinct business versus a known variant, set needsReview=true.
            Project: {request.ProjectName}
            Description: {request.ProjectDescription}
            Research instructions: {request.ResearchInstructions}
            Keywords: {string.Join(", ", request.Keywords)}
            Excluded keywords: {string.Join(", ", request.ExcludedKeywords)}
            Countries: {string.Join(", ", request.Countries)}
            Industries: {string.Join(", ", request.Industries)}
            Excluded industries: {string.Join(", ", request.ExcludedIndustries)}
            Target roles: {string.Join(", ", request.TargetRoles)}
            Company: {request.CompanyName}
            Website: {request.Website}
            Country: {request.Country}
            City: {request.City}
            Industry: {request.Industry}
            Description: {request.CompanyDescription}
            Size: {request.CompanySize}
            Contact: {request.ContactName}
            Role: {request.ContactRole}
            Email: {request.Email}
            WhatsApp: {request.WhatsApp}
            Source URL: {request.SourceUrl}
            Notes: {request.ResearchNotes}
            Return JSON with:
            isRelevant (bool), isQualified (bool), needsReview (bool), qualificationScore (0-100),
            emailConfidenceScore (0-100 or null), whatsAppConfidenceScore (0-100 or null),
            industry (string|null), companySize (string|null), contactRole (string|null),
            summary (short explanation, no chain-of-thought).
            """;
    }

    private static string BuildTemplatePrompt(AiTemplateRecommendationRequest request)
    {
        var templates = string.Join(
            '\n',
            request.Templates.Select(x =>
                $"- {x.TemplateId} | {x.Name} | category={x.Category} | industry={x.MatchIndustry} | size={x.MatchCompanySize} | notes={x.MatchNotes}"));

        return
            $"""
            Recommend the best template for this prospect. Choose only from the provided template IDs.
            Company: {request.CompanyName}
            Industry: {request.Industry}
            Size: {request.CompanySize}
            Role: {request.ContactRole}
            Country: {request.Country}
            Templates:
            {templates}
            Return JSON with templateId, reason, confidence (0-100).
            """;
    }

    private static AiQualificationResult? ParseQualification(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        return new AiQualificationResult
        {
            IsRelevant = ReadBool(root, "isRelevant"),
            IsQualified = ReadBool(root, "isQualified"),
            NeedsReview = ReadBool(root, "needsReview"),
            QualificationScore = Math.Clamp(ReadInt(root, "qualificationScore", 0), 0, 100),
            EmailConfidenceScore = ReadNullableInt(root, "emailConfidenceScore"),
            WhatsAppConfidenceScore = ReadNullableInt(root, "whatsAppConfidenceScore"),
            Industry = ReadString(root, "industry"),
            CompanySize = ReadString(root, "companySize"),
            ContactRole = ReadString(root, "contactRole"),
            Summary = ReadString(root, "summary") ?? string.Empty
        };
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True;
    }

    private static int ReadInt(JsonElement root, string name, int fallback)
    {
        if (root.TryGetProperty(name, out var prop) && prop.TryGetInt32(out var value))
        {
            return value;
        }

        return fallback;
    }

    private static int? ReadNullableInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.TryGetInt32(out var value))
        {
            return Math.Clamp(value, 0, 100);
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return prop.GetString();
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

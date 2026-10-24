using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Ai;

public class OpenAiMapContactAssistant : IOpenAiMapContactAssistant
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiOptions _options;
    private readonly ILogger<OpenAiMapContactAssistant> _logger;

    public OpenAiMapContactAssistant(
        IHttpClientFactory httpClientFactory,
        IOptions<OpenAiOptions> options,
        ILogger<OpenAiMapContactAssistant> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task<OpenAiMapContactHintResult> SuggestContactTargetsAsync(
        OpenAiMapContactHintRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return new OpenAiMapContactHintResult
            {
                IsAvailable = false,
                ErrorSummary = "OpenAI is not configured."
            };
        }

        try
        {
            var prompt = BuildHintPrompt(request);
            var content = await CompleteJsonAsync(prompt, cancellationToken);
            return ParseHint(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI map contact hint failed for {Company}", request.CompanyName);
            return new OpenAiMapContactHintResult
            {
                IsAvailable = true,
                ErrorSummary = ex.Message
            };
        }
    }

    public async Task<OpenAiMapPageContactResult> ExtractContactsFromPageAsync(
        OpenAiMapPageContactRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return new OpenAiMapPageContactResult
            {
                IsAvailable = false,
                ErrorSummary = "OpenAI is not configured."
            };
        }

        try
        {
            var prompt = BuildPagePrompt(request);
            var content = await CompleteJsonAsync(prompt, cancellationToken);
            return ParsePage(content, request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI map page contact extraction failed for {Company}", request.CompanyName);
            return new OpenAiMapPageContactResult
            {
                IsAvailable = true,
                ErrorSummary = ex.Message
            };
        }
    }

    private static string BuildHintPrompt(OpenAiMapContactHintRequest request)
    {
        var missing = string.Join(", ", request.MissingFields);
        var countries = string.Join(", ", request.TargetCountries);
        return
            $"""
            Propose public discovery targets for this real business.
            CRITICAL RULE: Be 100% sure a website/contact page belongs to THIS exact business at THIS location. If you are not 100% certain, return null and do not suggest it.
            Do not invent, guess, approximate, or reuse contacts from a similar-looking business.
            Do not invent email or phone numbers in this step.
            Return only official website URLs and search queries that can find public contact pages.
            Company: {request.CompanyName}
            City: {request.City}
            Country: {request.Country}
            Industry: {request.Industry}
            Known website: {request.Website}
            Street address: {request.StreetAddress}
            Missing fields: {missing}
            Target countries: {countries}
            Return JSON with:
            officialWebsite (string|null),
            contactPageUrl (string|null),
            searchQueries (string array, max 4),
            reason (short string).
            If not 100% certain, return null for URLs and an empty searchQueries array.
            """;
    }

    private static string BuildPagePrompt(OpenAiMapPageContactRequest request)
    {
        var missing = string.Join(", ", request.MissingFields);
        var allowedEmails = string.Join(", ", request.AllowedEmails);
        var allowedPhones = string.Join(", ", request.AllowedPhones);
        var text = request.PageText ?? string.Empty;
        if (text.Length > 7000)
        {
            text = text[..7000];
        }

        return
            $"""
            Extract contact fields for this business from the supplied page text only.
            CRITICAL RULE: Return email/phone/website ONLY when you are 100% sure they are the correct public contacts for THIS exact business. If not 100% certain, return null for that field. Do not fetch or invent uncertain contacts.
            Never invent, guess, autocomplete, or repair partial contacts.
            Email/phone must appear verbatim in the page text or allowed lists and clearly belong to this company.
            Company: {request.CompanyName}
            Page URL: {request.PageUrl}
            Page title: {request.PageTitle}
            Missing fields: {missing}
            Allowed emails: {allowedEmails}
            Allowed phones: {allowedPhones}
            Page text:
            {text}
            Return JSON with email, phone, website, contactPageUrl, reason.
            Any field that is not 100% certain must be null.
            """;
    }

    private static OpenAiMapContactHintResult ParseHint(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var result = new OpenAiMapContactHintResult
        {
            IsAvailable = true,
            OfficialWebsite = ReadString(root, "officialWebsite"),
            ContactPageUrl = ReadString(root, "contactPageUrl"),
            Reason = ReadString(root, "reason")
        };

        if (root.TryGetProperty("searchQueries", out var queries) && queries.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in queries.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.SearchQueries.Add(value.Trim());
                }

                if (result.SearchQueries.Count >= 4)
                {
                    break;
                }
            }
        }

        return result;
    }

    private static OpenAiMapPageContactResult ParsePage(string content, OpenAiMapPageContactRequest request)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var email = ReadString(root, "email");
        var phone = ReadString(root, "phone");
        if (!IsAllowedEmail(email, request.AllowedEmails, request.PageText))
        {
            email = null;
        }

        if (!IsAllowedPhone(phone, request.AllowedPhones, request.PageText))
        {
            phone = null;
        }

        return new OpenAiMapPageContactResult
        {
            IsAvailable = true,
            Email = email,
            Phone = phone,
            Website = ReadString(root, "website"),
            ContactPageUrl = ReadString(root, "contactPageUrl"),
            Reason = ReadString(root, "reason")
        };
    }

    private static bool IsAllowedEmail(string? email, List<string> allowed, string? pageText)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        foreach (var item in allowed)
        {
            if (string.Equals(item, email, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(pageText)
               && pageText.Contains(email, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedPhone(string? phone, List<string> allowed, string? pageText)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var digits = DigitsOnly(phone);
        foreach (var item in allowed)
        {
            if (DigitsOnly(item) == digits)
            {
                return true;
            }
        }

        return !string.IsNullOrWhiteSpace(pageText)
               && (pageText.Contains(phone, StringComparison.OrdinalIgnoreCase)
                   || pageText.Contains(digits, StringComparison.Ordinal));
    }

    private static string DigitsOnly(string value)
    {
        var chars = new List<char>();
        foreach (var ch in value)
        {
            if (char.IsDigit(ch))
            {
                chars.Add(ch);
            }
        }

        return new string(chars.ToArray());
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = prop.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
                    content = "You help find public business contact discovery targets and extract contacts from supplied page text only. CRITICAL: return a contact value only when you are 100% sure it is correct for the exact business. If not 100% sure, return null and do not invent, guess, or approximate emails, phones, WhatsApp numbers, or websites. Respond with JSON only."
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
}

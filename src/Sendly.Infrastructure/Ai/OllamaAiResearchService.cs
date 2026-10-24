using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Ai;

public class OllamaAiResearchService : IAiResearchService, ILocalAiHealthService, ILocalAiQueryPlanner, ILocalAiSearchClassifier, ILocalAiResearchIntelligence
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaOptions _options;
    private readonly RuleBasedAiResearchService _fallback;
    private readonly ILogger<OllamaAiResearchService> _logger;

    public OllamaAiResearchService(
        IHttpClientFactory httpClientFactory,
        IOptions<OllamaOptions> options,
        RuleBasedAiResearchService fallback,
        ILogger<OllamaAiResearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _fallback = fallback;
        _logger = logger;
    }

    public bool IsAvailable => true;

    public async Task<LocalAiHealthResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var result = new LocalAiHealthResult
        {
            Model = _options.Model,
            BaseUrl = _options.BaseUrl
        };
        if (!_options.Enabled)
        {
            result.ErrorSummary = "Ollama is disabled in settings. Deterministic research remains available.";
            return result;
        }

        try
        {
            var client = CreateClient();
            using var response = await client.GetAsync("api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                result.ErrorSummary = $"Ollama health check failed with HTTP {(int)response.StatusCode}.";
                return result;
            }

            result.IsReachable = true;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            result.AvailableModels = ParseModelNames(body);
            result.ModelAvailable = ModelIsInstalled(result.AvailableModels, _options.Model);
            if (!result.ModelAvailable)
            {
                result.ErrorSummary = $"Ollama is reachable but model '{_options.Model}' is not installed. Run: ollama pull {_options.Model}";
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.ErrorSummary = $"Ollama is unreachable at {_options.BaseUrl}. Start it with: ollama serve";
            _logger.LogWarning(ex, "Ollama health check failed");
            return result;
        }
    }

    public async Task<LocalAiHealthResult> EnsureRunningAsync(CancellationToken cancellationToken)
    {
        var health = await CheckHealthAsync(cancellationToken);
        if (!_options.Enabled || health.ModelAvailable)
        {
            return health;
        }

        if (!health.IsReachable && IsLocalBaseUrl())
        {
            await TryStartOllamaServeAsync(cancellationToken);
            health = await WaitForHealthAsync(cancellationToken);
        }

        if (health.IsReachable && !health.ModelAvailable)
        {
            await TryPullModelAsync(cancellationToken);
            health = await CheckHealthAsync(cancellationToken);
        }

        return health;
    }

    private async Task<LocalAiHealthResult> WaitForHealthAsync(CancellationToken cancellationToken)
    {
        var health = await CheckHealthAsync(cancellationToken);
        for (var attempt = 0; attempt < 20 && !health.IsReachable; attempt++)
        {
            await Task.Delay(500, cancellationToken);
            health = await CheckHealthAsync(cancellationToken);
        }

        return health;
    }

    private bool IsLocalBaseUrl()
    {
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.IsLoopback
               || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private Task TryStartOllamaServeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(startInfo);
            _logger.LogInformation("Started Ollama serve for research run (model {Model})", _options.Model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not start Ollama automatically. Install Ollama and run: ollama serve");
        }

        return Task.CompletedTask;
    }

    private async Task TryPullModelAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = $"pull {_options.Model}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(startInfo);
            if (process is not null)
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            _logger.LogInformation("Pulled Ollama model {Model} for research run", _options.Model);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not pull Ollama model {Model} automatically", _options.Model);
        }
    }

    public async Task<IReadOnlyList<string>> GenerateSearchQueriesAsync(
        AiQueryGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        var prompt = BuildQueryPrompt(request);
        var content = await CompleteJsonAsync(prompt, QuerySchema, cancellationToken);
        if (content is null)
        {
            return [];
        }

        return ParseQueries(content, request);
    }

    public async Task<AiSearchResultClassification> ClassifySearchResultAsync(
        AiSearchResultClassificationRequest request,
        CancellationToken cancellationToken)
    {
        var prompt = BuildSearchResultPrompt(request);
        var content = await CompleteJsonAsync(prompt, SearchResultSchema, cancellationToken);
        if (content is null)
        {
            return new AiSearchResultClassification();
        }

        return ParseSearchResultClassification(content, request);
    }

    public async Task<AiBusinessVerification> VerifyBusinessAsync(
        AiBusinessVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var prompt = BuildBusinessVerificationPrompt(request);
        var content = await CompleteJsonAsync(prompt, BusinessVerificationSchema, cancellationToken);
        if (content is null)
        {
            return new AiBusinessVerification();
        }

        return ParseBusinessVerification(content, request);
    }

    public async Task<ResearchPlanResult> ProposeResearchPlanAsync(
        ResearchPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new ResearchPlanResult { ErrorSummary = "Ollama disabled." };
        }

        var prompt = BuildResearchPlanPrompt(request);
        var content = await CompleteJsonAsync(prompt, ResearchPlanSchema, cancellationToken, 1024);
        if (content is null)
        {
            return new ResearchPlanResult { ErrorSummary = "Ollama returned no plan." };
        }

        return ParseResearchPlan(content, request);
    }

    public async Task<AiBatchSearchClassificationResult> ClassifySearchResultsBatchAsync(
        AiBatchSearchClassificationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || request.Results.Count == 0)
        {
            return new AiBatchSearchClassificationResult();
        }

        var prompt = BuildBatchSearchResultPrompt(request);
        var content = await CompleteJsonAsync(prompt, BatchSearchResultSchema, cancellationToken, 1024);
        if (content is null)
        {
            return new AiBatchSearchClassificationResult();
        }

        return ParseBatchSearchClassification(content, request);
    }

    public async Task<AiEvidenceExtractionResult> ExtractBusinessEvidenceAsync(
        AiEvidenceExtractionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return new AiEvidenceExtractionResult();
        }

        var prompt = BuildEvidenceExtractionPrompt(request);
        var content = await CompleteJsonAsync(prompt, EvidenceExtractionSchema, cancellationToken, 768);
        if (content is null)
        {
            return new AiEvidenceExtractionResult();
        }

        return ParseEvidenceExtraction(content, request);
    }

    public async Task<AiQualificationResult> QualifyAsync(
        AiClassificationRequest request,
        CancellationToken cancellationToken)
    {
        var prompt = BuildQualificationPrompt(request);
        var content = await CompleteJsonAsync(prompt, QualificationSchema, cancellationToken);
        var parsed = content is null ? null : ParseQualification(content);
        return parsed ?? await _fallback.QualifyAsync(request, cancellationToken);
    }

    public Task<AiTemplateRecommendationResult> RecommendTemplateAsync(
        AiTemplateRecommendationRequest request,
        CancellationToken cancellationToken)
        => _fallback.RecommendTemplateAsync(request, cancellationToken);

    public Task<AiPersonalizationResult> ExtractPersonalizationAsync(
        AiClassificationRequest request,
        CancellationToken cancellationToken)
        => _fallback.ExtractPersonalizationAsync(request, cancellationToken);

    public async Task<IReadOnlyList<ProspectCandidate>> StructureFromSourceTextAsync(
        AiSourceStructureRequest request,
        CancellationToken cancellationToken)
    {
        var prompt = BuildStructurePrompt(request);
        var content = await CompleteJsonAsync(prompt, StructureSchema, cancellationToken);
        var parsed = content is null ? [] : ParseStructuredCandidates(request, content);
        if (parsed.Count == 0)
        {
            return await _fallback.StructureFromSourceTextAsync(request, cancellationToken);
        }

        return parsed;
    }

    private const string QuerySchema = """{"type":"object","properties":{"queries":{"type":"array","items":{"type":"string"}}},"required":["queries"]}""";

    private const string QualificationSchema = """{"type":"object","properties":{"isRelevant":{"type":"boolean"},"isQualified":{"type":"boolean"},"needsReview":{"type":"boolean"},"qualificationScore":{"type":"integer"},"emailConfidenceScore":{"type":["integer","null"]},"whatsAppConfidenceScore":{"type":["integer","null"]},"industry":{"type":["string","null"]},"companySize":{"type":["string","null"]},"contactRole":{"type":["string","null"]},"summary":{"type":"string"}},"required":["isRelevant","isQualified","needsReview","qualificationScore","summary"]}""";

    private const string StructureSchema = """{"type":"object","properties":{"candidates":{"type":"array","items":{"type":"object","properties":{"companyName":{"type":["string","null"]},"contactName":{"type":["string","null"]},"contactRole":{"type":["string","null"]},"email":{"type":["string","null"]},"phone":{"type":["string","null"]},"whatsApp":{"type":["string","null"]},"industry":{"type":["string","null"]},"country":{"type":["string","null"]},"city":{"type":["string","null"]},"companyDescription":{"type":["string","null"]}}}}},"required":["candidates"]}""";

    private const string SearchResultSchema = """{"type":"object","properties":{"decision":{"type":"string"},"businessType":{"type":["string","null"]},"countryEvidence":{"type":["string","null"]},"industryEvidence":{"type":["string","null"]},"confidence":{"type":"integer"},"reason":{"type":"string"},"evidenceQuotes":{"type":"array","items":{"type":"string"}},"needsVerification":{"type":"boolean"}},"required":["decision","confidence","reason","evidenceQuotes","needsVerification"]}""";

    private const string BusinessVerificationSchema = """{"type":"object","properties":{"isRealBusiness":{"type":"boolean"},"isOfficialWebsite":{"type":"boolean"},"matchesIndustry":{"type":"boolean"},"matchesCountry":{"type":"boolean"},"businessName":{"type":["string","null"]},"officialWebsite":{"type":["string","null"]},"country":{"type":["string","null"]},"city":{"type":["string","null"]},"industry":{"type":["string","null"]},"email":{"type":["string","null"]},"phone":{"type":["string","null"]},"contactPage":{"type":["string","null"]},"confidence":{"type":"integer"},"reason":{"type":"string"},"evidenceQuotes":{"type":"array","items":{"type":"string"}}},"required":["isRealBusiness","isOfficialWebsite","matchesIndustry","matchesCountry","confidence","reason","evidenceQuotes"]}""";

    private const string ResearchPlanSchema = """{"type":"object","properties":{"strategySummary":{"type":"string"},"abandonedPatterns":{"type":"array","items":{"type":"string"}},"actions":{"type":"array","items":{"type":"object","properties":{"actionType":{"type":"string"},"query":{"type":["string","null"]},"purpose":{"type":"string"},"targetIndustry":{"type":["string","null"]},"geography":{"type":["string","null"]},"expectedResult":{"type":["string","null"]},"reason":{"type":"string"},"preferredSource":{"type":["string","null"]},"targetDomainOrUrl":{"type":["string","null"]},"priority":{"type":"integer"},"addressesRequirements":{"type":"array","items":{"type":"string"}}},"required":["actionType","purpose","reason","priority"]}}},"required":["strategySummary","abandonedPatterns","actions"]}""";

    private const string BatchSearchResultSchema = """{"type":"object","properties":{"results":{"type":"array","items":{"type":"object","properties":{"index":{"type":"integer"},"decision":{"type":"string"},"businessType":{"type":["string","null"]},"countryEvidence":{"type":["string","null"]},"industryEvidence":{"type":["string","null"]},"confidence":{"type":"integer"},"reason":{"type":"string"},"evidenceQuotes":{"type":"array","items":{"type":"string"}},"needsVerification":{"type":"boolean"},"crawlPriority":{"type":"integer"}},"required":["index","decision","confidence","reason","evidenceQuotes","needsVerification","crawlPriority"]}}},"required":["results"]}""";

    private const string EvidenceExtractionSchema = """{"type":"object","properties":{"businessName":{"type":["string","null"]},"officialWebsite":{"type":["string","null"]},"country":{"type":["string","null"]},"city":{"type":["string","null"]},"industry":{"type":["string","null"]},"subindustry":{"type":["string","null"]},"description":{"type":["string","null"]},"email":{"type":["string","null"]},"phone":{"type":["string","null"]},"whatsApp":{"type":["string","null"]},"evidenceUrls":{"type":"array","items":{"type":"string"}},"evidenceQuotes":{"type":"array","items":{"type":"string"}},"missingFields":{"type":"array","items":{"type":"string"}},"uncertainties":{"type":"array","items":{"type":"string"}},"isRealBusiness":{"type":"boolean"},"isOfficialWebsite":{"type":"boolean"},"matchesIndustry":{"type":"boolean"},"matchesCountry":{"type":"boolean"},"confidence":{"type":"integer"},"reason":{"type":"string"}},"required":["isRealBusiness","isOfficialWebsite","matchesIndustry","matchesCountry","confidence","reason","evidenceQuotes","missingFields","uncertainties"]}""";

    private async Task<string?> CompleteJsonAsync(
        string prompt,
        string schema,
        CancellationToken cancellationToken,
        int? outputTokenOverride = null)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Model))
        {
            return null;
        }

        var boundedPrompt = TrimInput(prompt);
        var attempts = Math.Clamp(_options.MaxRetries, 0, 3) + 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                var client = CreateClient();
                var payload = BuildChatPayload(boundedPrompt, schema, outputTokenOverride);
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat");
                httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(httpRequest, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Ollama request failed with HTTP {Status}", (int)response.StatusCode);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var content = ExtractMessageContent(body);
                if (!string.IsNullOrWhiteSpace(content) && IsValidJson(content))
                {
                    return content;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama request failed; deterministic fallback remains available");
            }
        }

        return null;
    }

    private string BuildChatPayload(string prompt, string schema, int? outputTokenOverride = null)
    {
        using var schemaDoc = JsonDocument.Parse(schema);
        var outputTokens = outputTokenOverride ?? _options.MaxOutputTokens;
        var payload = new
        {
            model = _options.Model,
            stream = false,
            think = false,
            format = schemaDoc.RootElement.Clone(),
            options = new
            {
                num_predict = Math.Clamp(outputTokens, 64, 4096),
                temperature = 0
            },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Return JSON only. Never invent companies, people, emails, phones, websites or URLs. Obey project hard constraints exactly. Separate facts from inferences. If a field is unknown, return null. Do not include chain-of-thought."
                },
                new { role = "user", content = prompt }
            }
        };
        return JsonSerializer.Serialize(payload);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("Ollama");
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? "http://localhost:11434" : _options.BaseUrl.Trim();
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 300));
        return client;
    }

    private string TrimInput(string text)
    {
        var limit = Math.Clamp(_options.MaxInputCharacters, 500, 50000);
        return text.Length <= limit ? text : text[..limit];
    }

    private static string BuildSearchResultPrompt(AiSearchResultClassificationRequest request)
    {
        return $"""
            Classify this search result using ONLY the supplied title, snippet, URL and query. You did not inspect the page. Do not invent evidence.
            decision must be one of RelevantBusinessCandidate, PotentialDirectory, PotentialOfficialBusinessWebsite, Ambiguous, Irrelevant, WrongGeography, WrongIndustry, UnsupportedOrInvalidResult.
            evidenceQuotes must be exact short quotes copied from the title or snippet supplied below.
            Title: {request.Title}
            Snippet: {request.Snippet}
            URL: {request.Url}
            Query: {request.Query}
            Target industries: {string.Join(", ", request.Industries)}
            Excluded industries: {string.Join(", ", request.ExcludedIndustries)}
            Target countries: {string.Join(", ", request.Countries)}
            Target locations: {string.Join(", ", request.Locations)}
            Return JSON with decision, businessType, countryEvidence, industryEvidence, confidence, reason, evidenceQuotes, needsVerification.
            """;
    }

    private static string BuildBusinessVerificationPrompt(AiBusinessVerificationRequest request)
    {
        return $"""
            Verify this business using ONLY the supplied page title and page text. A keyword mention alone is not enough. Extract contact values only when they appear verbatim in the supplied text; otherwise return null.
            Candidate name: {request.CompanyName}
            URL: {request.Url}
            Page title: {request.PageTitle}
            Target industries: {string.Join(", ", request.Industries)}
            Target countries: {string.Join(", ", request.Countries)}
            Page text: {request.PageText}
            Return JSON with isRealBusiness, isOfficialWebsite, matchesIndustry, matchesCountry, businessName, officialWebsite, country, city, industry, email, phone, contactPage, confidence, reason, evidenceQuotes.
            """;
    }

    private static AiSearchResultClassification ParseSearchResultClassification(
        string content,
        AiSearchResultClassificationRequest request)
    {
        var result = new AiSearchResultClassification();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var decision = ReadString(root, "decision");
            if (!IsKnownDecision(decision))
            {
                return result;
            }

            var evidence = $"{request.Title} {request.Snippet}";
            var quotes = ReadQuotes(root, evidence);
            result.Decision = decision!;
            result.BusinessType = ReadString(root, "businessType");
            result.CountryEvidence = ReadString(root, "countryEvidence");
            result.IndustryEvidence = ReadString(root, "industryEvidence");
            result.Confidence = Math.Clamp(ReadInt(root, "confidence"), 0, 100);
            result.Reason = ReadString(root, "reason") ?? string.Empty;
            result.EvidenceQuotes = quotes;
            result.NeedsVerification = ReadBool(root, "needsVerification");
            result.IsValid = quotes.Count > 0 || decision is "Ambiguous" or "Irrelevant" or "WrongGeography" or "WrongIndustry";
            if (!result.IsValid)
            {
                result.Decision = "Ambiguous";
                result.NeedsVerification = true;
            }

            return result;
        }
        catch (JsonException)
        {
            return result;
        }
    }

    private static AiBusinessVerification ParseBusinessVerification(
        string content,
        AiBusinessVerificationRequest request)
    {
        var result = new AiBusinessVerification();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var evidence = $"{request.PageTitle} {request.PageText}";
            var quotes = ReadQuotes(root, evidence);
            result.IsRealBusiness = ReadBool(root, "isRealBusiness");
            result.IsOfficialWebsite = ReadBool(root, "isOfficialWebsite");
            result.MatchesIndustry = ReadBool(root, "matchesIndustry");
            result.MatchesCountry = ReadBool(root, "matchesCountry");
            result.BusinessName = ReadString(root, "businessName");
            result.OfficialWebsite = ReadSupportedUrl(root, "officialWebsite", request.Url);
            result.Country = ReadString(root, "country");
            result.City = ReadString(root, "city");
            result.Industry = ReadString(root, "industry");
            result.Email = ReadSupportedValue(root, "email", evidence);
            result.Phone = ReadSupportedValue(root, "phone", evidence);
            result.ContactPage = ReadSupportedUrl(root, "contactPage", request.Url);
            result.Confidence = Math.Clamp(ReadInt(root, "confidence"), 0, 100);
            result.Reason = ReadString(root, "reason") ?? string.Empty;
            result.EvidenceQuotes = quotes;
            result.IsValid = quotes.Count > 0;
            if (!result.IsValid)
            {
                result.IsRealBusiness = false;
                result.MatchesIndustry = false;
                result.MatchesCountry = false;
            }

            return result;
        }
        catch (JsonException)
        {
            return result;
        }
    }

    private static bool IsKnownDecision(string? decision)
    {
        return decision is "RelevantBusinessCandidate" or "PotentialDirectory" or "PotentialOfficialBusinessWebsite"
            or "Ambiguous" or "Irrelevant" or "WrongGeography" or "WrongIndustry" or "UnsupportedOrInvalidResult";
    }

    private static List<string> ReadQuotes(JsonElement root, string evidence)
    {
        var quotes = new List<string>();
        if (!root.TryGetProperty("evidenceQuotes", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return quotes;
        }

        foreach (var item in items.EnumerateArray())
        {
            var quote = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
            if (!string.IsNullOrWhiteSpace(quote)
                && evidence.Contains(quote, StringComparison.OrdinalIgnoreCase))
            {
                quotes.Add(quote);
            }
        }

        return quotes;
    }

    private static string? ReadSupportedValue(JsonElement root, string name, string evidence)
    {
        var value = ReadString(root, name);
        if (string.IsNullOrWhiteSpace(value) || !evidence.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    private static string? ReadSupportedUrl(JsonElement root, string name, string? fallback)
    {
        var value = ReadString(root, name);
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return fallback;
        }

        return value;
    }

    private IReadOnlyList<string> ParseQueries(string content, AiQueryGenerationRequest request)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in request.ExistingQueries)
        {
            if (!string.IsNullOrWhiteSpace(existing))
            {
                seen.Add(existing.Trim());
            }
        }

        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("queries", out var queries) || queries.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var max = Math.Clamp(request.MaxQueries, 1, Math.Clamp(_options.MaxQueriesPerRequest, 1, 50));
        foreach (var item in queries.EnumerateArray())
        {
            var query = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(query) || query.Length > 200 || !seen.Add(query))
            {
                continue;
            }

            var spec = new ResearchSpecification
            {
                ApprovedIndustries = request.Industries.ToList(),
                ExcludedIndustries = request.ExcludedIndustries.ToList(),
                ExcludedKeywords = request.ExcludedKeywords.ToList(),
                TargetCountries = request.Countries.ToList(),
                Cities = request.Locations.ToList(),
                IndustrySynonyms = request.IndustrySynonyms.ToList()
            };
            var validation = ProjectEligibilityPolicy.ValidateQuery(query, spec);
            if (!validation.IsValid)
            {
                continue;
            }

            result.Add(query);
            if (result.Count >= max)
            {
                break;
            }
        }

        return result;
    }

    private static List<string> ParseModelNames(string body)
    {
        var names = new List<string>();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (var model in models.EnumerateArray())
        {
            if (model.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                var value = name.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    names.Add(value);
                }
            }
        }

        return names;
    }

    private static bool ModelIsInstalled(List<string> available, string configured)
    {
        foreach (var name in available)
        {
            if (string.Equals(name, configured, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(configured + ":", StringComparison.OrdinalIgnoreCase)
                || configured.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ExtractMessageContent(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        return null;
    }

    private static bool IsValidJson(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildQueryPrompt(AiQueryGenerationRequest request)
    {
        return $"""
            Generate web search queries that strictly follow the project hard constraints. Every query must include an approved industry and the target country or city. Never invent companies. Never target excluded categories. Never change or broaden the country or industry.
            Project: {request.ProjectName}
            Description: {request.ProjectDescription}
            Approved industries only: {string.Join(", ", request.Industries)}
            Synonyms (optional helpers, not new industries): {string.Join(", ", request.IndustrySynonyms)}
            Target countries only: {string.Join(", ", request.Countries)}
            Locations: {string.Join(", ", request.Locations)}
            Keywords: {string.Join(", ", request.Keywords)}
            Excluded industries: {string.Join(", ", request.ExcludedIndustries)}
            Excluded keywords: {string.Join(", ", request.ExcludedKeywords)}
            Hard constraints: {request.HardConstraintsSummary}
            Coverage so far: {request.CoverageSummary}
            Rejection patterns: {request.RejectionSummary}
            Under-covered industries: {string.Join(", ", request.UnderCoveredIndustries)}
            Under-covered locations: {string.Join(", ", request.UnderCoveredLocations)}
            Avoid duplicates of: {string.Join(" | ", request.ExistingQueries.Take(30))}
            Return JSON with a queries array of strings, with at most {Math.Clamp(request.MaxQueries, 1, 50)} short queries.
            """;
    }

    private static string BuildResearchPlanPrompt(ResearchPlanRequest request)
    {
        var spec = request.Specification;
        var sources = string.Join(
            " | ",
            request.AvailableSources.Select(x =>
                $"{x.Name}({x.SourceKind},available={x.IsAvailable},health={x.Health},yield={x.UsefulResultYield})"));
        var mode = request.IsFollowUp
            ? "This is a follow-up plan. Prefer changing strategy based on outcomes. Abandon low-yield patterns. Do not repeat unsuccessful query shapes."
            : "This is the initial research plan. Propose a diverse, targeted set of actions for this project's actual configuration.";
        return $"""
            You are the research intelligence layer for a universal business research engine. Propose structured research actions only. C# will validate and execute them.
            {mode}
            Allowed actionType values only: {string.Join(", ", ResearchActionTypes.Allowed)}
            Use only available sources from the catalog. Never invent providers.
            Every search query must include an approved industry and target country or city. Never invent companies. Never target excluded categories. Never weaken hard constraints.
            Prefer expected value over volume. A missing public email does not invalidate an otherwise eligible business.
            Project: {request.ProjectName}
            Description: {request.ProjectDescription}
            Instructions: {request.ResearchInstructions}
            Target definition industries: {string.Join(", ", spec.ApprovedIndustries)}
            Synonyms: {string.Join(", ", spec.IndustrySynonyms)}
            Excluded industries: {string.Join(", ", spec.ExcludedIndustries)}
            Excluded entity types: {string.Join(", ", spec.ExcludedEntityTypes)}
            Excluded keywords: {string.Join(", ", spec.ExcludedKeywords)}
            Countries: {string.Join(", ", spec.TargetCountries)}
            Regions: {string.Join(", ", spec.Regions)}
            Cities: {string.Join(", ", spec.Cities)}
            Language hints: {string.Join(", ", spec.LanguageHints)}
            Required criteria: {string.Join(", ", spec.RequiredCriteria)}
            Mandatory gates: {string.Join(", ", spec.MandatoryGates)}
            Min score: {spec.MinQualificationScore}
            Prospect goal: {spec.TargetProspectCount}
            Available sources: {sources}
            Existing queries to avoid: {string.Join(" | ", request.ExistingQueries.Take(30))}
            Already abandoned patterns: {string.Join(" | ", request.AbandonedPatterns)}
            Outcome facts: {request.OutcomeSummary}
            Budget facts: {request.BudgetSummary}
            Coverage: {request.CoverageSummary}
            Rejections: {request.RejectionSummary}
            Under-covered industries: {string.Join(", ", request.UnderCoveredIndustries)}
            Under-covered locations: {string.Join(", ", request.UnderCoveredLocations)}
            Return at most {Math.Clamp(request.MaxActions, 1, 12)} actions with purpose, targetIndustry, geography, expectedResult, reason, preferredSource when useful, priority 1-100, and addressesRequirements.
            Also return abandonedPatterns for low-yield shapes that should not be repeated, and a short strategySummary.
            """;
    }

    private static string BuildBatchSearchResultPrompt(AiBatchSearchClassificationRequest request)
    {
        var rows = new StringBuilder();
        for (var i = 0; i < request.Results.Count; i++)
        {
            var item = request.Results[i];
            rows.AppendLine($"[{item.Index}] title={item.Title} | snippet={item.Snippet} | url={item.Url} | query={item.Query}");
        }

        return $"""
            Classify each search result using ONLY the supplied title, snippet, URL and query. You did not inspect the pages. Do not invent evidence.
            decision must be one of RelevantBusinessCandidate, PotentialDirectory, PotentialOfficialBusinessWebsite, Ambiguous, Irrelevant, WrongGeography, WrongIndustry, UnsupportedOrInvalidResult.
            evidenceQuotes must be exact short quotes copied from the supplied title or snippet for that row.
            crawlPriority is 1 (highest) to 5 (lowest). Prefer official business websites and strong industry+geography matches.
            Target industries: {string.Join(", ", request.Industries)}
            Excluded industries: {string.Join(", ", request.ExcludedIndustries)}
            Excluded keywords: {string.Join(", ", request.ExcludedKeywords)}
            Target countries: {string.Join(", ", request.Countries)}
            Target locations: {string.Join(", ", request.Locations)}
            Results:
            {rows}
            Return JSON with results array matching each index.
            """;
    }

    private string BuildEvidenceExtractionPrompt(AiEvidenceExtractionRequest request)
    {
        return $"""
            Extract and verify business evidence using ONLY the supplied page title and page text. Never invent names, websites, emails, phones, WhatsApp numbers, addresses, or quotes.
            Contacts may only come from the allowed email/phone lists or exact verbatim page text. If unsure, leave null and list the field in missingFields or uncertainties.
            Candidate name: {request.CompanyName}
            URL: {request.Url}
            Page title: {request.PageTitle}
            Target industries: {string.Join(", ", request.Industries)}
            Excluded industries: {string.Join(", ", request.ExcludedIndustries)}
            Target countries: {string.Join(", ", request.Countries)}
            Allowed emails: {string.Join(", ", request.AllowedEmails)}
            Allowed phones: {string.Join(", ", request.AllowedPhones)}
            Page text: {TrimInput(request.PageText ?? string.Empty)}
            Return JSON with businessName, officialWebsite, country, city, industry, subindustry, description, email, phone, whatsApp, evidenceUrls, evidenceQuotes, missingFields, uncertainties, isRealBusiness, isOfficialWebsite, matchesIndustry, matchesCountry, confidence, reason.
            """;
    }

    private ResearchPlanResult ParseResearchPlan(string content, ResearchPlanRequest request)
    {
        var result = new ResearchPlanResult();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            result.StrategySummary = ReadString(root, "strategySummary") ?? string.Empty;
            result.AbandonedPatterns = ReadStringArray(root, "abandonedPatterns");
            if (!root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
            {
                result.ErrorSummary = "Plan missing actions.";
                return result;
            }

            var max = Math.Clamp(request.MaxActions, 1, 12);
            foreach (var item in actions.EnumerateArray())
            {
                if (result.Actions.Count >= max)
                {
                    break;
                }

                var actionType = ReadString(item, "actionType");
                if (string.IsNullOrWhiteSpace(actionType))
                {
                    continue;
                }

                result.Actions.Add(new ResearchActionProposal
                {
                    ActionType = actionType,
                    Query = ReadString(item, "query"),
                    Purpose = ReadString(item, "purpose") ?? string.Empty,
                    TargetIndustry = ReadString(item, "targetIndustry"),
                    Geography = ReadString(item, "geography"),
                    ExpectedResult = ReadString(item, "expectedResult"),
                    Reason = ReadString(item, "reason") ?? string.Empty,
                    PreferredSource = ReadString(item, "preferredSource"),
                    TargetDomainOrUrl = ReadString(item, "targetDomainOrUrl"),
                    Priority = Math.Clamp(ReadInt(item, "priority"), 1, 100),
                    AddressesRequirements = ReadStringArray(item, "addressesRequirements")
                });
            }

            result.IsValid = result.Actions.Count > 0 || result.AbandonedPatterns.Count > 0;
            return result;
        }
        catch (JsonException)
        {
            result.ErrorSummary = "Plan JSON parse failed.";
            return result;
        }
    }

    private static AiBatchSearchClassificationResult ParseBatchSearchClassification(
        string content,
        AiBatchSearchClassificationRequest request)
    {
        var result = new AiBatchSearchClassificationResult();
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            var byIndex = request.Results.ToDictionary(x => x.Index);
            foreach (var item in items.EnumerateArray())
            {
                var index = ReadInt(item, "index");
                if (!byIndex.TryGetValue(index, out var source))
                {
                    continue;
                }

                var decision = ReadString(item, "decision");
                if (!IsKnownDecision(decision))
                {
                    continue;
                }

                var evidence = $"{source.Title} {source.Snippet}";
                var quotes = ReadQuotes(item, evidence);
                var classified = new AiBatchSearchClassificationItem
                {
                    Index = index,
                    Decision = decision!,
                    BusinessType = ReadString(item, "businessType"),
                    CountryEvidence = ReadString(item, "countryEvidence"),
                    IndustryEvidence = ReadString(item, "industryEvidence"),
                    Confidence = Math.Clamp(ReadInt(item, "confidence"), 0, 100),
                    Reason = ReadString(item, "reason") ?? string.Empty,
                    EvidenceQuotes = quotes,
                    NeedsVerification = ReadBool(item, "needsVerification"),
                    CrawlPriority = Math.Clamp(ReadInt(item, "crawlPriority") == 0 ? 3 : ReadInt(item, "crawlPriority"), 1, 5),
                    IsValid = quotes.Count > 0 || decision is "Ambiguous" or "Irrelevant" or "WrongGeography" or "WrongIndustry"
                };
                if (!classified.IsValid)
                {
                    classified.Decision = "Ambiguous";
                    classified.NeedsVerification = true;
                }

                result.Items.Add(classified);
            }

            result.IsValid = result.Items.Count > 0;
            return result;
        }
        catch (JsonException)
        {
            return result;
        }
    }

    private static AiEvidenceExtractionResult ParseEvidenceExtraction(
        string content,
        AiEvidenceExtractionRequest request)
    {
        var result = new AiEvidenceExtractionResult();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var evidence = $"{request.PageTitle} {request.PageText}";
            var quotes = ReadQuotes(root, evidence);
            var allowedEmails = new HashSet<string>(request.AllowedEmails, StringComparer.OrdinalIgnoreCase);
            var allowedPhones = new HashSet<string>(request.AllowedPhones.Select(NormalizeDigits), StringComparer.Ordinal);
            result.BusinessName = ReadString(root, "businessName");
            result.OfficialWebsite = ReadSupportedUrl(root, "officialWebsite", request.Url);
            result.Country = ReadString(root, "country");
            result.City = ReadString(root, "city");
            result.Industry = ReadString(root, "industry");
            result.Subindustry = ReadString(root, "subindustry");
            result.Description = ReadString(root, "description");
            result.Email = ConstrainContact(ReadString(root, "email"), evidence, allowedEmails, false);
            result.Phone = ConstrainContact(ReadString(root, "phone"), evidence, allowedPhones, true);
            result.WhatsApp = ConstrainContact(ReadString(root, "whatsApp"), evidence, allowedPhones, true) ?? result.Phone;
            result.EvidenceUrls = ReadUrlArray(root, "evidenceUrls");
            result.EvidenceQuotes = quotes;
            result.MissingFields = ReadStringArray(root, "missingFields");
            result.Uncertainties = ReadStringArray(root, "uncertainties");
            result.IsRealBusiness = ReadBool(root, "isRealBusiness");
            result.IsOfficialWebsite = ReadBool(root, "isOfficialWebsite");
            result.MatchesIndustry = ReadBool(root, "matchesIndustry");
            result.MatchesCountry = ReadBool(root, "matchesCountry");
            result.Confidence = Math.Clamp(ReadInt(root, "confidence"), 0, 100);
            result.Reason = ReadString(root, "reason") ?? string.Empty;
            result.IsValid = quotes.Count > 0;
            if (!result.IsValid)
            {
                result.IsRealBusiness = false;
                result.MatchesIndustry = false;
                result.MatchesCountry = false;
            }

            return result;
        }
        catch (JsonException)
        {
            return result;
        }
    }

    private static string? ConstrainContact(
        string? value,
        string evidence,
        HashSet<string> allowed,
        bool asPhone)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (asPhone)
        {
            var digits = NormalizeDigits(trimmed);
            if (allowed.Count > 0 && !allowed.Contains(digits))
            {
                return null;
            }

            return evidence.Contains(trimmed, StringComparison.OrdinalIgnoreCase) || allowed.Contains(digits)
                ? trimmed
                : null;
        }

        if (allowed.Count > 0 && !allowed.Contains(trimmed))
        {
            return null;
        }

        return evidence.Contains(trimmed, StringComparison.OrdinalIgnoreCase) || allowed.Contains(trimmed)
            ? trimmed
            : null;
    }

    private static List<string> ReadStringArray(JsonElement root, string name)
    {
        var values = new List<string>();
        if (!root.TryGetProperty(name, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var item in items.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static List<string> ReadUrlArray(JsonElement root, string name)
    {
        var values = new List<string>();
        foreach (var value in ReadStringArray(root, name))
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string BuildQualificationPrompt(AiClassificationRequest request)
    {
        return $"""
            Classify this prospect against the project. Do not invent missing details. Reject excluded industries.
            Project: {request.ProjectName}
            Description: {request.ProjectDescription}
            Instructions: {request.ResearchInstructions}
            Keywords: {string.Join(", ", request.Keywords)}
            Excluded keywords: {string.Join(", ", request.ExcludedKeywords)}
            Countries: {string.Join(", ", request.Countries)}
            Industries: {string.Join(", ", request.Industries)}
            Excluded industries: {string.Join(", ", request.ExcludedIndustries)}
            Company: {request.CompanyName}
            Website: {request.Website}
            Country: {request.Country}
            City: {request.City}
            Industry: {request.Industry}
            Description: {request.CompanyDescription}
            Email: {request.Email}
            Source: {request.SourceUrl}
            Notes: {request.ResearchNotes}
            Return JSON with isRelevant, isQualified, needsReview, qualificationScore (0-100), emailConfidenceScore (0-100 or null), whatsAppConfidenceScore (0-100 or null), industry, companySize, contactRole, summary.
            """;
    }

    private string BuildStructurePrompt(AiSourceStructureRequest request)
    {
        return $"""
            Structure business candidates from this page. Use ONLY emails and phones from the allowed lists. Never invent contact details. One candidate per company.
            Project: {request.ProjectName}
            Description: {request.ProjectDescription}
            Keywords: {string.Join(", ", request.Keywords)}
            Source URL: {request.SourceUrl}
            Hint company: {request.HintCompanyName}
            Hint country: {request.HintCountry}
            Hint industry: {request.HintIndustry}
            Allowed emails: {string.Join(", ", request.ExtractedEmails)}
            Allowed phones: {string.Join(", ", request.ExtractedPhones)}
            Page text: {TrimInput(request.PageText)}
            Return JSON with a candidates array. Each candidate may use keys companyName, contactName, contactRole, email, phone, whatsApp, industry, country, city, companyDescription. Use null for unknown values.
            """;
    }

    private static AiQualificationResult? ParseQualification(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            return new AiQualificationResult
            {
                IsRelevant = ReadBool(root, "isRelevant"),
                IsQualified = ReadBool(root, "isQualified"),
                NeedsReview = ReadBool(root, "needsReview"),
                QualificationScore = Math.Clamp(ReadInt(root, "qualificationScore"), 0, 100),
                EmailConfidenceScore = ReadNullableInt(root, "emailConfidenceScore"),
                WhatsAppConfidenceScore = ReadNullableInt(root, "whatsAppConfidenceScore"),
                Industry = ReadString(root, "industry"),
                CompanySize = ReadString(root, "companySize"),
                ContactRole = ReadString(root, "contactRole"),
                Summary = ReadString(root, "summary") ?? string.Empty
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<ProspectCandidate> ParseStructuredCandidates(AiSourceStructureRequest request, string content)
    {
        var result = new List<ProspectCandidate>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            var emails = new HashSet<string>(request.ExtractedEmails, StringComparer.OrdinalIgnoreCase);
            var phones = new HashSet<string>(request.ExtractedPhones.Select(NormalizeDigits), StringComparer.Ordinal);
            foreach (var item in candidates.EnumerateArray())
            {
                var companyName = ReadString(item, "companyName") ?? request.HintCompanyName;
                if (string.IsNullOrWhiteSpace(companyName))
                {
                    continue;
                }

                var email = ReadString(item, "email");
                if (!string.IsNullOrWhiteSpace(email) && !emails.Contains(email))
                {
                    email = null;
                }

                var phone = ReadString(item, "phone") ?? ReadString(item, "whatsApp");
                if (!string.IsNullOrWhiteSpace(phone) && !phones.Contains(NormalizeDigits(phone)))
                {
                    phone = null;
                }

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
                    ResearchNotes = "Structured by local Ollama model from crawled page content. Contact details were constrained to extracted source values."
                });
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return result;
    }

    private static string NormalizeDigits(string value) => new(value.Where(char.IsDigit).ToArray());

    private static bool ReadBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True;

    private static int ReadInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) && prop.TryGetInt32(out var value) ? value : 0;

    private static int? ReadNullableInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return prop.TryGetInt32(out var value) ? Math.Clamp(value, 0, 100) : null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = prop.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Application.Options;
using Sendly.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Services;

public class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ISendlyDbContext _db;
    private readonly OpenAiOptions _openAi;
    private readonly ResearchOptions _research;
    private readonly BraveSearchOptions _braveSearch;
    private readonly GooglePlacesOptions _googlePlaces;
    private readonly SearXngOptions _searXng;
    private readonly OllamaOptions _ollama;
    private readonly WhatsAppBusinessOptions _whatsApp;
    private readonly MicrosoftGraphOptions _microsoftGraph;
    private readonly EmailSendingOptions _emailSending;
    private readonly AttachmentOptions _attachments;
    private readonly BackgroundQueueOptions _queues;
    private readonly IBraveSearchSecretStore _braveSecretStore;
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(
        ISendlyDbContext db,
        IOptions<OpenAiOptions> openAi,
        IOptions<ResearchOptions> research,
        IOptions<BraveSearchOptions> braveSearch,
        IOptions<GooglePlacesOptions> googlePlaces,
        IOptions<SearXngOptions> searXng,
        IOptions<OllamaOptions> ollama,
        IOptions<WhatsAppBusinessOptions> whatsApp,
        IOptions<MicrosoftGraphOptions> microsoftGraph,
        IOptions<EmailSendingOptions> emailSending,
        IOptions<AttachmentOptions> attachments,
        IOptions<BackgroundQueueOptions> queues,
        IBraveSearchSecretStore braveSecretStore,
        ILogger<AppSettingsService> logger)
    {
        _db = db;
        _openAi = openAi.Value;
        _research = research.Value;
        _braveSearch = braveSearch.Value;
        _googlePlaces = googlePlaces.Value;
        _searXng = searXng.Value;
        _ollama = ollama.Value;
        _whatsApp = whatsApp.Value;
        _microsoftGraph = microsoftGraph.Value;
        _emailSending = emailSending.Value;
        _attachments = attachments.Value;
        _queues = queues.Value;
        _braveSecretStore = braveSecretStore;
        _logger = logger;
    }

    public async Task HydrateFromStoreAsync(CancellationToken cancellationToken)
    {
        LoadBraveApiKeyFromSecretStore();
        var entries = await _db.AppConfigurationEntries.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            ApplyStoredSection(entry.Section, entry.JsonPayload);
        }

        LoadBraveApiKeyFromSecretStore();
        SyncSearchProviderEnabledFlag();
        MigrateLegacySearchSettings();

        _logger.LogInformation("Hydrated {Count} app configuration sections from store", entries.Count);
    }

    public AppSettingsDto Get()
    {
        return new AppSettingsDto
        {
            OpenAi = MapOpenAi(),
            Research = MapResearch(),
            BraveSearch = MapBraveSearch(),
            GooglePlaces = MapGooglePlaces(),
            SearXng = MapSearXng(),
            Ollama = MapOllama(),
            WhatsApp = MapWhatsApp(),
            MicrosoftGraph = MapMicrosoftGraph(),
            EmailSending = MapEmailSending(),
            Attachments = MapAttachments(),
            BackgroundQueues = MapQueues()
        };
    }

    public async Task<AppSettingsDto> UpdateAsync(AppSettingsDto request, CancellationToken cancellationToken)
    {
        ApplyOpenAi(request.OpenAi);
        ApplyResearch(request.Research);
        ApplyBraveSearch(request.BraveSearch);
        if (request.GooglePlaces is not null)
        {
            ApplyGooglePlaces(request.GooglePlaces);
        }

        ApplySearXng(request.SearXng);
        ApplyOllama(request.Ollama);
        ApplyWhatsApp(request.WhatsApp);
        ApplyMicrosoftGraph(request.MicrosoftGraph);
        ApplyEmailSending(request.EmailSending);
        ApplyAttachments(request.Attachments);
        ApplyQueues(request.BackgroundQueues);
        SyncSearchProviderEnabledFlag();

        await PersistSectionAsync(OpenAiOptions.SectionName, BuildOpenAiStore(), cancellationToken);
        await PersistSectionAsync(ResearchOptions.SectionName, BuildResearchStore(), cancellationToken);
        await PersistSectionAsync(BraveSearchOptions.SectionName, BuildBraveSearchStore(), cancellationToken);
        await PersistSectionAsync(GooglePlacesOptions.SectionName, BuildGooglePlacesStore(), cancellationToken);
        await PersistSectionAsync(SearXngOptions.SectionName, BuildSearXngStore(), cancellationToken);
        await PersistSectionAsync(OllamaOptions.SectionName, BuildOllamaStore(), cancellationToken);
        await PersistSectionAsync(WhatsAppBusinessOptions.SectionName, BuildWhatsAppStore(), cancellationToken);
        await PersistSectionAsync(MicrosoftGraphOptions.SectionName, BuildMicrosoftGraphStore(), cancellationToken);
        await PersistSectionAsync(EmailSendingOptions.SectionName, BuildEmailSendingStore(), cancellationToken);
        await PersistSectionAsync(AttachmentOptions.SectionName, BuildAttachmentsStore(), cancellationToken);
        await PersistSectionAsync(BackgroundQueueOptions.SectionName, BuildQueuesStore(), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("App settings updated");
        return Get();
    }

    private async Task PersistSectionAsync(string section, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var existing = await _db.AppConfigurationEntries
            .FirstOrDefaultAsync(x => x.Section == section, cancellationToken);
        if (existing is null)
        {
            _db.AppConfigurationEntries.Add(new AppConfigurationEntry
            {
                Section = section,
                JsonPayload = json,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            return;
        }

        existing.JsonPayload = json;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private void ApplyStoredSection(string section, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        if (string.Equals(section, OpenAiOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<OpenAiSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyOpenAi(dto, allowBlankSecrets: false);
            }

            return;
        }

        if (string.Equals(section, ResearchOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<ResearchSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyResearch(dto, allowBlankSecrets: false);
            }

            return;
        }

        if (string.Equals(section, BraveSearchOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<BraveSearchSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyBraveSearchNonSecret(dto);
            }

            return;
        }

        if (string.Equals(section, GooglePlacesOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<GooglePlacesSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyGooglePlaces(dto, allowBlankSecrets: false);
            }

            return;
        }

        if (string.Equals(section, SearXngOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<SearXngSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplySearXng(dto, allowBlankSecrets: false);
            }

            return;
        }

        if (string.Equals(section, OllamaOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<OllamaSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyOllama(dto);
            }

            return;
        }

        if (string.Equals(section, WhatsAppBusinessOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<WhatsAppSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyWhatsApp(dto, allowBlankSecrets: false);
            }

            return;
        }

        if (string.Equals(section, MicrosoftGraphOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<MicrosoftGraphSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyMicrosoftGraph(dto, allowBlankSecrets: false);
            }

            return;
        }

        if (string.Equals(section, EmailSendingOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<EmailSendingSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyEmailSending(dto);
            }

            return;
        }

        if (string.Equals(section, AttachmentOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<AttachmentSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyAttachments(dto);
            }

            return;
        }

        if (string.Equals(section, BackgroundQueueOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var dto = JsonSerializer.Deserialize<BackgroundQueueSettingsDto>(json, JsonOptions);
            if (dto is not null)
            {
                ApplyQueues(dto);
            }
        }
    }

    private OpenAiSettingsDto MapOpenAi()
    {
        return new OpenAiSettingsDto
        {
            ApiKey = null,
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(_openAi.ApiKey),
            Model = _openAi.Model,
            OrganizationId = _openAi.OrganizationId,
            ProjectId = _openAi.ProjectId,
            BaseUrl = _openAi.BaseUrl,
            TimeoutSeconds = _openAi.TimeoutSeconds
        };
    }

    private ResearchSettingsDto MapResearch()
    {
        return new ResearchSettingsDto
        {
            MaxCandidatesPerRun = _research.MaxCandidatesPerRun,
            DefaultMinQualificationScore = _research.DefaultMinQualificationScore,
            DefaultRequestedProspectCount = _research.DefaultRequestedProspectCount,
            ProviderTimeoutSeconds = _research.ProviderTimeoutSeconds,
            EnableSeedProvider = _research.EnableSeedProvider,
            EnableWebsiteCrawler = _research.EnableWebsiteCrawler,
            EnableSearchProvider = _research.EnableSearchProvider,
            EnableIndependentCrawler = _research.EnableIndependentCrawler,
            EnableHtmlSearchFallback = _research.EnableHtmlSearchFallback,
            MaxPagesPerCrawl = _research.MaxPagesPerCrawl,
            MaxSearchResults = _research.MaxSearchResults,
            MaxSearchQueries = _research.MaxSearchQueries,
            MaxSearchRequests = _research.MaxSearchRequests,
            MaxResearchDurationMinutes = _research.MaxResearchDurationMinutes,
            DiscoveryOversampleFactor = _research.DiscoveryOversampleFactor,
            MinSearchRelevanceScore = _research.MinSearchRelevanceScore,
            BingSearchApiKey = null,
            BingSearchApiKeyConfigured = !string.IsNullOrWhiteSpace(_research.BingSearchApiKey),
            BingSearchEndpoint = _research.BingSearchEndpoint,
            GoogleSearchApiKey = null,
            GoogleSearchApiKeyConfigured = !string.IsNullOrWhiteSpace(_research.GoogleSearchApiKey),
            GoogleSearchEngineId = _research.GoogleSearchEngineId,
            HostDenylist = [.._research.HostDenylist]
        };
    }

    private BraveSearchSettingsDto MapBraveSearch()
    {
        var configured = !string.IsNullOrWhiteSpace(_braveSearch.ApiKey);
        return new BraveSearchSettingsDto
        {
            Enabled = _braveSearch.Enabled,
            ApiKey = null,
            ApiKeyConfigured = configured,
            BaseUrl = _braveSearch.BaseUrl,
            TimeoutSeconds = _braveSearch.TimeoutSeconds,
            MaxResultsPerQuery = _braveSearch.MaxResultsPerQuery,
            MaxRequestsPerResearchRun = _braveSearch.MaxRequestsPerResearchRun,
            RequestDelayMilliseconds = _braveSearch.RequestDelayMilliseconds,
            ConnectionStatus = configured ? "Configured" : "NotConfigured"
        };
    }

    private GooglePlacesSettingsDto MapGooglePlaces()
    {
        var configured = !string.IsNullOrWhiteSpace(_googlePlaces.ApiKey);
        return new GooglePlacesSettingsDto
        {
            Enabled = _googlePlaces.Enabled,
            ApiKey = null,
            ApiKeyConfigured = configured,
            BaseUrl = _googlePlaces.BaseUrl,
            TimeoutSeconds = _googlePlaces.TimeoutSeconds,
            MaxResultsPerQuery = _googlePlaces.MaxResultsPerQuery,
            MaxQueriesPerRequest = _googlePlaces.MaxQueriesPerRequest,
            RequestDelayMilliseconds = _googlePlaces.RequestDelayMilliseconds,
            ConnectionStatus = configured
                ? (_googlePlaces.Enabled ? "Configured" : "Disabled")
                : "NotConfigured"
        };
    }

    private SearXngSettingsDto MapSearXng()
    {
        return new SearXngSettingsDto
        {
            Enabled = false,
            BaseUrl = _searXng.BaseUrl,
            DefaultLanguage = _searXng.DefaultLanguage,
            RequestTimeoutSeconds = _searXng.RequestTimeoutSeconds,
            DefaultResultsPerQuery = _searXng.DefaultResultsPerQuery,
            MaximumPagesPerQuery = _searXng.MaximumPagesPerQuery,
            MaxRequestsPerResearchRun = _searXng.MaxRequestsPerResearchRun,
            RequestDelayMilliseconds = _searXng.RequestDelayMilliseconds,
            AllowPrivateNetworkHosts = _searXng.AllowPrivateNetworkHosts,
            BasicAuthUsername = null,
            BasicAuthPassword = null,
            BasicAuthConfigured = !string.IsNullOrWhiteSpace(_searXng.BasicAuthUsername),
            ConnectionStatus = "Disabled"
        };
    }

    private OllamaSettingsDto MapOllama()
    {
        return new OllamaSettingsDto
        {
            Enabled = _ollama.Enabled,
            BaseUrl = _ollama.BaseUrl,
            Model = _ollama.Model,
            TimeoutSeconds = _ollama.TimeoutSeconds,
            MaxRetries = _ollama.MaxRetries,
            ConnectionStatus = _ollama.Enabled ? "NotChecked" : "Disabled"
        };
    }

    private void ApplyOllama(OllamaSettingsDto dto)
    {
        _ollama.Enabled = dto.Enabled;
        if (!string.IsNullOrWhiteSpace(dto.BaseUrl))
        {
            _ollama.BaseUrl = dto.BaseUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.Model))
        {
            _ollama.Model = dto.Model.Trim();
        }

        _ollama.TimeoutSeconds = Math.Clamp(dto.TimeoutSeconds, 5, 300);
        _ollama.MaxRetries = Math.Clamp(dto.MaxRetries, 0, 3);
    }

    private object BuildOllamaStore()
    {
        return new
        {
            enabled = _ollama.Enabled,
            baseUrl = _ollama.BaseUrl,
            model = _ollama.Model,
            timeoutSeconds = _ollama.TimeoutSeconds,
            maxRetries = _ollama.MaxRetries,
            maxInputCharacters = _ollama.MaxInputCharacters,
            maxQueriesPerRequest = _ollama.MaxQueriesPerRequest
        };
    }

    private WhatsAppSettingsDto MapWhatsApp()
    {
        return new WhatsAppSettingsDto
        {
            AccessToken = null,
            AccessTokenConfigured = !string.IsNullOrWhiteSpace(_whatsApp.AccessToken),
            PhoneNumberId = _whatsApp.PhoneNumberId,
            ApiVersion = _whatsApp.ApiVersion,
            BaseUrl = _whatsApp.BaseUrl,
            DefaultTemplateLanguage = _whatsApp.DefaultTemplateLanguage,
            PreferTemplateMessages = _whatsApp.PreferTemplateMessages,
            TimeoutSeconds = _whatsApp.TimeoutSeconds
        };
    }

    private MicrosoftGraphSettingsDto MapMicrosoftGraph()
    {
        return new MicrosoftGraphSettingsDto
        {
            ClientId = _microsoftGraph.ClientId,
            ClientSecret = null,
            ClientSecretConfigured = !string.IsNullOrWhiteSpace(_microsoftGraph.ClientSecret),
            TenantId = _microsoftGraph.TenantId,
            RedirectUri = _microsoftGraph.RedirectUri,
            FrontendSuccessUri = _microsoftGraph.FrontendSuccessUri,
            FrontendErrorUri = _microsoftGraph.FrontendErrorUri
        };
    }

    private EmailSendingSettingsDto MapEmailSending()
    {
        return new EmailSendingSettingsDto
        {
            DefaultDelayMilliseconds = _emailSending.DefaultDelayMilliseconds,
            DefaultBatchSize = _emailSending.DefaultBatchSize,
            DefaultMaxRetryCount = _emailSending.DefaultMaxRetryCount,
            ConnectionTimeoutSeconds = _emailSending.ConnectionTimeoutSeconds
        };
    }

    private AttachmentSettingsDto MapAttachments()
    {
        return new AttachmentSettingsDto
        {
            MaxFileSizeBytes = _attachments.MaxFileSizeBytes,
            MaxTotalSizeBytes = _attachments.MaxTotalSizeBytes,
            AllowedContentTypes = [.._attachments.AllowedContentTypes],
            StorageRoot = _attachments.StorageRoot
        };
    }

    private BackgroundQueueSettingsDto MapQueues()
    {
        return new BackgroundQueueSettingsDto
        {
            Provider = _queues.Provider
        };
    }

    private void ApplyOpenAi(OpenAiSettingsDto dto, bool allowBlankSecrets = true)
    {
        if (!string.IsNullOrWhiteSpace(dto.ApiKey) || (!allowBlankSecrets && dto.ApiKey is not null))
        {
            if (!string.IsNullOrWhiteSpace(dto.ApiKey))
            {
                _openAi.ApiKey = dto.ApiKey.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(dto.Model))
        {
            _openAi.Model = dto.Model.Trim();
        }

        _openAi.OrganizationId = TrimOrNull(dto.OrganizationId);
        _openAi.ProjectId = TrimOrNull(dto.ProjectId);
        if (!string.IsNullOrWhiteSpace(dto.BaseUrl))
        {
            _openAi.BaseUrl = dto.BaseUrl.Trim();
        }

        _openAi.TimeoutSeconds = Math.Clamp(dto.TimeoutSeconds, 5, 300);
    }

    private void ApplyResearch(ResearchSettingsDto dto, bool allowBlankSecrets = true)
    {
        _research.MaxCandidatesPerRun = Math.Clamp(dto.MaxCandidatesPerRun, 1, 1000);
        _research.DefaultMinQualificationScore = Math.Clamp(dto.DefaultMinQualificationScore, 0, 100);
        _research.DefaultRequestedProspectCount = Math.Clamp(dto.DefaultRequestedProspectCount, 1, 500);
        _research.ProviderTimeoutSeconds = Math.Clamp(dto.ProviderTimeoutSeconds, 5, 120);
        _research.EnableSeedProvider = dto.EnableSeedProvider;
        _research.EnableWebsiteCrawler = dto.EnableWebsiteCrawler;
        _research.EnableSearchProvider = dto.EnableSearchProvider;
        _research.EnableIndependentCrawler = dto.EnableIndependentCrawler;
        _research.EnableHtmlSearchFallback = dto.EnableHtmlSearchFallback;
        _research.MaxPagesPerCrawl = Math.Clamp(dto.MaxPagesPerCrawl, 1, 50);
        _research.MaxSearchResults = Math.Clamp(dto.MaxSearchResults, 1, 50);
        _research.MaxSearchQueries = Math.Clamp(dto.MaxSearchQueries, 1, 200);
        _research.MaxSearchRequests = Math.Clamp(dto.MaxSearchRequests, 1, 500);
        _research.MaxResearchDurationMinutes = Math.Clamp(dto.MaxResearchDurationMinutes, 1, 240);
        _research.DiscoveryOversampleFactor = Math.Clamp(dto.DiscoveryOversampleFactor, 1, 10);
        _research.MinSearchRelevanceScore = Math.Max(0, dto.MinSearchRelevanceScore);
        if (!string.IsNullOrWhiteSpace(dto.BingSearchApiKey))
        {
            _research.BingSearchApiKey = dto.BingSearchApiKey.Trim();
        }
        else if (!allowBlankSecrets && dto.BingSearchApiKey is not null)
        {
            _research.BingSearchApiKey = dto.BingSearchApiKey;
        }

        _research.BingSearchEndpoint = TrimOrNull(dto.BingSearchEndpoint)
            ?? "https://api.bing.microsoft.com/v7.0/search";
        if (!string.IsNullOrWhiteSpace(dto.GoogleSearchApiKey))
        {
            _research.GoogleSearchApiKey = dto.GoogleSearchApiKey.Trim();
        }

        _research.GoogleSearchEngineId = TrimOrNull(dto.GoogleSearchEngineId);
        _research.HostDenylist = NormalizeList(dto.HostDenylist);
    }

    private void ApplyBraveSearch(BraveSearchSettingsDto dto)
    {
        ApplyBraveSearchNonSecret(dto);
        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
        {
            var key = dto.ApiKey.Trim();
            _braveSearch.ApiKey = key;
            _braveSecretStore.WriteApiKey(key);
        }
    }

    private void ApplyBraveSearchNonSecret(BraveSearchSettingsDto dto)
    {
        _braveSearch.Enabled = dto.Enabled;
        if (!string.IsNullOrWhiteSpace(dto.BaseUrl))
        {
            _braveSearch.BaseUrl = dto.BaseUrl.Trim();
        }

        _braveSearch.TimeoutSeconds = Math.Clamp(dto.TimeoutSeconds, 5, 120);
        _braveSearch.MaxResultsPerQuery = Math.Clamp(dto.MaxResultsPerQuery, 1, 20);
        _braveSearch.MaxRequestsPerResearchRun = Math.Clamp(dto.MaxRequestsPerResearchRun, 1, 100);
        _braveSearch.RequestDelayMilliseconds = Math.Clamp(dto.RequestDelayMilliseconds, 0, 10000);
    }

    private void ApplyGooglePlaces(GooglePlacesSettingsDto dto, bool allowBlankSecrets = true)
    {
        _googlePlaces.Enabled = dto.Enabled;
        if (!string.IsNullOrWhiteSpace(dto.BaseUrl))
        {
            _googlePlaces.BaseUrl = dto.BaseUrl.Trim();
        }

        _googlePlaces.TimeoutSeconds = Math.Clamp(dto.TimeoutSeconds, 5, 120);
        _googlePlaces.MaxResultsPerQuery = Math.Clamp(dto.MaxResultsPerQuery, 1, 20);
        _googlePlaces.MaxQueriesPerRequest = Math.Clamp(dto.MaxQueriesPerRequest, 1, 8);
        _googlePlaces.RequestDelayMilliseconds = Math.Clamp(dto.RequestDelayMilliseconds, 0, 10000);

        if (!string.IsNullOrWhiteSpace(dto.ApiKey))
        {
            _googlePlaces.ApiKey = dto.ApiKey.Trim();
        }
        else if (allowBlankSecrets && dto.ApiKey is not null && dto.ApiKey.Length == 0)
        {
            _googlePlaces.ApiKey = null;
        }
    }

    private void ApplySearXng(SearXngSettingsDto dto, bool allowBlankSecrets = true)
    {
        _searXng.Enabled = false;
        _searXng.BaseUrl = string.Empty;

        if (!string.IsNullOrWhiteSpace(dto.DefaultLanguage))
        {
            _searXng.DefaultLanguage = dto.DefaultLanguage.Trim();
        }

        _searXng.RequestTimeoutSeconds = Math.Clamp(dto.RequestTimeoutSeconds, 5, 120);
        _searXng.DefaultResultsPerQuery = Math.Clamp(dto.DefaultResultsPerQuery, 1, 50);
        _searXng.MaximumPagesPerQuery = Math.Clamp(dto.MaximumPagesPerQuery, 1, 20);
        _searXng.MaxRequestsPerResearchRun = Math.Clamp(dto.MaxRequestsPerResearchRun, 1, 500);
        _searXng.RequestDelayMilliseconds = Math.Clamp(dto.RequestDelayMilliseconds, 0, 10000);
        _searXng.AllowPrivateNetworkHosts = dto.AllowPrivateNetworkHosts;

        if (!string.IsNullOrWhiteSpace(dto.BasicAuthUsername))
        {
            _searXng.BasicAuthUsername = dto.BasicAuthUsername.Trim();
        }
        else if (allowBlankSecrets && dto.BasicAuthUsername is not null && dto.BasicAuthUsername.Length == 0)
        {
            _searXng.BasicAuthUsername = null;
            _searXng.BasicAuthPassword = null;
        }

        if (!string.IsNullOrWhiteSpace(dto.BasicAuthPassword))
        {
            _searXng.BasicAuthPassword = dto.BasicAuthPassword;
        }
    }

    private void SyncSearchProviderEnabledFlag()
    {
        _searXng.Enabled = false;
        _searXng.BaseUrl = string.Empty;
    }

    private void MigrateLegacySearchSettings()
    {
        if (_research.EnableSearchProvider || _research.EnableHtmlSearchFallback)
        {
            return;
        }

        _research.EnableSearchProvider = true;
        _research.EnableHtmlSearchFallback = true;
        _logger.LogInformation(
            "Migrated legacy research settings: SearXNG is disabled and zero-budget public HTML search is enabled");
    }

    private void LoadBraveApiKeyFromSecretStore()
    {
        if (!string.IsNullOrWhiteSpace(_braveSearch.ApiKey))
        {
            return;
        }

        var stored = _braveSecretStore.ReadApiKey();
        if (!string.IsNullOrWhiteSpace(stored))
        {
            _braveSearch.ApiKey = stored;
        }
    }

    private void ApplyWhatsApp(WhatsAppSettingsDto dto, bool allowBlankSecrets = true)
    {
        if (!string.IsNullOrWhiteSpace(dto.AccessToken))
        {
            _whatsApp.AccessToken = dto.AccessToken.Trim();
        }
        else if (!allowBlankSecrets && dto.AccessToken is not null)
        {
            _whatsApp.AccessToken = dto.AccessToken;
        }

        _whatsApp.PhoneNumberId = dto.PhoneNumberId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dto.ApiVersion))
        {
            _whatsApp.ApiVersion = dto.ApiVersion.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.BaseUrl))
        {
            _whatsApp.BaseUrl = dto.BaseUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.DefaultTemplateLanguage))
        {
            _whatsApp.DefaultTemplateLanguage = dto.DefaultTemplateLanguage.Trim();
        }

        _whatsApp.PreferTemplateMessages = dto.PreferTemplateMessages;
        _whatsApp.TimeoutSeconds = Math.Clamp(dto.TimeoutSeconds, 5, 120);
    }

    private void ApplyMicrosoftGraph(MicrosoftGraphSettingsDto dto, bool allowBlankSecrets = true)
    {
        _microsoftGraph.ClientId = dto.ClientId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dto.ClientSecret))
        {
            _microsoftGraph.ClientSecret = dto.ClientSecret.Trim();
        }
        else if (!allowBlankSecrets && dto.ClientSecret is not null)
        {
            _microsoftGraph.ClientSecret = dto.ClientSecret;
        }

        if (!string.IsNullOrWhiteSpace(dto.TenantId))
        {
            _microsoftGraph.TenantId = dto.TenantId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.RedirectUri))
        {
            _microsoftGraph.RedirectUri = dto.RedirectUri.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.FrontendSuccessUri))
        {
            _microsoftGraph.FrontendSuccessUri = dto.FrontendSuccessUri.Trim();
        }

        if (!string.IsNullOrWhiteSpace(dto.FrontendErrorUri))
        {
            _microsoftGraph.FrontendErrorUri = dto.FrontendErrorUri.Trim();
        }
    }

    private void ApplyEmailSending(EmailSendingSettingsDto dto)
    {
        _emailSending.DefaultDelayMilliseconds = Math.Clamp(dto.DefaultDelayMilliseconds, 0, 60000);
        _emailSending.DefaultBatchSize = Math.Clamp(dto.DefaultBatchSize, 1, 500);
        _emailSending.DefaultMaxRetryCount = Math.Clamp(dto.DefaultMaxRetryCount, 0, 10);
        _emailSending.ConnectionTimeoutSeconds = Math.Clamp(dto.ConnectionTimeoutSeconds, 5, 120);
    }

    private void ApplyAttachments(AttachmentSettingsDto dto)
    {
        _attachments.MaxFileSizeBytes = Math.Clamp(dto.MaxFileSizeBytes, 1024, 100L * 1024 * 1024);
        _attachments.MaxTotalSizeBytes = Math.Clamp(dto.MaxTotalSizeBytes, 1024, 500L * 1024 * 1024);
        var types = NormalizeList(dto.AllowedContentTypes);
        if (types.Count > 0)
        {
            _attachments.AllowedContentTypes = types.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(dto.StorageRoot))
        {
            _attachments.StorageRoot = dto.StorageRoot.Trim();
        }
    }

    private void ApplyQueues(BackgroundQueueSettingsDto dto)
    {
        var provider = string.IsNullOrWhiteSpace(dto.Provider) ? "InMemory" : dto.Provider.Trim();
        if (!string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "Ef", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, "Durable", StringComparison.OrdinalIgnoreCase))
        {
            provider = "InMemory";
        }

        _queues.Provider = provider;
    }

    private object BuildOpenAiStore()
    {
        return new
        {
            apiKey = _openAi.ApiKey,
            model = _openAi.Model,
            organizationId = _openAi.OrganizationId,
            projectId = _openAi.ProjectId,
            baseUrl = _openAi.BaseUrl,
            timeoutSeconds = _openAi.TimeoutSeconds
        };
    }

    private object BuildResearchStore()
    {
        return new
        {
            maxCandidatesPerRun = _research.MaxCandidatesPerRun,
            defaultMinQualificationScore = _research.DefaultMinQualificationScore,
            defaultRequestedProspectCount = _research.DefaultRequestedProspectCount,
            providerTimeoutSeconds = _research.ProviderTimeoutSeconds,
            enableSeedProvider = _research.EnableSeedProvider,
            enableWebsiteCrawler = _research.EnableWebsiteCrawler,
            enableSearchProvider = _research.EnableSearchProvider,
            enableIndependentCrawler = _research.EnableIndependentCrawler,
            enableHtmlSearchFallback = _research.EnableHtmlSearchFallback,
            maxPagesPerCrawl = _research.MaxPagesPerCrawl,
            maxSearchResults = _research.MaxSearchResults,
            maxSearchQueries = _research.MaxSearchQueries,
            maxSearchRequests = _research.MaxSearchRequests,
            maxResearchDurationMinutes = _research.MaxResearchDurationMinutes,
            discoveryOversampleFactor = _research.DiscoveryOversampleFactor,
            minSearchRelevanceScore = _research.MinSearchRelevanceScore,
            bingSearchApiKey = _research.BingSearchApiKey,
            bingSearchEndpoint = _research.BingSearchEndpoint,
            googleSearchApiKey = _research.GoogleSearchApiKey,
            googleSearchEngineId = _research.GoogleSearchEngineId,
            hostDenylist = _research.HostDenylist
        };
    }

    private object BuildBraveSearchStore()
    {
        return new
        {
            enabled = _braveSearch.Enabled,
            baseUrl = _braveSearch.BaseUrl,
            timeoutSeconds = _braveSearch.TimeoutSeconds,
            maxResultsPerQuery = _braveSearch.MaxResultsPerQuery,
            maxRequestsPerResearchRun = _braveSearch.MaxRequestsPerResearchRun,
            requestDelayMilliseconds = _braveSearch.RequestDelayMilliseconds
        };
    }

    private object BuildGooglePlacesStore()
    {
        return new
        {
            enabled = _googlePlaces.Enabled,
            apiKey = _googlePlaces.ApiKey,
            baseUrl = _googlePlaces.BaseUrl,
            timeoutSeconds = _googlePlaces.TimeoutSeconds,
            maxResultsPerQuery = _googlePlaces.MaxResultsPerQuery,
            maxQueriesPerRequest = _googlePlaces.MaxQueriesPerRequest,
            requestDelayMilliseconds = _googlePlaces.RequestDelayMilliseconds
        };
    }

    private object BuildSearXngStore()
    {
        return new
        {
            enabled = _searXng.Enabled,
            baseUrl = _searXng.BaseUrl,
            defaultLanguage = _searXng.DefaultLanguage,
            requestTimeoutSeconds = _searXng.RequestTimeoutSeconds,
            defaultResultsPerQuery = _searXng.DefaultResultsPerQuery,
            maximumPagesPerQuery = _searXng.MaximumPagesPerQuery,
            maxRequestsPerResearchRun = _searXng.MaxRequestsPerResearchRun,
            requestDelayMilliseconds = _searXng.RequestDelayMilliseconds,
            allowPrivateNetworkHosts = _searXng.AllowPrivateNetworkHosts,
            basicAuthUsername = _searXng.BasicAuthUsername,
            basicAuthPassword = _searXng.BasicAuthPassword
        };
    }

    private object BuildWhatsAppStore()
    {
        return new
        {
            accessToken = _whatsApp.AccessToken,
            phoneNumberId = _whatsApp.PhoneNumberId,
            apiVersion = _whatsApp.ApiVersion,
            baseUrl = _whatsApp.BaseUrl,
            defaultTemplateLanguage = _whatsApp.DefaultTemplateLanguage,
            preferTemplateMessages = _whatsApp.PreferTemplateMessages,
            timeoutSeconds = _whatsApp.TimeoutSeconds
        };
    }

    private object BuildMicrosoftGraphStore()
    {
        return new
        {
            clientId = _microsoftGraph.ClientId,
            clientSecret = _microsoftGraph.ClientSecret,
            tenantId = _microsoftGraph.TenantId,
            redirectUri = _microsoftGraph.RedirectUri,
            frontendSuccessUri = _microsoftGraph.FrontendSuccessUri,
            frontendErrorUri = _microsoftGraph.FrontendErrorUri
        };
    }

    private object BuildEmailSendingStore()
    {
        return new
        {
            defaultDelayMilliseconds = _emailSending.DefaultDelayMilliseconds,
            defaultBatchSize = _emailSending.DefaultBatchSize,
            defaultMaxRetryCount = _emailSending.DefaultMaxRetryCount,
            connectionTimeoutSeconds = _emailSending.ConnectionTimeoutSeconds
        };
    }

    private object BuildAttachmentsStore()
    {
        return new
        {
            maxFileSizeBytes = _attachments.MaxFileSizeBytes,
            maxTotalSizeBytes = _attachments.MaxTotalSizeBytes,
            allowedContentTypes = _attachments.AllowedContentTypes,
            storageRoot = _attachments.StorageRoot
        };
    }

    private object BuildQueuesStore()
    {
        return new
        {
            provider = _queues.Provider
        };
    }

    private static List<string> NormalizeList(IEnumerable<string>? values)
    {
        var result = new List<string>();
        if (values is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

namespace Sendly.Application.DTOs;

public class AppSettingsDto
{
    public OpenAiSettingsDto OpenAi { get; set; } = new();

    public ResearchSettingsDto Research { get; set; } = new();

    public BraveSearchSettingsDto BraveSearch { get; set; } = new();

    public SearXngSettingsDto SearXng { get; set; } = new();

    public WhatsAppSettingsDto WhatsApp { get; set; } = new();

    public MicrosoftGraphSettingsDto MicrosoftGraph { get; set; } = new();

    public EmailSendingSettingsDto EmailSending { get; set; } = new();

    public AttachmentSettingsDto Attachments { get; set; } = new();

    public BackgroundQueueSettingsDto BackgroundQueues { get; set; } = new();
}

public class OpenAiSettingsDto
{
    public string? ApiKey { get; set; }

    public bool ApiKeyConfigured { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public string? OrganizationId { get; set; }

    public string? ProjectId { get; set; }

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public int TimeoutSeconds { get; set; } = 60;
}

public class ResearchSettingsDto
{
    public int MaxCandidatesPerRun { get; set; } = 200;

    public int DefaultMinQualificationScore { get; set; } = 70;

    public int DefaultRequestedProspectCount { get; set; } = 50;

    public int ProviderTimeoutSeconds { get; set; } = 30;

    public bool EnableSeedProvider { get; set; }

    public bool EnableWebsiteCrawler { get; set; }

    public bool EnableSearchProvider { get; set; } = true;

    public bool EnableHtmlSearchFallback { get; set; }

    public int MaxPagesPerCrawl { get; set; } = 5;

    public int MaxSearchResults { get; set; } = 20;

    public int MaxSearchQueries { get; set; } = 50;

    public int MaxSearchRequests { get; set; } = 100;

    public int MaxResearchDurationMinutes { get; set; } = 30;

    public int DiscoveryOversampleFactor { get; set; } = 4;

    public int MinSearchRelevanceScore { get; set; }

    public string? BingSearchApiKey { get; set; }

    public bool BingSearchApiKeyConfigured { get; set; }

    public string? BingSearchEndpoint { get; set; }

    public string? GoogleSearchApiKey { get; set; }

    public bool GoogleSearchApiKeyConfigured { get; set; }

    public string? GoogleSearchEngineId { get; set; }

    public List<string> HostDenylist { get; set; } = [];
}

public class BraveSearchSettingsDto
{
    public bool Enabled { get; set; } = true;

    public string? ApiKey { get; set; }

    public bool ApiKeyConfigured { get; set; }

    public string BaseUrl { get; set; } = "https://api.search.brave.com/res/v1/web/search";

    public int TimeoutSeconds { get; set; } = 30;

    public int MaxResultsPerQuery { get; set; } = 10;

    public int MaxRequestsPerResearchRun { get; set; } = 20;

    public int RequestDelayMilliseconds { get; set; } = 250;

    public string ConnectionStatus { get; set; } = "NotConfigured";
}

public class SearXngSettingsDto
{
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = string.Empty;

    public string DefaultLanguage { get; set; } = "en";

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int DefaultResultsPerQuery { get; set; } = 10;

    public int MaximumPagesPerQuery { get; set; } = 2;

    public int MaxRequestsPerResearchRun { get; set; } = 100;

    public int RequestDelayMilliseconds { get; set; } = 250;

    public bool AllowPrivateNetworkHosts { get; set; }

    public string? BasicAuthUsername { get; set; }

    public string? BasicAuthPassword { get; set; }

    public bool BasicAuthConfigured { get; set; }

    public string ConnectionStatus { get; set; } = "NotConfigured";
}

public class WhatsAppSettingsDto
{
    public string? AccessToken { get; set; }

    public bool AccessTokenConfigured { get; set; }

    public string PhoneNumberId { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = "v21.0";

    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    public string DefaultTemplateLanguage { get; set; } = "en";

    public bool PreferTemplateMessages { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;
}

public class MicrosoftGraphSettingsDto
{
    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public bool ClientSecretConfigured { get; set; }

    public string TenantId { get; set; } = "common";

    public string RedirectUri { get; set; } = string.Empty;

    public string FrontendSuccessUri { get; set; } = string.Empty;

    public string FrontendErrorUri { get; set; } = string.Empty;
}

public class EmailSendingSettingsDto
{
    public int DefaultDelayMilliseconds { get; set; } = 1000;

    public int DefaultBatchSize { get; set; } = 50;

    public int DefaultMaxRetryCount { get; set; } = 2;

    public int ConnectionTimeoutSeconds { get; set; } = 30;
}

public class AttachmentSettingsDto
{
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    public long MaxTotalSizeBytes { get; set; } = 20 * 1024 * 1024;

    public List<string> AllowedContentTypes { get; set; } = [];

    public string StorageRoot { get; set; } = "App_Data/attachments";
}

public class BackgroundQueueSettingsDto
{
    public string Provider { get; set; } = "InMemory";
}

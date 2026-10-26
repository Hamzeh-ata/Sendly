using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Infrastructure.Ai;
using Sendly.Infrastructure.Email;
using Sendly.Infrastructure.Jobs;
using Sendly.Infrastructure.Persistence;
using Sendly.Infrastructure.Research;
using Sendly.Infrastructure.Security;
using Sendly.Infrastructure.Storage;
using Sendly.Infrastructure.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sendly.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Sendly")
            ?? "Data Source=App_Data/Sendly.db";

        var dataDirectory = Path.GetDirectoryName(connectionString.Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(dataDirectory) && !Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }

        services.AddDbContext<SendlyDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ISendlyDbContext>(sp => sp.GetRequiredService<SendlyDbContext>());

        RegisterBackgroundQueues(services);
        services.AddSingleton<ISecretProtector, AesSecretProtector>();
        services.AddHttpClient("MicrosoftGraph");
        services.AddHttpClient("OpenAI", (_, client) =>
        {
            ConfigureNamedHttpClient(client, configuration.GetSection(OpenAiOptions.SectionName), "https://api.openai.com/v1/", 60);
        });
        services.AddHttpClient("ResearchCrawler", (_, client) =>
        {
            var section = configuration.GetSection(ResearchOptions.SectionName);
            var timeoutSeconds = 30;
            if (int.TryParse(section["ProviderTimeoutSeconds"], out var parsedTimeout))
            {
                timeoutSeconds = Math.Clamp(parsedTimeout, 5, 120);
            }

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var userAgent = section["CrawlerUserAgent"];
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                userAgent = "SendlyResearchBot/1.0 (+https://localhost; research@sendly.local)";
            }

            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        services.AddHttpClient("WhatsAppBusiness", (_, client) =>
        {
            ConfigureNamedHttpClient(
                client,
                configuration.GetSection(WhatsAppBusinessOptions.SectionName),
                "https://graph.facebook.com/",
                30);
        });
        services.AddHttpClient("BingSearch");
        services.AddHttpClient("GoogleSearch");
        services.AddHttpClient("BraveSearch", (_, client) =>
        {
            var section = configuration.GetSection(BraveSearchOptions.SectionName);
            var timeoutSeconds = 30;
            if (int.TryParse(section["TimeoutSeconds"], out var parsedTimeout))
            {
                timeoutSeconds = Math.Clamp(parsedTimeout, 5, 120);
            }

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SendlyResearchBot/1.0");
        });
        services.AddHttpClient("HtmlSearch", (_, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SendlyResearchBot/1.0");
        });
        services.AddHttpClient("OpenStreetMap", (_, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SendlyResearchBot/1.0 (local research; contact: research@sendly.local)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddHttpClient("OpenStreetMapOverpass", (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OverpassOptions>>().Value;
            var timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 5, 300);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SendlyResearchBot/1.0 (local research; contact: research@sendly.local)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddSingleton<IChallengedSourceRegistry, ChallengedSourceRegistry>();
        services.AddSingleton<IBraveSearchSecretStore, BraveSearchSecretStore>();
        services.AddScoped<IBraveSearchClient, BraveSearchClient>();
        services.AddScoped<MicrosoftOAuthService>();
        services.AddScoped<IMicrosoftOAuthService>(sp => sp.GetRequiredService<MicrosoftOAuthService>());
        services.AddScoped<IMicrosoftTokenService>(sp => sp.GetRequiredService<MicrosoftOAuthService>());
        services.AddScoped<IProviderEmailSender, SmtpEmailSender>();
        services.AddScoped<IProviderEmailSender, MicrosoftGraphEmailSender>();
        services.AddScoped<ISmtpConnectionTester, MailKitSmtpConnectionTester>();
        services.AddScoped<IAttachmentStore, FileAttachmentStore>();
        services.AddHttpClient("Ollama");
        services.AddScoped<OllamaAiResearchService>();
        services.AddScoped<OpenAiMapContactAssistant>();
        services.AddScoped<IOpenAiMapContactAssistant>(sp => sp.GetRequiredService<OpenAiMapContactAssistant>());
        services.AddScoped<IAiResearchService>(sp => sp.GetRequiredService<OllamaAiResearchService>());
        services.AddScoped<ILocalAiHealthService>(sp => sp.GetRequiredService<OllamaAiResearchService>());
        services.AddScoped<ILocalAiQueryPlanner>(sp => sp.GetRequiredService<OllamaAiResearchService>());
        services.AddScoped<ILocalAiSearchClassifier>(sp => sp.GetRequiredService<OllamaAiResearchService>());
        services.AddScoped<ILocalAiResearchIntelligence>(sp => sp.GetRequiredService<OllamaAiResearchService>());
        services.AddScoped<IMapResearchIntelligence, OllamaMapResearchIntelligence>();
        services.AddHttpClient("GooglePlaces", (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GooglePlacesOptions>>().Value;
            var timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 5, 120);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddScoped<OverpassOpenStreetMapSource>();
        services.AddScoped<NominatimMapGeographicSource>();
        services.AddScoped<GooglePlacesMapGeographicSource>();
        services.AddScoped<IMapGeographicSource, CompositeMapGeographicSource>();
        services.AddScoped<WebsitePageCrawler>();
        services.AddScoped<ISitemapParser, SitemapParser>();
        services.AddSingleton<IRobotsTxtService, RobotsTxtService>();

        services.AddScoped<ISafeHttpFetcher, SafeHttpFetcher>();
        services.AddScoped<LocalBusinessIndexService>();
        services.AddScoped<IUrlDiscoverySource, SeedUrlDiscoverySource>();
        services.AddScoped<IUrlDiscoverySource, PriorResearchUrlDiscoverySource>();
        services.AddScoped<IUrlDiscoverySource, LocalBusinessIndexDiscoverySource>();
        services.AddScoped<IUrlDiscoverySource, DirectoryCatalogDiscoverySource>();
        services.AddScoped<IUrlDiscoverySource, IndustryAssociationDiscoverySource>();
        services.AddScoped<IUrlDiscoverySource, OpenStreetMapDiscoverySource>();
        services.AddScoped<IUrlDiscoverySource, CommonCrawlIndexDiscoverySource>();
        services.AddScoped<IUrlDiscoverySource, WebSearchUrlDiscoverySource>();
        services.AddScoped<IAutomaticDiscoveryCoordinator, AutomaticDiscoveryCoordinator>();
        services.AddScoped<IProspectSource, IndependentCrawlProspectSource>();
        services.AddScoped<IProspectSource, SeedProspectSource>();
        services.AddScoped<IProspectSource, WebsiteCrawlProspectSource>();
        services.AddScoped<IProspectSource, SearchProspectSource>();
        services.AddScoped<IWebSearchProvider, BraveWebSearchProvider>();
        services.AddScoped<IWebSearchProvider, BingWebSearchProvider>();
        services.AddScoped<IWebSearchProvider, GoogleCustomSearchProvider>();
        services.AddScoped<IWebSearchProvider, HtmlWebSearchProvider>();
        services.AddScoped<IWhatsAppSender, GraphWhatsAppSender>();
        services.AddHostedService<CampaignWorker>();
        services.AddHostedService<ResearchWorker>();
        services.AddHostedService<SequenceWorker>();
        services.AddHostedService<SequenceDueScanner>();

        return services;
    }

    private static void RegisterBackgroundQueues(IServiceCollection services)
    {
        services.AddSingleton<InMemoryCampaignQueue>();
        services.AddSingleton<InMemoryResearchQueue>();
        services.AddSingleton<InMemorySequenceQueue>();
        services.AddSingleton<EfCampaignQueue>();
        services.AddSingleton<EfResearchQueue>();
        services.AddSingleton<EfSequenceQueue>();
        services.AddSingleton<ICampaignQueue>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackgroundQueueOptions>>().Value;
            return options.UseDurableQueue
                ? sp.GetRequiredService<EfCampaignQueue>()
                : sp.GetRequiredService<InMemoryCampaignQueue>();
        });
        services.AddSingleton<IResearchQueue>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackgroundQueueOptions>>().Value;
            return options.UseDurableQueue
                ? sp.GetRequiredService<EfResearchQueue>()
                : sp.GetRequiredService<InMemoryResearchQueue>();
        });
        services.AddSingleton<ISequenceQueue>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackgroundQueueOptions>>().Value;
            return options.UseDurableQueue
                ? sp.GetRequiredService<EfSequenceQueue>()
                : sp.GetRequiredService<InMemorySequenceQueue>();
        });
    }

    private static void ConfigureNamedHttpClient(
        HttpClient client,
        IConfigurationSection section,
        string defaultBaseUrl,
        int defaultTimeoutSeconds)
    {
        var baseUrl = section["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = defaultBaseUrl;
        }

        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        var timeoutSeconds = defaultTimeoutSeconds;
        if (int.TryParse(section["TimeoutSeconds"], out var parsedTimeout))
        {
            timeoutSeconds = Math.Clamp(parsedTimeout, 5, 300);
        }

        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }
}

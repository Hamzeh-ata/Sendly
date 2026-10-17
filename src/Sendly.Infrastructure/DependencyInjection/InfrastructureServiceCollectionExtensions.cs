using Sendly.Application.Abstractions;
using Sendly.Application.Options;
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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SendlyResearchBot/1.0");
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
        services.AddHttpClient("SearXng", (_, client) =>
        {
            var section = configuration.GetSection(SearXngOptions.SectionName);
            var timeoutSeconds = 30;
            if (int.TryParse(section["RequestTimeoutSeconds"], out var parsedTimeout))
            {
                timeoutSeconds = Math.Clamp(parsedTimeout, 5, 120);
            }

            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SendlyResearchBot/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        services.AddHttpClient("HtmlSearch", (_, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SendlyResearchBot/1.0");
        });
        services.AddSingleton<IBraveSearchSecretStore, BraveSearchSecretStore>();
        services.AddScoped<IBraveSearchClient, BraveSearchClient>();
        services.AddScoped<ISearXngSearchClient, SearXngSearchClient>();
        services.AddScoped<MicrosoftOAuthService>();
        services.AddScoped<IMicrosoftOAuthService>(sp => sp.GetRequiredService<MicrosoftOAuthService>());
        services.AddScoped<IMicrosoftTokenService>(sp => sp.GetRequiredService<MicrosoftOAuthService>());
        services.AddScoped<IProviderEmailSender, SmtpEmailSender>();
        services.AddScoped<IProviderEmailSender, MicrosoftGraphEmailSender>();
        services.AddScoped<ISmtpConnectionTester, MailKitSmtpConnectionTester>();
        services.AddScoped<IAttachmentStore, FileAttachmentStore>();
        services.AddScoped<IAiResearchService, OpenAiResearchService>();
        services.AddScoped<WebsitePageCrawler>();
        services.AddScoped<IProspectSource, SeedProspectSource>();
        services.AddScoped<IProspectSource, WebsiteCrawlProspectSource>();
        services.AddScoped<IProspectSource, SearchProspectSource>();
        services.AddScoped<IWebSearchProvider, BraveWebSearchProvider>();
        services.AddScoped<IWebSearchProvider, SearXngWebSearchProvider>();
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

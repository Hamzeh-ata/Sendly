using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sendly.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailSendingOptions>(configuration.GetSection(EmailSendingOptions.SectionName));
        services.Configure<AttachmentOptions>(configuration.GetSection(AttachmentOptions.SectionName));
        services.Configure<SecretProtectionOptions>(configuration.GetSection(SecretProtectionOptions.SectionName));
        services.Configure<MicrosoftGraphOptions>(configuration.GetSection(MicrosoftGraphOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));
        services.Configure<ResearchOptions>(configuration.GetSection(ResearchOptions.SectionName));
        services.Configure<BraveSearchOptions>(configuration.GetSection(BraveSearchOptions.SectionName));
        services.Configure<SearXngOptions>(configuration.GetSection(SearXngOptions.SectionName));
        services.Configure<WhatsAppBusinessOptions>(configuration.GetSection(WhatsAppBusinessOptions.SectionName));
        services.Configure<BackgroundQueueOptions>(configuration.GetSection(BackgroundQueueOptions.SectionName));

        services.AddSingleton<CampaignCancellationRegistry>();
        services.AddSingleton<ResearchCancellationRegistry>();
        services.AddScoped<IEmailDeliveryService, EmailDeliveryService>();
        services.AddScoped<EmailAccountService>();
        services.AddScoped<SignatureService>();
        services.AddScoped<EmailTemplateService>();
        services.AddScoped<TemplateRecipientProtectionService>();
        services.AddScoped<CampaignService>();
        services.AddScoped<CampaignProcessor>();
        services.AddScoped<RuleBasedAiResearchService>();
        services.AddScoped<ProspectDuplicateService>();
        services.AddScoped<OutreachProjectService>();
        services.AddScoped<ResearchRunService>();
        services.AddScoped<ResearchProcessor>();
        services.AddScoped<ProspectService>();
        services.AddScoped<OutreachSequenceService>();
        services.AddScoped<SequenceProcessor>();
        services.AddScoped<IOutreachChannel, EmailOutreachChannel>();
        services.AddScoped<IOutreachChannel, WhatsAppOutreachChannel>();
        services.AddScoped<AppSettingsService>();
        return services;
    }
}

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

        services.AddSingleton<CampaignCancellationRegistry>();
        services.AddScoped<SmtpSettingsService>();
        services.AddScoped<SignatureService>();
        services.AddScoped<CampaignService>();
        services.AddScoped<CampaignProcessor>();
        return services;
    }
}

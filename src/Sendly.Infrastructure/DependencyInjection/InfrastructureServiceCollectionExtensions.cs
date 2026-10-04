using Sendly.Application.Abstractions;
using Sendly.Infrastructure.Email;
using Sendly.Infrastructure.Jobs;
using Sendly.Infrastructure.Persistence;
using Sendly.Infrastructure.Security;
using Sendly.Infrastructure.Storage;
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

        services.AddSingleton<ICampaignQueue, InMemoryCampaignQueue>();
        services.AddSingleton<ISecretProtector, AesSecretProtector>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<ISmtpConnectionTester, MailKitSmtpConnectionTester>();
        services.AddScoped<IAttachmentStore, FileAttachmentStore>();
        services.AddHostedService<CampaignWorker>();

        return services;
    }
}

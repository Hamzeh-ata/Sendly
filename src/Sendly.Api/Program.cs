using System.Text.Json.Serialization;
using Sendly.Api.Hubs;
using Sendly.Api.Services;
using Sendly.Application.Abstractions;
using Sendly.Application.DependencyInjection;
using Sendly.Application.Services;
using Sendly.Infrastructure.DependencyInjection;
using Sendly.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ICampaignProgressNotifier, SignalRCampaignProgressNotifier>();
builder.Services.AddSingleton<IResearchProgressNotifier, SignalRResearchProgressNotifier>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    });

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("SendlyClient", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "http://localhost:4201",
                "http://localhost:4202")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SendlyDbContext>();
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data", "attachments"));
    db.Database.Migrate();
    var appSettings = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
    await appSettings.HydrateFromStoreAsync(CancellationToken.None);
    var protection = scope.ServiceProvider.GetRequiredService<TemplateRecipientProtectionService>();
    await protection.BackfillFromExistingCampaignsAsync(CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("SendlyClient");
app.MapControllers();
app.MapHub<CampaignHub>("/hubs/campaigns");
app.MapHub<ResearchHub>("/hubs/research");

app.Run();

public partial class Program;

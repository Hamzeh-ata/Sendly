using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class SmtpSettingsService
{
    private readonly ISendlyDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly ISmtpConnectionTester _connectionTester;

    public SmtpSettingsService(
        ISendlyDbContext db,
        ISecretProtector secretProtector,
        ISmtpConnectionTester connectionTester)
    {
        _db = db;
        _secretProtector = secretProtector;
        _connectionTester = connectionTester;
    }

    public async Task<SmtpSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var settings = await GetTrackedEntityAsync(cancellationToken);
        return MapToDto(settings);
    }

    public async Task<SmtpSettingsDto> UpsertAsync(UpsertSmtpSettingsDto request, CancellationToken cancellationToken)
    {
        ValidateUpsert(request);
        var settings = await GetTrackedEntityAsync(cancellationToken);

        if (settings is null)
        {
            settings = CreateNewSettings(request);
            _db.SmtpSettings.Add(settings);
        }
        else
        {
            ApplyUpdates(settings, request);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(settings);
    }

    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        var settings = await GetTrackedEntityAsync(cancellationToken);
        if (settings is null)
        {
            return;
        }

        _db.SmtpSettings.Remove(settings);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<SmtpTestResultDto> TestConnectionAsync(UpsertSmtpSettingsDto? request, CancellationToken cancellationToken)
    {
        var credentials = await ResolveCredentialsAsync(request, cancellationToken);
        if (credentials is null)
        {
            return new SmtpTestResultDto
            {
                Success = false,
                Message = "SMTP configuration is incomplete."
            };
        }

        return await _connectionTester.TestAsync(credentials, cancellationToken);
    }

    public async Task<ResolvedSmtpCredentials?> GetResolvedCredentialsAsync(CancellationToken cancellationToken)
    {
        var settings = await GetTrackedEntityAsync(cancellationToken);
        if (settings is null || string.IsNullOrWhiteSpace(settings.PasswordProtected))
        {
            return null;
        }

        return new ResolvedSmtpCredentials
        {
            Host = settings.Host,
            Port = settings.Port,
            SecurityMode = settings.SecurityMode,
            Username = settings.Username,
            Password = _secretProtector.Unprotect(settings.PasswordProtected),
            SenderEmail = settings.SenderEmail,
            SenderDisplayName = settings.SenderDisplayName,
            ReplyToEmail = settings.ReplyToEmail
        };
    }

    private async Task<SmtpSettings?> GetTrackedEntityAsync(CancellationToken cancellationToken)
    {
        return await _db.SmtpSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private SmtpSettings CreateNewSettings(UpsertSmtpSettingsDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Password is required when saving SMTP settings for the first time.");
        }

        return new SmtpSettings
        {
            Host = request.Host.Trim(),
            Port = request.Port,
            SecurityMode = request.SecurityMode,
            Username = request.Username.Trim(),
            PasswordProtected = _secretProtector.Protect(request.Password),
            SenderEmail = request.SenderEmail.Trim(),
            SenderDisplayName = request.SenderDisplayName.Trim(),
            ReplyToEmail = NormalizeOptionalEmail(request.ReplyToEmail),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private void ApplyUpdates(SmtpSettings settings, UpsertSmtpSettingsDto request)
    {
        settings.Host = request.Host.Trim();
        settings.Port = request.Port;
        settings.SecurityMode = request.SecurityMode;
        settings.Username = request.Username.Trim();
        settings.SenderEmail = request.SenderEmail.Trim();
        settings.SenderDisplayName = request.SenderDisplayName.Trim();
        settings.ReplyToEmail = NormalizeOptionalEmail(request.ReplyToEmail);
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            settings.PasswordProtected = _secretProtector.Protect(request.Password);
        }
    }

    private async Task<SmtpConnectionTestRequest?> ResolveCredentialsAsync(
        UpsertSmtpSettingsDto? request,
        CancellationToken cancellationToken)
    {
        if (request is not null &&
            !string.IsNullOrWhiteSpace(request.Host) &&
            !string.IsNullOrWhiteSpace(request.Username) &&
            !string.IsNullOrWhiteSpace(request.Password))
        {
            return new SmtpConnectionTestRequest
            {
                Host = request.Host.Trim(),
                Port = request.Port,
                SecurityMode = request.SecurityMode,
                Username = request.Username.Trim(),
                Password = request.Password
            };
        }

        var existing = await GetResolvedCredentialsAsync(cancellationToken);
        if (existing is null)
        {
            return null;
        }

        return new SmtpConnectionTestRequest
        {
            Host = existing.Host,
            Port = existing.Port,
            SecurityMode = existing.SecurityMode,
            Username = existing.Username,
            Password = existing.Password
        };
    }

    private static void ValidateUpsert(UpsertSmtpSettingsDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
        {
            throw new InvalidOperationException("SMTP host is required.");
        }

        if (request.Port < 1 || request.Port > 65535)
        {
            throw new InvalidOperationException("SMTP port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            throw new InvalidOperationException("SMTP username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SenderEmail) || !EmailAddressParser.IsValidEmail(request.SenderEmail))
        {
            throw new InvalidOperationException("A valid sender email is required.");
        }

        if (!string.IsNullOrWhiteSpace(request.ReplyToEmail) && !EmailAddressParser.IsValidEmail(request.ReplyToEmail))
        {
            throw new InvalidOperationException("Reply-to email is invalid.");
        }
    }

    private static string? NormalizeOptionalEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return email.Trim();
    }

    private static SmtpSettingsDto MapToDto(SmtpSettings? settings)
    {
        if (settings is null)
        {
            return new SmtpSettingsDto
            {
                IsConfigured = false,
                Port = 587,
                SecurityMode = SmtpSecurityMode.StartTls
            };
        }

        return new SmtpSettingsDto
        {
            Host = settings.Host,
            Port = settings.Port,
            SecurityMode = settings.SecurityMode,
            Username = settings.Username,
            HasPassword = !string.IsNullOrWhiteSpace(settings.PasswordProtected),
            SenderEmail = settings.SenderEmail,
            SenderDisplayName = settings.SenderDisplayName,
            ReplyToEmail = settings.ReplyToEmail,
            UpdatedAtUtc = settings.UpdatedAtUtc,
            IsConfigured = true,
            IsInsecure = settings.SecurityMode == SmtpSecurityMode.None
        };
    }
}

public sealed class ResolvedSmtpCredentials
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required SmtpSecurityMode SecurityMode { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }

    public required string SenderEmail { get; init; }

    public required string SenderDisplayName { get; init; }

    public string? ReplyToEmail { get; init; }
}

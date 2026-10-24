using Sendly.Application.Abstractions;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class EmailAccountService
{
    private readonly ISendlyDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly ISmtpConnectionTester _connectionTester;
    private readonly IMicrosoftOAuthService _microsoftOAuth;
    private readonly IMicrosoftTokenService _microsoftTokenService;

    public EmailAccountService(
        ISendlyDbContext db,
        ISecretProtector secretProtector,
        ISmtpConnectionTester connectionTester,
        IMicrosoftOAuthService microsoftOAuth,
        IMicrosoftTokenService microsoftTokenService)
    {
        _db = db;
        _secretProtector = secretProtector;
        _connectionTester = connectionTester;
        _microsoftOAuth = microsoftOAuth;
        _microsoftTokenService = microsoftTokenService;
    }

    public async Task<List<EmailAccountDto>> ListAsync(CancellationToken cancellationToken)
    {
        var entities = await _db.EmailAccounts.AsNoTracking().ToListAsync(cancellationToken);
        SortAccounts(entities);
        var result = new List<EmailAccountDto>();
        foreach (var entity in entities)
        {
            result.Add(Map(entity));
        }

        return result;
    }

    public async Task<EmailAccountDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.EmailAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<EmailAccountStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var entities = await _db.EmailAccounts.AsNoTracking().ToListAsync(cancellationToken);
        var connected = 0;
        foreach (var entity in entities)
        {
            if (IsConnected(entity))
            {
                connected++;
            }
        }

        return new EmailAccountStatusDto
        {
            IsConfigured = connected > 0,
            AccountCount = entities.Count,
            MicrosoftOAuthConfigured = _microsoftOAuth.IsConfigured
        };
    }

    public async Task<EmailAccountDto> CreateSmtpAsync(UpsertSmtpAccountDto request, CancellationToken cancellationToken)
    {
        ValidateSmtp(request, requirePassword: true);
        var entity = new EmailAccount
        {
            Provider = EmailProvider.Smtp,
            DisplayName = request.DisplayName.Trim(),
            EmailAddress = request.SenderEmail.Trim(),
            IsDefault = request.IsDefault || await _db.EmailAccounts.CountAsync(cancellationToken) == 0,
            IsActive = true,
            Host = request.Host.Trim(),
            Port = request.Port,
            SecurityMode = request.SecurityMode,
            Username = request.Username.Trim(),
            PasswordProtected = _secretProtector.Protect(request.Password!),
            SenderEmail = request.SenderEmail.Trim(),
            SenderDisplayName = request.SenderDisplayName.Trim(),
            ReplyToEmail = NormalizeOptionalEmail(request.ReplyToEmail),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        if (entity.IsDefault)
        {
            await ClearDefaultFlagsAsync(cancellationToken);
            entity.IsDefault = true;
        }

        _db.EmailAccounts.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<EmailAccountDto> UpdateSmtpAsync(Guid id, UpsertSmtpAccountDto request, CancellationToken cancellationToken)
    {
        var entity = await _db.EmailAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null || entity.Provider != EmailProvider.Smtp)
        {
            throw new InvalidOperationException("SMTP account was not found.");
        }

        ValidateSmtp(request, requirePassword: false);
        if (request.IsDefault)
        {
            await ClearDefaultFlagsAsync(cancellationToken);
        }

        entity.DisplayName = request.DisplayName.Trim();
        entity.EmailAddress = request.SenderEmail.Trim();
        entity.IsDefault = request.IsDefault || entity.IsDefault;
        entity.IsActive = true;
        entity.Host = request.Host.Trim();
        entity.Port = request.Port;
        entity.SecurityMode = request.SecurityMode;
        entity.Username = request.Username.Trim();
        entity.SenderEmail = request.SenderEmail.Trim();
        entity.SenderDisplayName = request.SenderDisplayName.Trim();
        entity.ReplyToEmail = NormalizeOptionalEmail(request.ReplyToEmail);
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            entity.PasswordProtected = _secretProtector.Protect(request.Password);
        }

        await EnsureDefaultExistsAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.EmailAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        var wasDefault = entity.IsDefault;
        _db.EmailAccounts.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        if (wasDefault)
        {
            await PromoteFirstAsDefaultAsync(cancellationToken);
        }
    }

    public async Task DisconnectMicrosoftAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.EmailAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null || entity.Provider != EmailProvider.MicrosoftGraph)
        {
            throw new InvalidOperationException("Microsoft account was not found.");
        }

        await _microsoftTokenService.DisconnectAsync(id, cancellationToken);
    }

    public async Task<EmailAccountDto> SetDefaultAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.EmailAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException("Email account was not found.");
        }

        await ClearDefaultFlagsAsync(cancellationToken);
        entity.IsDefault = true;
        entity.IsActive = true;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    public async Task<EmailTestResultDto> TestAsync(Guid? id, SmtpAccountTestRequestDto? smtpRequest, CancellationToken cancellationToken)
    {
        if (id.HasValue)
        {
            var account = await _db.EmailAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
            if (account is null)
            {
                return new EmailTestResultDto { Success = false, Message = "Email account was not found." };
            }

            if (account.Provider == EmailProvider.MicrosoftGraph)
            {
                var token = await _microsoftTokenService.GetValidAccessTokenAsync(account.Id, cancellationToken);
                return new EmailTestResultDto
                {
                    Success = !string.IsNullOrWhiteSpace(token),
                    Message = string.IsNullOrWhiteSpace(token)
                        ? "Microsoft account is disconnected or authentication expired."
                        : "Microsoft Graph connection is ready."
                };
            }
        }

        if (smtpRequest is not null
            && !string.IsNullOrWhiteSpace(smtpRequest.Host)
            && !string.IsNullOrWhiteSpace(smtpRequest.Username)
            && !string.IsNullOrWhiteSpace(smtpRequest.Password))
        {
            var result = await _connectionTester.TestAsync(new SmtpConnectionTestRequest
            {
                Host = smtpRequest.Host.Trim(),
                Port = smtpRequest.Port,
                SecurityMode = smtpRequest.SecurityMode,
                Username = smtpRequest.Username.Trim(),
                Password = smtpRequest.Password
            }, cancellationToken);

            return new EmailTestResultDto { Success = result.Success, Message = result.Message };
        }

        if (id.HasValue)
        {
            var account = await _db.EmailAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken);
            if (account is null || account.Provider != EmailProvider.Smtp || string.IsNullOrWhiteSpace(account.PasswordProtected))
            {
                return new EmailTestResultDto { Success = false, Message = "SMTP configuration is incomplete." };
            }

            var result = await _connectionTester.TestAsync(new SmtpConnectionTestRequest
            {
                Host = account.Host ?? string.Empty,
                Port = account.Port ?? 587,
                SecurityMode = account.SecurityMode ?? SmtpSecurityMode.StartTls,
                Username = account.Username ?? string.Empty,
                Password = _secretProtector.Unprotect(account.PasswordProtected)
            }, cancellationToken);

            return new EmailTestResultDto { Success = result.Success, Message = result.Message };
        }

        return new EmailTestResultDto { Success = false, Message = "SMTP configuration is incomplete." };
    }

    public async Task EnsureAccountReadyAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(accountId, cancellationToken);
        if (account is null || !IsConnected(account))
        {
            throw new InvalidOperationException("A connected email account is required before sending.");
        }
    }

    public async Task<ResolvedSendingAccount?> ResolveSendingAccountAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        var account = await ResolveAccountAsync(accountId, cancellationToken);
        if (account is null || !IsConnected(account))
        {
            return null;
        }

        return new ResolvedSendingAccount
        {
            AccountId = account.Id,
            Provider = account.Provider,
            DisplayName = account.DisplayName,
            EmailAddress = account.EmailAddress
        };
    }

    public MicrosoftConnectDto BeginMicrosoftConnect()
    {
        if (!_microsoftOAuth.IsConfigured)
        {
            throw new InvalidOperationException("Microsoft Graph OAuth is not configured on the server.");
        }

        var state = Guid.NewGuid().ToString("N");
        return new MicrosoftConnectDto
        {
            AuthorizeUrl = _microsoftOAuth.BuildAuthorizeUrl(state)
        };
    }

    private async Task<EmailAccount?> ResolveAccountAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        if (accountId.HasValue)
        {
            return await _db.EmailAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == accountId.Value, cancellationToken);
        }

        var defaults = await _db.EmailAccounts
            .AsNoTracking()
            .Where(x => x.IsDefault && x.IsActive)
            .ToListAsync(cancellationToken);
        if (defaults.Count > 0)
        {
            return defaults[0];
        }

        var all = await _db.EmailAccounts.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        SortAccounts(all);
        return all.Count > 0 ? all[0] : null;
    }

    private async Task ClearDefaultFlagsAsync(CancellationToken cancellationToken)
    {
        var defaults = await _db.EmailAccounts.Where(x => x.IsDefault).ToListAsync(cancellationToken);
        foreach (var item in defaults)
        {
            item.IsDefault = false;
            item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private async Task PromoteFirstAsDefaultAsync(CancellationToken cancellationToken)
    {
        var remaining = await _db.EmailAccounts.ToListAsync(cancellationToken);
        if (remaining.Count == 0)
        {
            return;
        }

        SortAccounts(remaining);
        remaining[0].IsDefault = true;
        remaining[0].UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDefaultExistsAsync(CancellationToken cancellationToken)
    {
        var hasDefault = await _db.EmailAccounts.AnyAsync(x => x.IsDefault, cancellationToken);
        if (!hasDefault)
        {
            await PromoteFirstAsDefaultAsync(cancellationToken);
        }
    }

    private static void ValidateSmtp(UpsertSmtpAccountDto request, bool requirePassword)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new InvalidOperationException("Account name is required.");
        }

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

        if (requirePassword && string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Password is required when creating an SMTP account.");
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

    private static bool IsConnected(EmailAccount account)
    {
        if (!account.IsActive)
        {
            return false;
        }

        if (account.Provider == EmailProvider.Smtp)
        {
            return !string.IsNullOrWhiteSpace(account.Host)
                && account.Port.HasValue
                && !string.IsNullOrWhiteSpace(account.PasswordProtected)
                && !string.IsNullOrWhiteSpace(account.SenderEmail);
        }

        return account.Provider == EmailProvider.MicrosoftGraph
            && !string.IsNullOrWhiteSpace(account.RefreshTokenProtected);
    }

    private static void SortAccounts(List<EmailAccount> entities)
    {
        entities.Sort(static (left, right) =>
        {
            if (left.IsDefault != right.IsDefault)
            {
                return right.IsDefault.CompareTo(left.IsDefault);
            }

            return string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private string? RevealPassword(EmailAccount entity)
    {
        if (string.IsNullOrWhiteSpace(entity.PasswordProtected))
        {
            return null;
        }

        try
        {
            return _secretProtector.Unprotect(entity.PasswordProtected);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private EmailAccountDto Map(EmailAccount entity)
    {
        return new EmailAccountDto
        {
            Id = entity.Id,
            Provider = entity.Provider,
            DisplayName = entity.DisplayName,
            EmailAddress = entity.EmailAddress,
            IsDefault = entity.IsDefault,
            IsActive = entity.IsActive,
            IsConnected = IsConnected(entity),
            Host = entity.Host,
            Port = entity.Port,
            SecurityMode = entity.SecurityMode,
            Username = entity.Username,
            HasPassword = !string.IsNullOrWhiteSpace(entity.PasswordProtected),
            Password = RevealPassword(entity),
            SenderEmail = entity.SenderEmail,
            SenderDisplayName = entity.SenderDisplayName,
            ReplyToEmail = entity.ReplyToEmail,
            IsInsecure = entity.SecurityMode == SmtpSecurityMode.None,
            CreatedAtUtc = entity.CreatedAtUtc,
            UpdatedAtUtc = entity.UpdatedAtUtc
        };
    }
}

public sealed class ResolvedSendingAccount
{
    public required Guid AccountId { get; init; }

    public required EmailProvider Provider { get; init; }

    public required string DisplayName { get; init; }

    public required string EmailAddress { get; init; }
}

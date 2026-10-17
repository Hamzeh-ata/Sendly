using Sendly.Application.Abstractions;
using Sendly.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Sendly.Application.Services;

public class EmailDeliveryService : IEmailDeliveryService
{
    private readonly ISendlyDbContext _db;
    private readonly ISecretProtector _secretProtector;
    private readonly IMicrosoftTokenService _microsoftTokenService;
    private readonly IEnumerable<IProviderEmailSender> _senders;

    public EmailDeliveryService(
        ISendlyDbContext db,
        ISecretProtector secretProtector,
        IMicrosoftTokenService microsoftTokenService,
        IEnumerable<IProviderEmailSender> senders)
    {
        _db = db;
        _secretProtector = secretProtector;
        _microsoftTokenService = microsoftTokenService;
        _senders = senders;
    }

    public async Task<EmailSendResult> SendAsync(EmailDeliveryRequest request, CancellationToken cancellationToken)
    {
        var account = await _db.EmailAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.AccountId, cancellationToken);

        if (account is null || !account.IsActive)
        {
            return new EmailSendResult
            {
                Success = false,
                Message = "Sending account was not found or is inactive."
            };
        }

        var credentials = await ResolveCredentialsAsync(account, cancellationToken);
        if (credentials is null)
        {
            return new EmailSendResult
            {
                Success = false,
                Message = "Sending account credentials are incomplete or expired."
            };
        }

        var sender = ResolveSender(account.Provider);
        if (sender is null)
        {
            return new EmailSendResult
            {
                Success = false,
                Message = "No email provider is registered for this account."
            };
        }

        return await sender.SendAsync(credentials, request, cancellationToken);
    }

    private async Task<EmailAccountCredentials?> ResolveCredentialsAsync(
        Domain.Entities.EmailAccount account,
        CancellationToken cancellationToken)
    {
        if (account.Provider == EmailProvider.Smtp)
        {
            if (string.IsNullOrWhiteSpace(account.Host)
                || !account.Port.HasValue
                || string.IsNullOrWhiteSpace(account.PasswordProtected)
                || string.IsNullOrWhiteSpace(account.SenderEmail))
            {
                return null;
            }

            return new EmailAccountCredentials
            {
                AccountId = account.Id,
                Provider = account.Provider,
                DisplayName = account.DisplayName,
                EmailAddress = account.EmailAddress,
                Host = account.Host,
                Port = account.Port,
                SecurityMode = account.SecurityMode ?? SmtpSecurityMode.StartTls,
                Username = account.Username,
                Password = _secretProtector.Unprotect(account.PasswordProtected),
                SenderEmail = account.SenderEmail,
                SenderDisplayName = account.SenderDisplayName ?? account.DisplayName,
                ReplyToEmail = account.ReplyToEmail
            };
        }

        if (account.Provider == EmailProvider.MicrosoftGraph)
        {
            var accessToken = await _microsoftTokenService.GetValidAccessTokenAsync(account.Id, cancellationToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            return new EmailAccountCredentials
            {
                AccountId = account.Id,
                Provider = account.Provider,
                DisplayName = account.DisplayName,
                EmailAddress = account.EmailAddress,
                AccessToken = accessToken,
                SenderEmail = account.EmailAddress,
                SenderDisplayName = account.DisplayName
            };
        }

        return null;
    }

    private IProviderEmailSender? ResolveSender(EmailProvider provider)
    {
        foreach (var sender in _senders)
        {
            if (sender.Provider == provider)
            {
                return sender;
            }
        }

        return null;
    }
}

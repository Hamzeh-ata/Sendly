namespace Sendly.Application.Abstractions;

public interface IEmailDeliveryService
{
    Task<EmailSendResult> SendAsync(EmailDeliveryRequest request, CancellationToken cancellationToken);
}

public interface IProviderEmailSender
{
    Domain.Enums.EmailProvider Provider { get; }

    Task<EmailSendResult> SendAsync(
        EmailAccountCredentials credentials,
        EmailDeliveryRequest request,
        CancellationToken cancellationToken);
}

public sealed class EmailDeliveryRequest
{
    public required Guid AccountId { get; init; }

    public required string ToEmail { get; init; }

    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    public string? PlainTextBody { get; init; }

    public IReadOnlyList<EmailAttachmentContent> Attachments { get; init; } = [];
}

public sealed class EmailAttachmentContent
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}

public sealed class EmailSendResult
{
    public bool Success { get; init; }

    public bool IsTransientFailure { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class EmailAccountCredentials
{
    public required Guid AccountId { get; init; }

    public required Domain.Enums.EmailProvider Provider { get; init; }

    public required string DisplayName { get; init; }

    public required string EmailAddress { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public Domain.Enums.SmtpSecurityMode? SecurityMode { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? SenderEmail { get; init; }

    public string? SenderDisplayName { get; init; }

    public string? ReplyToEmail { get; init; }

    public string? AccessToken { get; init; }
}

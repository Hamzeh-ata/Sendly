using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailSendRequest request, CancellationToken cancellationToken);
}

public sealed class EmailSendRequest
{
    public required string ToEmail { get; init; }

    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    public string? PlainTextBody { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    public required SmtpSecurityMode SecurityMode { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }

    public required string SenderEmail { get; init; }

    public required string SenderDisplayName { get; init; }

    public string? ReplyToEmail { get; init; }

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

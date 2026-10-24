using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class EmailAccountDto
{
    public Guid Id { get; set; }

    public EmailProvider Provider { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }

    public bool IsConnected { get; set; }

    public string? Host { get; set; }

    public int? Port { get; set; }

    public SmtpSecurityMode? SecurityMode { get; set; }

    public string? Username { get; set; }

    public bool HasPassword { get; set; }

    public string? Password { get; set; }

    public string? SenderEmail { get; set; }

    public string? SenderDisplayName { get; set; }

    public string? ReplyToEmail { get; set; }

    public bool IsInsecure { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class UpsertSmtpAccountDto
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public SmtpSecurityMode SecurityMode { get; set; }

    public string Username { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string SenderEmail { get; set; } = string.Empty;

    public string SenderDisplayName { get; set; } = string.Empty;

    public string? ReplyToEmail { get; set; }
}

public class SmtpAccountTestRequestDto : UpsertSmtpAccountDto
{
    public Guid? Id { get; set; }
}

public class EmailAccountStatusDto
{
    public bool IsConfigured { get; set; }

    public int AccountCount { get; set; }

    public bool MicrosoftOAuthConfigured { get; set; }
}

public class MicrosoftConnectDto
{
    public string AuthorizeUrl { get; set; } = string.Empty;
}

public class EmailTestResultDto
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;
}

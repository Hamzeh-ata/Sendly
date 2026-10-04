using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class SmtpSettingsDto
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public SmtpSecurityMode SecurityMode { get; set; }

    public string Username { get; set; } = string.Empty;

    public bool HasPassword { get; set; }

    public string SenderEmail { get; set; } = string.Empty;

    public string SenderDisplayName { get; set; } = string.Empty;

    public string? ReplyToEmail { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public bool IsConfigured { get; set; }

    public bool IsInsecure { get; set; }
}

public class UpsertSmtpSettingsDto
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }

    public SmtpSecurityMode SecurityMode { get; set; }

    public string Username { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string SenderEmail { get; set; } = string.Empty;

    public string SenderDisplayName { get; set; } = string.Empty;

    public string? ReplyToEmail { get; set; }
}

public class SmtpTestResultDto
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;
}

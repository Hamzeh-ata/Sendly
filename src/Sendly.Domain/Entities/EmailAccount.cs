using System.ComponentModel.DataAnnotations;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

public class EmailAccount
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public EmailProvider Provider { get; set; } = EmailProvider.Smtp;

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(320)]
    public string EmailAddress { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(255)]
    public string? Host { get; set; }

    public int? Port { get; set; }

    public SmtpSecurityMode? SecurityMode { get; set; }

    [MaxLength(255)]
    public string? Username { get; set; }

    public string? PasswordProtected { get; set; }

    [MaxLength(320)]
    public string? SenderEmail { get; set; }

    [MaxLength(200)]
    public string? SenderDisplayName { get; set; }

    [MaxLength(320)]
    public string? ReplyToEmail { get; set; }

    public string? AccessTokenProtected { get; set; }

    public string? RefreshTokenProtected { get; set; }

    public DateTimeOffset? TokenExpiresAtUtc { get; set; }

    [MaxLength(200)]
    public string? MicrosoftUserId { get; set; }

    [MaxLength(100)]
    public string? MicrosoftTenantId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

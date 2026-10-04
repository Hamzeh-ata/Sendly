using System.ComponentModel.DataAnnotations;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

public class SmtpSettings
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    public SmtpSecurityMode SecurityMode { get; set; } = SmtpSecurityMode.StartTls;

    [MaxLength(255)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PasswordProtected { get; set; } = string.Empty;

    [Required]
    [MaxLength(320)]
    public string SenderEmail { get; set; } = string.Empty;

    [MaxLength(200)]
    public string SenderDisplayName { get; set; } = string.Empty;

    [MaxLength(320)]
    public string? ReplyToEmail { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

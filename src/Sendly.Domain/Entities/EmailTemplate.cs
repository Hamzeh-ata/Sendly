using System.ComponentModel.DataAnnotations;

namespace Sendly.Domain.Entities;

public class EmailTemplate
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string HtmlBody { get; set; } = string.Empty;

    public Guid? SignatureId { get; set; }

    public EmailSignature? Signature { get; set; }

    public Guid? EmailAccountId { get; set; }

    public EmailAccount? EmailAccount { get; set; }

    [MaxLength(100)]
    public string? Category { get; set; }

    [MaxLength(200)]
    public string? WhatsAppTemplateName { get; set; }

    [MaxLength(20)]
    public string? WhatsAppTemplateLanguage { get; set; }

    [MaxLength(500)]
    public string? WhatsAppHeaderParameter { get; set; }

    [MaxLength(20)]
    public string? WhatsAppButtonSubType { get; set; }

    public int? WhatsAppButtonIndex { get; set; }

    [MaxLength(500)]
    public string? WhatsAppButtonParameter { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<CampaignAttachment> Attachments { get; set; } = new List<CampaignAttachment>();
}

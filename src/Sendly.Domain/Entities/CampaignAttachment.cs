using System.ComponentModel.DataAnnotations;

namespace Sendly.Domain.Entities;

public class CampaignAttachment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    [Required]
    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    [Required]
    [MaxLength(500)]
    public string StoredPath { get; set; } = string.Empty;
}

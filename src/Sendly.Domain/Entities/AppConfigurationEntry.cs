using System.ComponentModel.DataAnnotations;

namespace Sendly.Domain.Entities;

public class AppConfigurationEntry
{
    [Key]
    [MaxLength(100)]
    public string Section { get; set; } = string.Empty;

    [Required]
    public string JsonPayload { get; set; } = "{}";

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

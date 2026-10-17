using System.ComponentModel.DataAnnotations;

namespace Sendly.Domain.Entities;

public class OutreachProjectTargetRole
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    [Required]
    [MaxLength(200)]
    public string Role { get; set; } = string.Empty;
}

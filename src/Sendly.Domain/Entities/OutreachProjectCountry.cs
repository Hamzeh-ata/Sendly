using System.ComponentModel.DataAnnotations;

namespace Sendly.Domain.Entities;

public class OutreachProjectCountry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}

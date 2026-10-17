using System.ComponentModel.DataAnnotations;

namespace Sendly.Domain.Entities;

public class OutreachProjectKeyword
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    [Required]
    [MaxLength(200)]
    public string Value { get; set; } = string.Empty;

    public bool IsExcluded { get; set; }
}

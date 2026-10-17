using System.ComponentModel.DataAnnotations;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

public class ProjectTemplate
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public OutreachProject? Project { get; set; }

    public Guid TemplateId { get; set; }

    public EmailTemplate? Template { get; set; }

    public OutreachChannelType Channel { get; set; } = OutreachChannelType.Email;

    [MaxLength(200)]
    public string? MatchIndustry { get; set; }

    [MaxLength(100)]
    public string? MatchCompanySize { get; set; }

    [MaxLength(500)]
    public string? MatchNotes { get; set; }

    public int SortOrder { get; set; }
}

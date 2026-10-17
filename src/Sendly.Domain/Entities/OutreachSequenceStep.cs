using System.ComponentModel.DataAnnotations;
using Sendly.Domain.Enums;

namespace Sendly.Domain.Entities;

public class OutreachSequenceStep
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SequenceId { get; set; }

    public OutreachSequence? Sequence { get; set; }

    public int StepOrder { get; set; }

    public OutreachChannelType Channel { get; set; } = OutreachChannelType.Email;

    public Guid? TemplateId { get; set; }

    public EmailTemplate? Template { get; set; }

    public int DelayHoursAfterPrevious { get; set; }

    [MaxLength(200)]
    public string? WhatsAppTemplateName { get; set; }

    [MaxLength(20)]
    public string? WhatsAppTemplateLanguage { get; set; }

    [MaxLength(20)]
    public string? VariantKey { get; set; }

    public int TrafficPercent { get; set; } = 100;
}

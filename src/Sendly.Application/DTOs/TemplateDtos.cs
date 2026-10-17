namespace Sendly.Application.DTOs;

public class EmailTemplateSummaryDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Subject { get; set; } = string.Empty;

    public Guid? SignatureId { get; set; }

    public string? SignatureName { get; set; }

    public Guid? EmailAccountId { get; set; }

    public string? EmailAccountName { get; set; }

    public string? EmailAccountAddress { get; set; }

    public string? Category { get; set; }

    public string? WhatsAppTemplateName { get; set; }

    public string? WhatsAppTemplateLanguage { get; set; }

    public string? WhatsAppHeaderParameter { get; set; }

    public string? WhatsAppButtonSubType { get; set; }

    public int? WhatsAppButtonIndex { get; set; }

    public string? WhatsAppButtonParameter { get; set; }

    public bool IsActive { get; set; }

    public int AttachmentCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public int TotalCampaigns { get; set; }

    public int UniqueRecipients { get; set; }

    public int SuccessfullyContacted { get; set; }

    public int FailedRecipients { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }
}

public class EmailTemplateDetailDto : EmailTemplateSummaryDto
{
    public string HtmlBody { get; set; } = string.Empty;

    public string? SignatureHtml { get; set; }

    public List<CampaignAttachmentDto> Attachments { get; set; } = [];
}

public class UpsertEmailTemplateDto
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public Guid? SignatureId { get; set; }

    public Guid? EmailAccountId { get; set; }

    public string? Category { get; set; }

    public string? WhatsAppTemplateName { get; set; }

    public string? WhatsAppTemplateLanguage { get; set; }

    public string? WhatsAppHeaderParameter { get; set; }

    public string? WhatsAppButtonSubType { get; set; }

    public int? WhatsAppButtonIndex { get; set; }

    public string? WhatsAppButtonParameter { get; set; }

    public bool IsActive { get; set; } = true;

    public List<Guid> AttachmentIds { get; set; } = [];
}

public class EmailTemplatePreviewDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string HtmlBody { get; set; } = string.Empty;

    public string? SignatureName { get; set; }

    public string? SignatureHtml { get; set; }

    public List<CampaignAttachmentDto> Attachments { get; set; } = [];
}

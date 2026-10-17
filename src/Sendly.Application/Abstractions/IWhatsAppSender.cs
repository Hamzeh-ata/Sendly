namespace Sendly.Application.Abstractions;

public class WhatsAppSendRequest
{
    public string ToNumber { get; set; } = string.Empty;

    public string MessageBody { get; set; } = string.Empty;

    public string? TemplateName { get; set; }

    public string? TemplateLanguage { get; set; }

    public List<string> TemplateBodyParameters { get; set; } = [];

    public string? TemplateHeaderParameter { get; set; }

    public string? TemplateButtonSubType { get; set; }

    public int? TemplateButtonIndex { get; set; }

    public string? TemplateButtonParameter { get; set; }

    public bool PreferTemplate { get; set; } = true;
}

public class WhatsAppSendResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? ProviderMessageId { get; set; }
}

public interface IWhatsAppSender
{
    bool IsConfigured { get; }

    Task<WhatsAppSendResult> SendAsync(WhatsAppSendRequest request, CancellationToken cancellationToken);
}

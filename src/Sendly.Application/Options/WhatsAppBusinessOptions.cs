namespace Sendly.Application.Options;

public class WhatsAppBusinessOptions
{
    public const string SectionName = "WhatsAppBusiness";

    public string AccessToken { get; set; } = string.Empty;

    public string PhoneNumberId { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = "v21.0";

    public string BaseUrl { get; set; } = "https://graph.facebook.com";

    public string DefaultTemplateLanguage { get; set; } = "en";

    public bool PreferTemplateMessages { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 30;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(PhoneNumberId);
}

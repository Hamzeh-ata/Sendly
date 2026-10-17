namespace Sendly.Application.Options;

public class MicrosoftGraphOptions
{
    public const string SectionName = "MicrosoftGraph";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string TenantId { get; set; } = "common";

    public string RedirectUri { get; set; } = "http://localhost:5088/api/email-accounts/microsoft/callback";

    public string FrontendSuccessUri { get; set; } = "http://localhost:4202/settings?microsoft=connected";

    public string FrontendErrorUri { get; set; } = "http://localhost:4202/settings?microsoft=error";
}

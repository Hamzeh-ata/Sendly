namespace Sendly.Application.Services;

public static class SignatureComposer
{
    public static string Combine(string htmlBody, string? signatureHtml)
    {
        var body = htmlBody?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(signatureHtml))
        {
            return body;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return signatureHtml.Trim();
        }

        return $"{body}<br/><div class=\"email-signature\">{signatureHtml.Trim()}</div>";
    }
}

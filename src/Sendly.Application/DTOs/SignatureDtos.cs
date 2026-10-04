namespace Sendly.Application.DTOs;

public class EmailSignatureDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string HtmlContent { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class UpsertEmailSignatureDto
{
    public string Name { get; set; } = string.Empty;

    public string HtmlContent { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}

public class SignatureSettingsDto
{
    public bool SignaturesEnabled { get; set; }
}

public class UpdateSignatureSettingsDto
{
    public bool SignaturesEnabled { get; set; }
}

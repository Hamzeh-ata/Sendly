namespace Sendly.Application.Options;

public class SecretProtectionOptions
{
    public const string SectionName = "SecretProtection";

    public string EncryptionKey { get; set; } = string.Empty;
}

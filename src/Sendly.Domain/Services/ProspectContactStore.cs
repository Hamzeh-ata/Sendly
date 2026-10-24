using System.Text.Json;

namespace Sendly.Domain.Services;

public static class ProspectContactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static List<string> Parse(string? json)
    {
        return ParseAttributed(json).Select(x => x.Value).ToList();
    }

    public static List<AttributedContact> ParseAttributed(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return FromLegacyValues([json]);
            }

            var result = new List<AttributedContact>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(new AttributedContact { Value = value.Trim() });
                    }

                    continue;
                }

                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var attributed = element.Deserialize<AttributedContact>(JsonOptions);
                if (attributed is null || string.IsNullOrWhiteSpace(attributed.Value))
                {
                    continue;
                }

                attributed.Value = attributed.Value.Trim();
                result.Add(attributed);
            }

            return DeduplicateAttributed(result, x => x);
        }
        catch (JsonException)
        {
            return FromLegacyValues([json]);
        }
    }

    public static string? Serialize(IEnumerable<string?> values, Func<string, string?> normalize)
    {
        var unique = DeduplicateValues(values, normalize);
        if (unique.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(unique, JsonOptions);
    }

    public static string? SerializeAttributed(IEnumerable<AttributedContact> contacts, Func<string, string?> normalize)
    {
        var unique = DeduplicateAttributed(contacts, normalize);
        if (unique.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(unique, JsonOptions);
    }

    public static string? MergeEmails(string? existingJson, string? primaryEmail, IEnumerable<string?> additional)
    {
        var contacts = ParseAttributed(existingJson);
        if (!string.IsNullOrWhiteSpace(primaryEmail))
        {
            contacts.Add(new AttributedContact { Value = primaryEmail, Channel = "email" });
        }

        foreach (var value in additional)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                contacts.Add(new AttributedContact { Value = value, Channel = "email" });
            }
        }

        return SerializeAttributed(contacts, ProspectNormalizer.NormalizeEmail);
    }

    public static string? MergeAttributedEmails(
        string? existingJson,
        IEnumerable<AttributedContact> incoming)
    {
        var contacts = ParseAttributed(existingJson);
        foreach (var contact in incoming)
        {
            if (string.IsNullOrWhiteSpace(contact.Value))
            {
                continue;
            }

            contact.Channel = string.IsNullOrWhiteSpace(contact.Channel) ? "email" : contact.Channel;
            contacts.Add(contact);
        }

        return SerializeAttributed(contacts, ProspectNormalizer.NormalizeEmail);
    }

    public static string? MergePhones(string? existingJson, string? primaryPhone, IEnumerable<string?> additional)
    {
        var contacts = ParseAttributed(existingJson);
        if (!string.IsNullOrWhiteSpace(primaryPhone))
        {
            contacts.Add(new AttributedContact { Value = primaryPhone, Channel = "phone" });
        }

        foreach (var value in additional)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                contacts.Add(new AttributedContact { Value = value, Channel = "phone" });
            }
        }

        return SerializeAttributed(contacts, ProspectNormalizer.NormalizePhone);
    }

    public static string? MergeAttributedPhones(
        string? existingJson,
        IEnumerable<AttributedContact> incoming)
    {
        var contacts = ParseAttributed(existingJson);
        foreach (var contact in incoming)
        {
            if (string.IsNullOrWhiteSpace(contact.Value))
            {
                continue;
            }

            contact.Channel = string.IsNullOrWhiteSpace(contact.Channel) ? "phone" : contact.Channel;
            contacts.Add(contact);
        }

        return SerializeAttributed(contacts, ProspectNormalizer.NormalizePhone);
    }

    public static int CountContacts(string? emailsJson, string? phonesJson, string? primaryEmail, string? primaryPhone, string? whatsApp)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contact in ParseAttributed(emailsJson))
        {
            var key = ProspectNormalizer.NormalizeEmail(contact.Value) ?? contact.Value;
            values.Add("email:" + key);
        }

        foreach (var contact in ParseAttributed(phonesJson))
        {
            var key = ProspectNormalizer.NormalizePhone(contact.Value) ?? contact.Value;
            values.Add("phone:" + key);
        }

        if (!string.IsNullOrWhiteSpace(primaryEmail))
        {
            values.Add("email:" + (ProspectNormalizer.NormalizeEmail(primaryEmail) ?? primaryEmail));
        }

        if (!string.IsNullOrWhiteSpace(primaryPhone))
        {
            values.Add("phone:" + (ProspectNormalizer.NormalizePhone(primaryPhone) ?? primaryPhone));
        }

        if (!string.IsNullOrWhiteSpace(whatsApp))
        {
            values.Add("whatsapp:" + (ProspectNormalizer.NormalizePhone(whatsApp) ?? whatsApp));
        }

        return values.Count;
    }

    private static List<AttributedContact> FromLegacyValues(IEnumerable<string?> values)
    {
        var result = new List<AttributedContact>();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(new AttributedContact { Value = value.Trim() });
            }
        }

        return result;
    }

    private static List<string> DeduplicateValues(IEnumerable<string?> values, Func<string, string?> normalize)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            var key = normalize(trimmed) ?? trimmed.ToLowerInvariant();
            if (seen.Add(key))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static List<AttributedContact> DeduplicateAttributed(
        IEnumerable<AttributedContact> contacts,
        Func<string, string?> normalize)
    {
        var result = new List<AttributedContact>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contact in contacts)
        {
            if (string.IsNullOrWhiteSpace(contact.Value))
            {
                continue;
            }

            var trimmed = contact.Value.Trim();
            var key = (contact.Channel ?? "email") + "|" + (normalize(trimmed) ?? trimmed.ToLowerInvariant());
            if (!seen.Add(key))
            {
                var existing = result.FirstOrDefault(x =>
                    string.Equals(
                        (x.Channel ?? "email") + "|" + (normalize(x.Value) ?? x.Value),
                        key,
                        StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    PreferRicherAttribution(existing, contact);
                }

                continue;
            }

            contact.Value = trimmed;
            result.Add(contact);
        }

        return result;
    }

    private static void PreferRicherAttribution(AttributedContact existing, AttributedContact incoming)
    {
        if (string.IsNullOrWhiteSpace(existing.SourceUrl) && !string.IsNullOrWhiteSpace(incoming.SourceUrl))
        {
            existing.SourceUrl = incoming.SourceUrl;
        }

        if (string.IsNullOrWhiteSpace(existing.Context) && !string.IsNullOrWhiteSpace(incoming.Context))
        {
            existing.Context = incoming.Context;
        }

        if (string.IsNullOrWhiteSpace(existing.AttributionReasonCode)
            && !string.IsNullOrWhiteSpace(incoming.AttributionReasonCode))
        {
            existing.AttributionReasonCode = incoming.AttributionReasonCode;
        }

        if (string.Equals(existing.VerificationStatus, "extracted", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(incoming.VerificationStatus)
            && !string.Equals(incoming.VerificationStatus, "extracted", StringComparison.OrdinalIgnoreCase))
        {
            existing.VerificationStatus = incoming.VerificationStatus;
        }
    }
}

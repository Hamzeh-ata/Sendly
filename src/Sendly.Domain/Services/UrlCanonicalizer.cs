namespace Sendly.Domain.Services;

public static class UrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "gclid", "fbclid", "mc_cid", "mc_eid", "ref", "ref_src"
    };

    public static string? Canonicalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var raw = value.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            raw = "https://" + raw;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        };

        if (builder.Port is 80 or 443)
        {
            builder.Port = -1;
        }

        builder.Host = builder.Host.Trim().ToLowerInvariant();
        builder.Path = NormalizePath(builder.Path);
        builder.Query = StripTrackingQuery(uri.Query);

        return builder.Uri.ToString().TrimEnd('/');
    }

    public static string? ExtractDomain(string? value)
    {
        return ProspectNormalizer.NormalizeWebsite(value);
    }

    private static readonly string[] MultiPartPublicSuffixes =
    [
        "edu.sa", "com.sa", "gov.sa", "org.sa", "net.sa", "sch.sa",
        "co.uk", "org.uk", "ac.uk", "gov.uk",
        "com.au", "co.nz", "org.nz", "govt.nz",
        "com.eg", "com.jo", "com.ae", "co.ae"
    ];

    public static string? ExtractRegistrableDomain(string? hostOrUrl)
    {
        var host = ExtractDomain(hostOrUrl);
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalized = host.Trim().ToLowerInvariant();
        foreach (var suffix in MultiPartPublicSuffixes)
        {
            if (normalized.Equals(suffix, StringComparison.Ordinal)
                || normalized.EndsWith("." + suffix, StringComparison.Ordinal))
            {
                var prefix = normalized.Equals(suffix, StringComparison.Ordinal)
                    ? string.Empty
                    : normalized[..^(suffix.Length + 1)];
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    return suffix;
                }

                var label = prefix.Split('.', StringSplitOptions.RemoveEmptyEntries)[^1];
                return label + "." + suffix;
            }
        }

        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return parts[^2] + "." + parts[^1];
        }

        return normalized;
    }

    public static bool AreSameRegistrableDomain(string? leftHostOrUrl, string? rightHostOrUrl)
    {
        var left = ExtractRegistrableDomain(leftHostOrUrl);
        var right = ExtractRegistrableDomain(rightHostOrUrl);
        return !string.IsNullOrWhiteSpace(left)
               && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var trimmed = path.TrimEnd('/');
        return string.IsNullOrWhiteSpace(trimmed) ? "/" : trimmed;
    }

    private static string StripTrackingQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        var kept = new List<string>();
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            if (string.IsNullOrWhiteSpace(key) || TrackingParameters.Contains(key))
            {
                continue;
            }

            var value = parts.Length > 1 ? parts[1] : string.Empty;
            kept.Add($"{Uri.EscapeDataString(key)}={value}");
        }

        return kept.Count == 0 ? string.Empty : string.Join('&', kept);
    }
}

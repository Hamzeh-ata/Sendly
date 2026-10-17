namespace Sendly.Domain.Services;

public static class SequenceVariantAssigner
{
    public static string? Assign(IReadOnlyList<(string VariantKey, int TrafficPercent)> variants, int roll)
    {
        if (variants.Count == 0)
        {
            return null;
        }

        var normalized = new List<(string Key, int Weight)>();
        var total = 0;
        foreach (var variant in variants)
        {
            if (string.IsNullOrWhiteSpace(variant.VariantKey))
            {
                continue;
            }

            var weight = Math.Max(0, variant.TrafficPercent);
            if (weight == 0)
            {
                continue;
            }

            total += weight;
            normalized.Add((variant.VariantKey.Trim(), weight));
        }

        if (normalized.Count == 0)
        {
            return variants[0].VariantKey?.Trim();
        }

        if (total <= 0)
        {
            return normalized[0].Key;
        }

        var cursor = Math.Clamp(roll, 0, 99) * total / 100;
        var running = 0;
        foreach (var item in normalized)
        {
            running += item.Weight;
            if (cursor < running)
            {
                return item.Key;
            }
        }

        return normalized[^1].Key;
    }
}

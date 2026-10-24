namespace Sendly.Application.Services;

public static class ArabicIndustryLexicon
{
    private static readonly Dictionary<string, string[]> Terms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sailing center"] = ["مركز شراعي", "نادي شراعي", "تدريب إبحار"],
        ["archery club"] = ["نادي رماية بالقوس", "ميدان رماية بالقوس", "رماية بالقوس"],
        ["climbing gym"] = ["صالة تسلق", "صالة تسلق صخور", "نادي تسلق"],
        ["photography studio"] = ["استوديو تصوير", "استوديو فوتوغرافي", "استوديو تصوير للإيجار"],
        ["dive center"] = ["مركز غوص", "مركز غوص سكوبا", "مدرسة غوص"],
        ["canoe rental"] = ["تأجير كانو", "تأجير قوارب كانو"],
        ["go-kart track"] = ["حلبة كارتينج", "حلبة غو كارت", "كارتينج"],
        ["art studio"] = ["استوديو فنون", "مرسم", "ورشة فنية"],
        ["bicycle workshop"] = ["ورشة دراجات", "إصلاح دراجات", "صيانة دراجات"],
        ["event equipment rental"] = ["تأجير معدات فعاليات", "تأجير معدات حفلات"],
        ["escape room"] = ["غرفة الهروب", "لعبة الهروب", "إسكيب روم"],
        ["bicycle rental"] = ["تأجير دراجات", "تأجير دراجة"],
        ["kayak rental"] = ["تأجير كاياك", "تأجير قوارب كاياك"],
        ["pottery studio"] = ["استوديو فخار", "ورشة خزف", "استوديو سيراميك"]
    };

    private static readonly Dictionary<string, string> CityTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dubai"] = "دبي",
        ["abu dhabi"] = "أبو ظبي",
        ["sharjah"] = "الشارقة",
        ["ajman"] = "عجمان",
        ["ras al khaimah"] = "رأس الخيمة",
        ["fujairah"] = "الفجيرة",
        ["al ain"] = "العين",
        ["umm al quwain"] = "أم القيوين",
        ["united arab emirates"] = "الإمارات",
        ["uae"] = "الإمارات"
    };

    public static IReadOnlyList<string> TranslateIndustry(string industry)
    {
        if (string.IsNullOrWhiteSpace(industry))
        {
            return [];
        }

        var trimmed = industry.Trim();
        if (Terms.TryGetValue(trimmed, out var mapped))
        {
            return mapped;
        }

        foreach (var pair in Terms)
        {
            if (trimmed.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)
                || pair.Key.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return [];
    }

    public static string? TranslateLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        if (CityTerms.TryGetValue(location.Trim(), out var mapped))
        {
            return mapped;
        }

        foreach (var pair in CityTerms)
        {
            if (location.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}

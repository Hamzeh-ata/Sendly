namespace Sendly.Application.Services;

public class DiscoveryBenchmarkDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Industry { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public DateOnly SampleDate { get; set; }

    public string SelectionMethod { get; set; } = string.Empty;

    public string Limitations { get; set; } = string.Empty;

    public List<DiscoveryBenchmarkSample> Reference { get; set; } = [];
}

public static class DiscoveryBenchmarkFixtures
{
    public static IReadOnlyList<DiscoveryBenchmarkDefinition> All()
        =>
        [
            new DiscoveryBenchmarkDefinition
            {
                Name = "commercial-cleaning-saudi-arabia-fixture",
                Industry = "Commercial cleaning",
                Country = "Saudi Arabia",
                Language = "ar+en",
                SampleDate = new DateOnly(2026, 10, 17),
                SelectionMethod = "Curated fixture of independently listed cleaning companies for deterministic tests.",
                Limitations = "Fixture sample of 3 companies. Not a market census. Recall only valid against this reference set.",
                Reference =
                [
                    new() { CompanyName = "Riyadh Facility Clean", WebsiteDomain = "riyadhfacilityclean.example", HasPublicContact = true },
                    new() { CompanyName = "Jeddah Commercial Hygiene", WebsiteDomain = "jeddahhygiene.example", HasPublicContact = true },
                    new() { CompanyName = "Dammam Building Care", WebsiteDomain = "dammambuildingcare.example", HasPublicContact = false }
                ]
            },
            new DiscoveryBenchmarkDefinition
            {
                Name = "private-schools-uae-fixture",
                Industry = "Education",
                Country = "United Arab Emirates",
                Language = "en",
                SampleDate = new DateOnly(2026, 10, 17),
                SelectionMethod = "Curated fixture of private school domains for deterministic tests.",
                Limitations = "Fixture sample of 3 schools. Not comprehensive UAE education coverage.",
                Reference =
                [
                    new() { CompanyName = "Dubai Horizon Academy", WebsiteDomain = "dubaihorizon.example", HasPublicContact = true },
                    new() { CompanyName = "Abu Dhabi Scholars School", WebsiteDomain = "adscholars.example", HasPublicContact = true },
                    new() { CompanyName = "Sharjah Learning Hub", WebsiteDomain = "sharjahlearning.example", HasPublicContact = false }
                ]
            },
            new DiscoveryBenchmarkDefinition
            {
                Name = "construction-egypt-fixture",
                Industry = "Construction",
                Country = "Egypt",
                Language = "ar+en",
                SampleDate = new DateOnly(2026, 10, 17),
                SelectionMethod = "Curated fixture of construction contractors for deterministic tests.",
                Limitations = "Fixture sample of 3 contractors. Does not represent the Egyptian construction market.",
                Reference =
                [
                    new() { CompanyName = "Cairo Build Partners", WebsiteDomain = "cairobuild.example", HasPublicContact = true },
                    new() { CompanyName = "Alexandria Civil Works", WebsiteDomain = "alexcivil.example", HasPublicContact = false },
                    new() { CompanyName = "Giza Structure Co", WebsiteDomain = "gizastructure.example", HasPublicContact = true }
                ]
            }
        ];
}

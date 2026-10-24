namespace Sendly.Application.Abstractions;

public static class KeywordTermKinds
{
    public const string Exact = "exact";
    public const string Synonym = "synonym";
    public const string Translation = "translation";
    public const string BroaderRelated = "broader_related";
    public const string LocalVariant = "local_variant";
}

public static class KeywordSearchUses
{
    public const string PrimaryDiscovery = "primary_discovery";
    public const string AlternateDiscovery = "alternate_discovery";
    public const string ExclusionFilter = "exclusion_filter";
    public const string Enrichment = "enrichment";
}

public static class SourceCategoryMatchKinds
{
    public const string Exact = "exact";
    public const string Approximate = "approximate";
    public const string Exploratory = "exploratory";
    public const string Unsupported = "unsupported";
}

public class MapKeywordTerm
{
    public string Term { get; set; } = string.Empty;

    public string Kind { get; set; } = KeywordTermKinds.Exact;

    public string? Language { get; set; }

    public string SearchUse { get; set; } = KeywordSearchUses.PrimaryDiscovery;

    public string? AmbiguityNotes { get; set; }
}

public class MapKeywordFamily
{
    public string FamilyKey { get; set; } = string.Empty;

    public string OriginatingRequirement { get; set; } = string.Empty;

    public string BusinessConcept { get; set; } = string.Empty;

    public bool IsPrimaryTarget { get; set; } = true;

    public bool IsMandatory { get; set; } = true;

    public List<MapKeywordTerm> Terms { get; set; } = [];

    public List<string> KnownExclusions { get; set; } = [];

    public List<string> Ambiguities { get; set; } = [];
}

public class MapSourceCategoryMapping
{
    public string FamilyKey { get; set; } = string.Empty;

    public string OriginatingRequirement { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public string SourceCategory { get; set; } = string.Empty;

    public string MatchKind { get; set; } = SourceCategoryMatchKinds.Approximate;

    public string RelevanceReason { get; set; } = string.Empty;

    public string? AmbiguityNotes { get; set; }

    public bool IsSupported { get; set; } = true;
}

public class MapGeographicCellPlan
{
    public string RegionLabel { get; set; } = string.Empty;

    public string? CountryCode { get; set; }

    public double South { get; set; }

    public double West { get; set; }

    public double North { get; set; }

    public double East { get; set; }

    public string KeywordFamilyKey { get; set; } = string.Empty;

    public string SourceName { get; set; } = "OpenStreetMapOverpass";

    public List<string> SourceCategories { get; set; } = [];
}

public class MapResearchPlanValidationIssue
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = "error";
}

public class MapResearchPlan
{
    public ResearchSpecification Specification { get; set; } = new();

    public List<MapKeywordFamily> KeywordFamilies { get; set; } = [];

    public List<MapSourceCategoryMapping> SourceCategoryMappings { get; set; } = [];

    public List<MapGeographicCellPlan> GeographicCells { get; set; } = [];

    public List<string> UnsupportedCategories { get; set; } = [];

    public List<MapResearchPlanValidationIssue> ValidationIssues { get; set; } = [];

    public List<string> GenerationLog { get; set; } = [];

    public int MaxOverpassRequests { get; set; } = 40;

    public int MaxRecordsPerRun { get; set; } = 200;

    public bool IsValid { get; set; }
}

public class MapPlaceRecord
{
    public string SourceName { get; set; } = "OpenStreetMapOverpass";

    public string SourceObjectKey { get; set; } = string.Empty;

    public string OsmElementType { get; set; } = string.Empty;

    public string OsmElementId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? AlternativeName { get; set; }

    public string? Operator { get; set; }

    public string? Description { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string? StreetAddress { get; set; }

    public string? City { get; set; }

    public string? Postcode { get; set; }

    public string? Country { get; set; }

    public string? Website { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Fax { get; set; }

    public string? OpeningHours { get; set; }

    public string? CategoryTags { get; set; }

    public string? SourceUrl { get; set; }

    public string KeywordFamilyKey { get; set; } = string.Empty;

    public string? MatchedSourceCategory { get; set; }

    public string? RawTagsJson { get; set; }

    public Dictionary<string, string> RawTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Phones { get; set; } = [];

    public List<string> Emails { get; set; } = [];

    public List<string> Websites { get; set; } = [];

    public List<string> Faxes { get; set; } = [];

    public Dictionary<string, MapFieldProvenance> FieldProvenance { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class MapFieldProvenance
{
    public string FieldName { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string? OriginalValue { get; set; }

    public string? SourceTagKey { get; set; }

    public string SourceIdentifier { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public DateTimeOffset RetrievedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string ExtractionMethod { get; set; } = "structured_tag";

    public string VerificationStatus { get; set; } = "source_reported";

    public string? SupportingExcerpt { get; set; }
}

public class MapSourceQueryRequest
{
    public double South { get; set; }

    public double West { get; set; }

    public double North { get; set; }

    public double East { get; set; }

    public List<string> SourceCategories { get; set; } = [];

    public List<string> NameKeywords { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public string KeywordFamilyKey { get; set; } = string.Empty;

    public string? CountryCode { get; set; }

    public int Limit { get; set; } = 50;
}

public class MapSourceQueryResult
{
    public bool Succeeded { get; set; }

    public bool ShouldSubdivide { get; set; }

    public bool IsRateLimited { get; set; }

    public bool IsUnsupported { get; set; }

    public string? ErrorSummary { get; set; }

    public List<MapPlaceRecord> Places { get; set; } = [];

    public int? RetryAfterSeconds { get; set; }
}

public class MapKeywordFamilyGenerationRequest
{
    public ResearchSpecification Specification { get; set; } = new();

    public List<string> ProjectKeywords { get; set; } = [];

    public List<string> MandatoryKeywords { get; set; } = [];

    public List<string> OptionalKeywords { get; set; } = [];
}

public class MapKeywordFamilyGenerationResult
{
    public List<MapKeywordFamily> Families { get; set; } = [];

    public bool IsValid { get; set; }

    public string? ErrorSummary { get; set; }
}

public class MapSourceCategoryMappingRequest
{
    public ResearchSpecification Specification { get; set; } = new();

    public List<MapKeywordFamily> KeywordFamilies { get; set; } = [];

    public List<OsmTagCapability> AvailableTags { get; set; } = [];
}

public class MapSourceCategoryMappingResult
{
    public List<MapSourceCategoryMapping> Mappings { get; set; } = [];

    public List<string> UnsupportedConcepts { get; set; } = [];

    public bool IsValid { get; set; }

    public string? ErrorSummary { get; set; }
}

public class OsmTagCapability
{
    public string Tag { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> ConceptTokens { get; set; } = [];
}

public interface IMapGeographicSource
{
    string Name { get; }

    bool IsEnabled { get; }

    Task<MapSourceQueryResult> QueryAsync(MapSourceQueryRequest request, CancellationToken cancellationToken);
}

public interface IMapResearchIntelligence
{
    Task<MapKeywordFamilyGenerationResult> GenerateKeywordFamiliesAsync(
        MapKeywordFamilyGenerationRequest request,
        CancellationToken cancellationToken);

    Task<MapSourceCategoryMappingResult> MapSourceCategoriesAsync(
        MapSourceCategoryMappingRequest request,
        CancellationToken cancellationToken);
}

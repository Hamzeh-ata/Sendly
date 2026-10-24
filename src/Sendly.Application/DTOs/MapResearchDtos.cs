using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class StartMapResearchRequestDto
{
    public Guid? ResearchQueryId { get; set; }

    public Guid? ResearchQueryVersionId { get; set; }

    public int RequestedProspectCount { get; set; } = 50;

    public int? MinQualificationScore { get; set; }
}

public class MapMarkerDto
{
    public Guid ProspectId { get; set; }

    public Guid? ResearchRunId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? Industry { get; set; }

    public string? Website { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Fax { get; set; }

    public string? StreetAddress { get; set; }

    public string? OpeningHours { get; set; }

    public string? SourceName { get; set; }

    public string? SourceUrl { get; set; }

    public string? SourceObjectKey { get; set; }

    public string? CategoryTags { get; set; }

    public ProspectStatus Status { get; set; }

    public bool IsQualified { get; set; }

    public int? QualificationScore { get; set; }

    public List<string> MissingFields { get; set; } = [];

    public List<string> AdditionalPhones { get; set; } = [];

    public List<string> AdditionalEmails { get; set; } = [];

    public List<MapFieldProvenanceDto> FieldProvenance { get; set; } = [];
}

public class MapFieldProvenanceDto
{
    public string FieldName { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string? SourceUrl { get; set; }

    public string? SourceTagKey { get; set; }

    public string ExtractionMethod { get; set; } = "structured_tag";

    public string VerificationStatus { get; set; } = "source_reported";

    public string? SupportingExcerpt { get; set; }

    public DateTimeOffset RetrievedAtUtc { get; set; }
}

public class MapContactDiagnosticsRequestDto
{
    public double South { get; set; }

    public double West { get; set; }

    public double North { get; set; }

    public double East { get; set; }

    public string? CountryCode { get; set; }

    public List<string>? SourceCategories { get; set; }

    public List<string>? NameKeywords { get; set; }

    public int Limit { get; set; } = 10;
}

public class MapContactDiagnosticsDto
{
    public Guid ProjectId { get; set; }

    public string SourceName { get; set; } = string.Empty;

    public bool HttpSucceeded { get; set; }

    public string? SourceError { get; set; }

    public int ElementsReceived { get; set; }

    public Dictionary<string, int> ElementsWithTag { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> MappedFieldCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int FieldsMappedToInternalModel { get; set; }

    public int ProspectsWithPhone { get; set; }

    public int ProspectsWithEmail { get; set; }

    public int ProspectsWithWebsite { get; set; }

    public int ApiMarkersReturned { get; set; }

    public List<MapContactDiagnosticSampleDto> Samples { get; set; } = [];

    public List<string> Errors { get; set; } = [];

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public class MapContactReenrichResultDto
{
    public Guid ResearchRunId { get; set; }

    public int ProspectsProcessed { get; set; }

    public int PhonesBefore { get; set; }

    public int PhonesAfter { get; set; }

    public int EmailsBefore { get; set; }

    public int EmailsAfter { get; set; }

    public int WebsitesAfter { get; set; }
}

public class MapOpenAiContactExtractResultDto
{
    public Guid ProspectId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string? Message { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Website { get; set; }

    public List<string> FilledFields { get; set; } = [];
}

public class MapOpenAiContactBatchResultDto
{
    public Guid ResearchRunId { get; set; }

    public int Processed { get; set; }

    public int Improved { get; set; }

    public List<MapOpenAiContactExtractResultDto> Results { get; set; } = [];
}

public class MapContactDiagnosticSampleDto
{
    public string SourceObjectKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? RawPhoneTag { get; set; }

    public string? RawEmailTag { get; set; }

    public string? RawWebsiteTag { get; set; }

    public bool MappedPhone { get; set; }

    public bool MappedEmail { get; set; }

    public bool MappedWebsite { get; set; }

    public string? PersistedPhone { get; set; }

    public string? PersistedEmail { get; set; }

    public string? PersistedWebsite { get; set; }

    public List<string> DroppedFields { get; set; } = [];
}

public class MapCoverageRegionDto
{
    public Guid Id { get; set; }

    public Guid ResearchRunId { get; set; }

    public string RegionLabel { get; set; } = string.Empty;

    public string? CountryCode { get; set; }

    public double South { get; set; }

    public double West { get; set; }

    public double North { get; set; }

    public double East { get; set; }

    public string KeywordFamilyKey { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    public MapCoverageRegionStatus Status { get; set; }

    public int RecordsFound { get; set; }

    public int UniqueRecordsFound { get; set; }

    public string? LastError { get; set; }

    public string? CoverageNotes { get; set; }
}

public class MapResearchOverviewDto
{
    public Guid? ActiveRunId { get; set; }

    public ResearchRunStatus? ActiveRunStatus { get; set; }

    public int ProgressPercent { get; set; }

    public int MarkersCount { get; set; }

    public int RegionsTotal { get; set; }

    public int RegionsCompleted { get; set; }

    public int RegionsPending { get; set; }

    public int RegionsFailed { get; set; }

    public int PlacesDiscovered { get; set; }

    public int ProspectsSaved { get; set; }

    public string? CoverageSummaryJson { get; set; }

    public List<string> UnsupportedCategories { get; set; } = [];

    public List<MapCoverageRegionDto> Regions { get; set; } = [];

    public List<MapMarkerDto> Markers { get; set; } = [];
}

public class MapResearchPlanPreviewDto
{
    public bool IsValid { get; set; }

    public List<string> GenerationLog { get; set; } = [];

    public List<string> KeywordFamilies { get; set; } = [];

    public List<string> SourceCategories { get; set; } = [];

    public List<string> UnsupportedCategories { get; set; } = [];

    public int GeographicCellCount { get; set; }

    public List<string> ValidationIssues { get; set; } = [];
}

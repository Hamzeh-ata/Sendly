import { ProspectStatus, ResearchRunStatus } from './prospecting.model';

export type MapCoverageRegionStatus =
  | 'pending'
  | 'inProgress'
  | 'completed'
  | 'failed'
  | 'subdivided'
  | 'cancelled'
  | 'unsupported';

export interface StartMapResearchRequest {
  researchQueryId?: string | null;
  researchQueryVersionId?: string | null;
  requestedProspectCount?: number;
  minQualificationScore?: number | null;
}

export interface MapFieldProvenanceDto {
  fieldName: string;
  value?: string | null;
  sourceUrl?: string | null;
  sourceTagKey?: string | null;
  extractionMethod: string;
  verificationStatus: string;
  supportingExcerpt?: string | null;
  retrievedAtUtc: string;
}

export interface MapMarkerDto {
  prospectId: string;
  researchRunId?: string | null;
  companyName: string;
  latitude: number;
  longitude: number;
  city?: string | null;
  country?: string | null;
  industry?: string | null;
  website?: string | null;
  phone?: string | null;
  email?: string | null;
  fax?: string | null;
  streetAddress?: string | null;
  openingHours?: string | null;
  sourceName?: string | null;
  sourceUrl?: string | null;
  sourceObjectKey?: string | null;
  categoryTags?: string | null;
  status: ProspectStatus;
  isQualified: boolean;
  qualificationScore?: number | null;
  missingFields: string[];
  additionalPhones?: string[];
  additionalEmails?: string[];
  fieldProvenance?: MapFieldProvenanceDto[];
}

export interface MapCoverageRegionDto {
  id: string;
  researchRunId: string;
  regionLabel: string;
  countryCode?: string | null;
  south: number;
  west: number;
  north: number;
  east: number;
  keywordFamilyKey: string;
  sourceName: string;
  status: MapCoverageRegionStatus;
  recordsFound: number;
  uniqueRecordsFound: number;
  lastError?: string | null;
  coverageNotes?: string | null;
}

export interface MapResearchOverviewDto {
  activeRunId?: string | null;
  activeRunStatus?: ResearchRunStatus | null;
  progressPercent: number;
  markersCount: number;
  regionsTotal: number;
  regionsCompleted: number;
  regionsPending: number;
  regionsFailed: number;
  placesDiscovered: number;
  prospectsSaved: number;
  coverageSummaryJson?: string | null;
  unsupportedCategories: string[];
  regions: MapCoverageRegionDto[];
  markers: MapMarkerDto[];
}

export interface MapResearchPlanPreviewDto {
  isValid: boolean;
  generationLog: string[];
  keywordFamilies: string[];
  sourceCategories: string[];
  unsupportedCategories: string[];
  geographicCellCount: number;
  validationIssues: string[];
}

export interface MapProjectOption {
  id: string;
  name: string;
}

export interface MapStatusFilterOption {
  value: string;
  labelKey: string;
  selected: boolean;
}

export interface MapFieldProvenanceView {
  fieldLabelKey: string;
  valueLabel: string;
  methodLabelKey: string;
  statusLabelKey: string;
  sourceUrl: string;
  hasSourceUrl: boolean;
  excerptLabel: string;
  hasExcerpt: boolean;
}

export interface MapMarkerDetailView {
  prospectId: string;
  companyName: string;
  statusLabelKey: string;
  qualifiedLabelKey: string;
  scoreLabel: string;
  locationLabel: string;
  industryLabel: string;
  websiteLabel: string;
  phoneLabel: string;
  emailLabel: string;
  faxLabel: string;
  streetAddressLabel: string;
  openingHoursLabel: string;
  sourceNameLabel: string;
  sourceUrl: string;
  hasSourceUrl: boolean;
  categoryTagsLabel: string;
  missingFields: string[];
  hasMissingFields: boolean;
  additionalPhonesLabel: string;
  additionalEmailsLabel: string;
  hasAdditionalContacts: boolean;
  emailRows: string[];
  phoneRows: string[];
  hasFetchedEmails: boolean;
  hasFetchedPhones: boolean;
  hasFetchedContacts: boolean;
  prospectDetailUrl: string;
  provenanceRows: MapFieldProvenanceView[];
  hasProvenance: boolean;
}

export interface MapRegionRow {
  id: string;
  regionLabel: string;
  countryCodeLabel: string;
  keywordFamilyKey: string;
  sourceName: string;
  statusLabelKey: string;
  recordsFound: number;
  uniqueRecordsFound: number;
  lastErrorLabel: string;
}

export interface MapMetricTile {
  labelKey: string;
  value: string;
  tone: 'default' | 'active' | 'success' | 'warning' | 'danger';
}

export interface MapOpenAiContactExtractResult {
  prospectId: string;
  companyName: string;
  success: boolean;
  message?: string | null;
  email?: string | null;
  phone?: string | null;
  website?: string | null;
  filledFields: string[];
}

export interface MapOpenAiContactBatchResult {
  researchRunId: string;
  processed: number;
  improved: number;
  results: MapOpenAiContactExtractResult[];
}

export interface MapResultRow {
  prospectId: string;
  companyName: string;
  locationLabel: string;
  emailLabel: string;
  phoneLabel: string;
  websiteLabel: string;
  statusLabelKey: string;
  missingLabel: string;
  hasMissingContacts: boolean;
  prospectDetailUrl: string;
  extracting: boolean;
}

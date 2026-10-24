export interface UpsertResearchSeedUrl {
  url: string;
  label?: string | null;
  isActive: boolean;
}

export interface UpsertResearchQuery {
  name: string;
  description?: string | null;
  plainLanguageBrief?: string | null;
  targetIndustry?: string | null;
  businessCategories: string[];
  targetCountry?: string | null;
  regions: string[];
  cities: string[];
  companyTypes: string[];
  companySizes: string[];
  requiredCriteria: string[];
  exclusionCriteria: string[];
  requiredContactInfo: string[];
  requiredEvidence: string[];
  discoveryStrategies: string[];
  maxPagesPerDomain: number;
  maxCrawlDepth: number;
  requestTimeoutSeconds: number;
  maxConcurrentRequests: number;
  requestsPerDomainPerMinute: number;
  maxUrlsPerRun: number;
  requireOfficialWebsite: boolean;
  requirePublicBusinessEmail: boolean;
  enableDirectoryDiscovery: boolean;
  enableSitemapDiscovery: boolean;
  enableLinkExpansion: boolean;
  enablePriorResearchUrls: boolean;
  seedUrls: UpsertResearchSeedUrl[];
}

export interface ResearchSeedUrl {
  id: string;
  url: string;
  normalizedUrl: string;
  normalizedDomain?: string | null;
  label?: string | null;
  isActive: boolean;
  createdAtUtc: string;
}

export interface ResearchQueryVersion {
  id: string;
  versionNumber: number;
  plainLanguageBrief?: string | null;
  targetIndustry?: string | null;
  businessCategories: string[];
  targetCountry?: string | null;
  regions: string[];
  cities: string[];
  companyTypes: string[];
  companySizes: string[];
  requiredCriteria: string[];
  exclusionCriteria: string[];
  requiredContactInfo: string[];
  requiredEvidence: string[];
  discoveryStrategies: string[];
  maxPagesPerDomain: number;
  maxCrawlDepth: number;
  requestTimeoutSeconds: number;
  maxConcurrentRequests: number;
  requestsPerDomainPerMinute: number;
  maxUrlsPerRun: number;
  requireOfficialWebsite: boolean;
  requirePublicBusinessEmail: boolean;
  enableDirectoryDiscovery: boolean;
  enableSitemapDiscovery: boolean;
  enableLinkExpansion: boolean;
  enablePriorResearchUrls: boolean;
  createdAtUtc: string;
}

export interface ResearchQuerySummary {
  id: string;
  projectId: string;
  name: string;
  description?: string | null;
  activeVersionId?: string | null;
  versionCount: number;
  activeSeedUrlCount: number;
  isArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ResearchQueryDetail extends ResearchQuerySummary {
  activeVersion?: ResearchQueryVersion | null;
  versions: ResearchQueryVersion[];
  seedUrls: ResearchSeedUrl[];
}

export interface ResearchEvidenceItem {
  id: string;
  factType: string;
  factValue?: string | null;
  confidenceKind: string;
  sourceUrl?: string | null;
  notes?: string | null;
  firstDiscoveredAtUtc: string;
  lastVerifiedAtUtc: string;
}

export interface QualificationCriterionItem {
  id: string;
  criterionKey: string;
  criterionLabel?: string | null;
  isMandatory: boolean;
  passed: boolean;
  scoreContribution: number;
  reason?: string | null;
  evidenceIds: string[];
  evaluatedAtUtc: string;
}

export interface DiscoveredUrlItem {
  id: string;
  url: string;
  normalizedDomain?: string | null;
  sourceType: string;
  sourceName?: string | null;
  discoveryReason?: string | null;
  parentDiscoveredUrlId?: string | null;
  parentUrl?: string | null;
  status: string;
  liveVerificationStatus?: string;
  discoverySourceId?: string | null;
  depth: number;
  retryCount?: number;
  rejectionReasonCode?: string | null;
  pagePurpose?: string | null;
  priorityScore?: number;
  lastError?: string | null;
  discoveredAtUtc: string;
  processedAtUtc?: string | null;
  provenanceComplete?: boolean;
}

export interface CrawledPageItem {
  id: string;
  researchRunId?: string;
  discoveredUrlId?: string | null;
  url: string;
  normalizedUrl?: string;
  normalizedDomain?: string | null;
  httpStatusCode?: number | null;
  contentType?: string | null;
  contentLengthBytes?: number | null;
  contentHash?: string | null;
  canonicalUrl?: string | null;
  finalUrl?: string | null;
  title?: string | null;
  robotsAllowed: boolean;
  blockedBySsrf?: boolean;
  wasRedirected?: boolean;
  redirectCount?: number;
  durationMs?: number | null;
  error?: string | null;
  crawlDepth: number;
  textExcerpt?: string | null;
  textExcerptAvailable?: boolean;
  crawledAtUtc: string;
}

export interface ResearchRunEventItem {
  id: string;
  researchRunId?: string;
  eventType: string;
  severity?: string;
  stage?: string;
  message?: string | null;
  dataJson?: string | null;
  durationMs?: number | null;
  relatedEntityId?: string | null;
  relatedEntityKind?: string | null;
  domain?: string | null;
  url?: string | null;
  httpStatusCode?: number | null;
  provider?: string | null;
  createdAtUtc: string;
}

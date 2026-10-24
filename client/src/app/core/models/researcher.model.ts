import { ResearchRunSummary } from './prospecting.model';

export type ResearchHubConnectionState = 'connected' | 'disconnected' | 'reconnecting';

export interface ResearchOverview {
  activeRuns: number;
  queuedRuns: number;
  completedRuns: number;
  failedRuns: number;
  cancelledRuns: number;
  pagesFetched: number;
  urlsDiscovered: number;
  domainsDiscovered: number;
  companiesFound: number;
  contactsFound: number;
  qualifiedProspects: number;
  crawlErrors: number;
  eventErrors: number;
  eventWarnings: number;
  recentRuns: ResearchRunSummary[];
  activeRunDetails: ResearchRunSummary[];
  recentCriticalErrors: ResearchErrorGroup[];
  topSources: DiscoverySourceSummary[];
  unhealthySources: DiscoverySourceSummary[];
}

export interface ResearchErrorGroup {
  category: string;
  sampleMessage: string;
  count: number;
  firstOccurredAtUtc: string;
  lastOccurredAtUtc: string;
  researchRunId?: string | null;
  domain?: string | null;
  url?: string | null;
  stage?: string | null;
  suggestedRemediation?: string | null;
  retryPossible: boolean;
}

export interface DiscoverySourceSummary {
  id: string;
  name: string;
  baseUrl: string;
  normalizedDomain?: string | null;
  sourceType: string;
  status: string;
  countriesJson?: string | null;
  industriesJson?: string | null;
  relevanceScore: number;
  freshnessScore: number;
  attemptCount: number;
  successCount: number;
  failureCount: number;
  candidatesDiscovered: number;
  uniqueCompaniesDiscovered: number;
  relevantCompaniesDiscovered: number;
  qualifiedProspectsYielded: number;
  pagesAccessible: number;
  pagesParsed: number;
  liveVerifiedCompanies: number;
  publicContactsExtracted: number;
  duplicateHits: number;
  totalProcessingMs: number;
  averageFetchMs?: number | null;
  failureRate?: number | null;
  duplicateRate?: number | null;
  lastAttemptedAtUtc?: string | null;
  lastSucceededAtUtc?: string | null;
  lastCheckedAtUtc?: string | null;
  lastError?: string | null;
  provenanceNotes?: string | null;
}

export interface ResearchEventListParams {
  severity?: string;
  stage?: string;
  eventType?: string;
  search?: string;
  afterId?: string;
  take?: number;
}

export interface ResearchPipelineStageView {
  key: string;
  labelKey: string;
  active: boolean;
  count: number;
}

export interface ResearchMetricTile {
  labelKey: string;
  value: string;
  tone: 'default' | 'active' | 'success' | 'warning' | 'danger';
}

export interface ResearchEventRow {
  id: string;
  researchRunId: string;
  eventType: string;
  severity: string;
  severityClass: string;
  stage: string;
  message: string;
  createdLabel: string;
  durationLabel: string;
  url: string;
  domain: string;
  httpStatusLabel: string;
  provider: string;
  dataJson: string;
  hasDataJson: boolean;
  relatedEntityId: string;
  relatedEntityKind: string;
  hasFetchLink: boolean;
}

export interface ResearchErrorGroupRow {
  category: string;
  sampleMessage: string;
  count: number;
  firstLabel: string;
  lastLabel: string;
  researchRunId: string;
  hasRunLink: boolean;
  domain: string;
  url: string;
  stage: string;
  remediation: string;
  hasRemediation: boolean;
}

export interface DiscoverySourceRow {
  id: string;
  name: string;
  baseUrl: string;
  sourceType: string;
  status: string;
  statusClass: string;
  candidatesDiscovered: number;
  uniqueCompaniesDiscovered: number;
  qualifiedProspectsYielded: number;
  liveVerifiedCompanies: number;
  failureRateLabel: string;
  averageFetchLabel: string;
  lastAttemptedLabel: string;
  lastError: string;
}

export interface ResearcherNavItem {
  path: string;
  labelKey: string;
  exact: boolean;
  queryParams?: Record<string, string>;
}

export const RESEARCH_PIPELINE_STAGES: string[] = [
  'Discovery',
  'Normalization',
  'Safety',
  'Queue',
  'Fetch',
  'Parse',
  'CompanyExtraction',
  'ContactExtraction',
  'Qualification',
  'ProspectMerge',
  'RunLifecycle'
];

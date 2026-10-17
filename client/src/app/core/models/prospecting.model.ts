export type DuplicateHandlingMode = 'warn' | 'exclude' | 'allow';
export type OutreachChannelType = 'email' | 'whatsApp';
export type ResearchRunStatus =
  | 'pending'
  | 'running'
  | 'completed'
  | 'completedWithFailures'
  | 'failed'
  | 'cancelled';
export type ProspectStatus =
  | 'discovered'
  | 'researching'
  | 'qualified'
  | 'needsReview'
  | 'rejected'
  | 'readyForOutreach'
  | 'contacted'
  | 'doNotContact';
export type ProspectDuplicateKind =
  | 'none'
  | 'sameProject'
  | 'sameCompany'
  | 'emailContacted'
  | 'whatsAppContacted'
  | 'crossProject'
  | 'doNotContact'
  | 'sameWebsite'
  | 'samePhone'
  | 'possibleDuplicate';
export type ProspectSourceType = 'manual' | 'website' | 'directory' | 'search' | 'import' | 'other';

export interface ProjectTemplateLink {
  id?: string;
  templateId: string;
  templateName?: string | null;
  channel: OutreachChannelType;
  matchIndustry?: string | null;
  matchCompanySize?: string | null;
  matchNotes?: string | null;
  sortOrder: number;
}

export interface UpsertOutreachProject {
  name: string;
  description?: string | null;
  researchInstructions?: string | null;
  isActive: boolean;
  minQualificationScore: number;
  duplicateHandling: DuplicateHandlingMode;
  enableEmail: boolean;
  enableWhatsApp: boolean;
  defaultRequestedProspectCount: number;
  keywords: string[];
  excludedKeywords: string[];
  countries: string[];
  industries: string[];
  excludedIndustries: string[];
  targetRoles: string[];
  templates: ProjectTemplateLink[];
}

export interface OutreachProjectSummary {
  id: string;
  name: string;
  description?: string | null;
  researchInstructions?: string | null;
  isActive: boolean;
  enableEmail: boolean;
  enableWhatsApp: boolean;
  countries: string[];
  keywords: string[];
  prospectCount: number;
  qualifiedCount: number;
  needsReviewCount: number;
  rejectedCount: number;
  validEmailCount: number;
  validWhatsAppCount: number;
  alreadyContactedCount: number;
  readyForOutreachCount: number;
  campaignsCreatedCount: number;
  lastResearchAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface OutreachProjectDetail extends OutreachProjectSummary {
  minQualificationScore: number;
  duplicateHandling: DuplicateHandlingMode;
  defaultRequestedProspectCount: number;
  excludedKeywords: string[];
  industries: string[];
  excludedIndustries: string[];
  targetRoles: string[];
  templates: ProjectTemplateLink[];
  recentResearchRuns: ResearchRunSummary[];
}

export interface ResearchSearchProviderDiagnostics {
  provider: string;
  queriesExecuted: number;
  requestsSuccessful: number;
  requestsFailed: number;
  resultsReceived: number;
  http401: number;
  http429: number;
  http5xx: number;
  timeouts: number;
}

export interface ResearchRunSummary {
  id: string;
  projectId: string;
  projectName?: string | null;
  status: ResearchRunStatus;
  requestedProspectCount: number;
  includeEmail: boolean;
  includeWhatsApp: boolean;
  minQualificationScore: number;
  progressPercent: number;
  companiesFound: number;
  contactsFound: number;
  validEmails: number;
  validWhatsApp: number;
  duplicatesFound: number;
  rejectedCount: number;
  qualifiedCount: number;
  searchQueriesExecuted: number;
  searchResultsReceived: number;
  candidatesDiscovered: number;
  candidatesAfterDeduplication: number;
  candidatesValidated: number;
  prospectsSaved: number;
  prospectsSkippedAsDuplicates: number;
  researchLimitsReached: boolean;
  researchLimitsReachedReason?: string | null;
  rejectionReasons: Record<string, number>;
  searchProviderDiagnostics?: ResearchSearchProviderDiagnostics | null;
  error?: string | null;
  warning?: string | null;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
}

export interface ResearchRunProspectItem {
  id: string;
  companyName: string;
  website?: string | null;
  city?: string | null;
  country?: string | null;
  email?: string | null;
  whatsApp?: string | null;
  sourceUrl?: string | null;
  qualificationScore?: number | null;
  status: ProspectStatus;
  isQualified: boolean;
  createdAtUtc: string;
}

export interface ResearchRunDetail extends ResearchRunSummary {
  prospects?: ResearchRunProspectItem[];
}

export interface StartResearchRequest {
  requestedProspectCount: number;
  includeEmail: boolean;
  includeWhatsApp: boolean;
  minQualificationScore?: number | null;
}

export interface ProspectSummary {
  id: string;
  projectId: string;
  projectName?: string | null;
  researchRunId?: string | null;
  companyName: string;
  website?: string | null;
  country?: string | null;
  city?: string | null;
  industry?: string | null;
  companySize?: string | null;
  contactName?: string | null;
  contactRole?: string | null;
  email?: string | null;
  phone?: string | null;
  whatsApp?: string | null;
  sourceUrl?: string | null;
  sourceUrls?: string[];
  sourceType: ProspectSourceType;
  sourceName?: string | null;
  qualificationScore?: number | null;
  emailConfidenceScore?: number | null;
  whatsAppConfidenceScore?: number | null;
  isQualified: boolean;
  status: ProspectStatus;
  duplicateKind: ProspectDuplicateKind;
  recommendedTemplateId?: string | null;
  recommendedTemplateName?: string | null;
  recommendedTemplateReason?: string | null;
  recommendedTemplateConfidence?: number | null;
  outreachCampaignId?: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface OutreachContactHistoryItem {
  id: string;
  channel: OutreachChannelType;
  contactValue: string;
  campaignId?: string | null;
  templateId?: string | null;
  contactedAtUtc: string;
}

export interface ProspectDetail extends ProspectSummary {
  companyDescription?: string | null;
  linkedInUrl?: string | null;
  additionalEmails?: string[];
  additionalPhones?: string[];
  researchNotes?: string | null;
  qualificationSummary?: string | null;
  contactHistory: OutreachContactHistoryItem[];
}

export interface CreateProspectRequest {
  projectId: string;
  companyName: string;
  website?: string | null;
  country?: string | null;
  city?: string | null;
  industry?: string | null;
  companyDescription?: string | null;
  companySize?: string | null;
  contactName?: string | null;
  contactRole?: string | null;
  email?: string | null;
  phone?: string | null;
  whatsApp?: string | null;
  linkedInUrl?: string | null;
  sourceUrl?: string | null;
  sourceType: ProspectSourceType;
  sourceName?: string | null;
  researchNotes?: string | null;
}

export interface ProspectListFilter {
  projectId?: string;
  search?: string;
  country?: string;
  industry?: string;
  contactMethods?: string[];
  statuses?: string[];
  qualifications?: string[];
  duplicates?: string[];
}

export interface PrepareOutreachRequest {
  prospectIds: string[];
  channel: OutreachChannelType;
  templateId?: string | null;
  useAiRecommendedTemplate: boolean;
  emailAccountId?: string | null;
  signatureId?: string | null;
  allowAlreadyContacted: boolean;
  delayMilliseconds?: number | null;
  batchSize?: number | null;
  maxRetryCount?: number | null;
}

export interface PrepareOutreachResponse {
  channel: OutreachChannelType;
  campaignId?: string | null;
  templateId?: string | null;
  templateName?: string | null;
  selectedCount: number;
  preparedCount: number;
  skippedDuplicateCount: number;
  skippedInvalidCount: number;
  warnings: string[];
  preparedProspectIds: string[];
}

export interface RecommendTemplateResponse {
  templateId?: string | null;
  templateName?: string | null;
  reason?: string | null;
  confidence: number;
}

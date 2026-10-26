export type SettingsTabId =
  | 'accounts'
  | 'signatures'
  | 'openai'
  | 'research'
  | 'whatsapp'
  | 'microsoft'
  | 'emailSending'
  | 'attachments'
  | 'queues';

export interface SettingsTabItem {
  id: SettingsTabId;
  labelKey: string;
}

export interface OpenAiSettings {
  apiKey: string | null;
  apiKeyConfigured: boolean;
  model: string;
  organizationId: string | null;
  projectId: string | null;
  baseUrl: string;
  timeoutSeconds: number;
}

export interface ResearchSettings {
  maxCandidatesPerRun: number;
  defaultMinQualificationScore: number;
  defaultRequestedProspectCount: number;
  providerTimeoutSeconds: number;
  enableSeedProvider: boolean;
  enableWebsiteCrawler: boolean;
  enableSearchProvider: boolean;
  enableHtmlSearchFallback: boolean;
  maxPagesPerCrawl: number;
  maxSearchResults: number;
  maxSearchQueries: number;
  maxSearchRequests: number;
  maxResearchDurationMinutes: number;
  discoveryOversampleFactor: number;
  minSearchRelevanceScore: number;
  bingSearchApiKey: string | null;
  bingSearchApiKeyConfigured: boolean;
  bingSearchEndpoint: string | null;
  googleSearchApiKey: string | null;
  googleSearchApiKeyConfigured: boolean;
  googleSearchEngineId: string | null;
  hostDenylist: string[];
}

export type ProviderConnectionStatus =
  | 'NotConfigured'
  | 'Configured'
  | 'ConnectionSuccessful'
  | 'ConnectionFailed'
  | 'Disabled'
  | 'NotChecked';

export type BraveConnectionStatus = ProviderConnectionStatus;

export interface BraveSearchSettings {
  enabled: boolean;
  apiKey: string | null;
  apiKeyConfigured: boolean;
  baseUrl: string;
  timeoutSeconds: number;
  maxResultsPerQuery: number;
  maxRequestsPerResearchRun: number;
  requestDelayMilliseconds: number;
  connectionStatus: BraveConnectionStatus;
}

export interface GooglePlacesSettings {
  enabled: boolean;
  apiKey: string | null;
  apiKeyConfigured: boolean;
  baseUrl: string;
  timeoutSeconds: number;
  maxResultsPerQuery: number;
  maxQueriesPerRequest: number;
  requestDelayMilliseconds: number;
  connectionStatus: ProviderConnectionStatus;
}

export interface SearXngSettings {
  enabled: boolean;
  baseUrl: string;
  defaultLanguage: string;
  requestTimeoutSeconds: number;
  defaultResultsPerQuery: number;
  maximumPagesPerQuery: number;
  maxRequestsPerResearchRun: number;
  requestDelayMilliseconds: number;
  allowPrivateNetworkHosts: boolean;
  basicAuthUsername: string | null;
  basicAuthPassword: string | null;
  basicAuthConfigured: boolean;
  connectionStatus: ProviderConnectionStatus;
}

export interface OllamaSettings {
  enabled: boolean;
  baseUrl: string;
  model: string;
  timeoutSeconds: number;
  maxRetries: number;
  connectionStatus: ProviderConnectionStatus;
}

export interface OllamaHealthResult {
  isReachable: boolean;
  modelAvailable: boolean;
  model: string;
  baseUrl: string;
  availableModels: string[];
  errorSummary: string | null;
}

export interface ProviderConnectionTestResult {
  success: boolean;
  status: ProviderConnectionStatus;
  message: string | null;
}

export interface WhatsAppSettings {
  accessToken: string | null;
  accessTokenConfigured: boolean;
  phoneNumberId: string;
  apiVersion: string;
  baseUrl: string;
  defaultTemplateLanguage: string;
  preferTemplateMessages: boolean;
  timeoutSeconds: number;
}

export interface MicrosoftGraphSettings {
  clientId: string;
  clientSecret: string | null;
  clientSecretConfigured: boolean;
  tenantId: string;
  redirectUri: string;
  frontendSuccessUri: string;
  frontendErrorUri: string;
}

export interface EmailSendingSettings {
  defaultDelayMilliseconds: number;
  defaultBatchSize: number;
  defaultMaxRetryCount: number;
  connectionTimeoutSeconds: number;
}

export interface AttachmentSettings {
  maxFileSizeBytes: number;
  maxTotalSizeBytes: number;
  allowedContentTypes: string[];
  storageRoot: string;
}

export interface BackgroundQueueSettings {
  provider: string;
}

export interface AppSettings {
  openAi: OpenAiSettings;
  research: ResearchSettings;
  braveSearch: BraveSearchSettings;
  googlePlaces: GooglePlacesSettings;
  searXng: SearXngSettings;
  ollama: OllamaSettings;
  whatsApp: WhatsAppSettings;
  microsoftGraph: MicrosoftGraphSettings;
  emailSending: EmailSendingSettings;
  attachments: AttachmentSettings;
  backgroundQueues: BackgroundQueueSettings;
}

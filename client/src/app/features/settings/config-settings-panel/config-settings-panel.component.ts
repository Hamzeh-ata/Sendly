import { Component, computed, effect, input, output, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import {
  AppSettings,
  BraveConnectionStatus,
  ProviderConnectionStatus,
  SettingsTabId
} from '../../../core/models/app-settings.model';

@Component({
  selector: 'app-config-settings-panel',
  imports: [TranslatePipe],
  templateUrl: './config-settings-panel.component.html',
  styleUrl: './config-settings-panel.component.scss'
})
export class ConfigSettingsPanelComponent {
  readonly settings = input.required<AppSettings>();
  readonly section = input.required<SettingsTabId>();
  readonly saving = input(false);
  readonly testingBrave = input(false);
  readonly braveConnectionStatus = input<BraveConnectionStatus>('NotConfigured');
  readonly braveConnectionMessage = input<string | null>(null);
  readonly testingSearXng = input(false);
  readonly searXngConnectionStatus = input<ProviderConnectionStatus>('NotConfigured');
  readonly searXngConnectionMessage = input<string | null>(null);
  readonly saveRequested = output<AppSettings>();
  readonly testBraveRequested = output<void>();
  readonly testSearXngRequested = output<void>();

  readonly openAiApiKey = signal('');
  readonly openAiModel = signal('');
  readonly openAiOrganizationId = signal('');
  readonly openAiProjectId = signal('');
  readonly openAiBaseUrl = signal('');
  readonly openAiTimeoutSeconds = signal(60);
  readonly openAiApiKeyConfigured = signal(false);

  readonly researchMaxCandidates = signal(200);
  readonly researchMinScore = signal(70);
  readonly researchRequestedCount = signal(50);
  readonly researchTimeout = signal(30);
  readonly researchEnableSeed = signal(false);
  readonly researchEnableCrawler = signal(false);
  readonly researchEnableSearch = signal(true);
  readonly researchEnableHtmlFallback = signal(false);
  readonly researchMaxPages = signal(5);
  readonly researchMaxResults = signal(20);
  readonly researchMaxQueries = signal(50);
  readonly researchMaxRequests = signal(100);
  readonly researchMaxDurationMinutes = signal(30);
  readonly researchOversampleFactor = signal(4);
  readonly researchMinRelevance = signal(0);
  readonly researchBingKey = signal('');
  readonly researchBingKeyConfigured = signal(false);
  readonly researchBingEndpoint = signal('');
  readonly researchGoogleKey = signal('');
  readonly researchGoogleKeyConfigured = signal(false);
  readonly researchGoogleCx = signal('');
  readonly researchHostDenylistText = signal('');

  readonly braveEnabled = signal(true);
  readonly braveApiKey = signal('');
  readonly braveApiKeyConfigured = signal(false);
  readonly braveBaseUrl = signal('https://api.search.brave.com/res/v1/web/search');
  readonly braveTimeout = signal(30);
  readonly braveMaxResults = signal(10);
  readonly braveMaxRequests = signal(100);
  readonly braveRequestDelay = signal(250);
  readonly braveStatus = signal<BraveConnectionStatus>('NotConfigured');
  readonly braveStatusLabelKey = computed(() => {
    const status = this.braveConnectionStatus() || this.braveStatus();
    return `appSettings.braveStatus.${status}`;
  });

  readonly searXngEnabled = signal(true);
  readonly searXngBaseUrl = signal('');
  readonly searXngDefaultLanguage = signal('en');
  readonly searXngTimeout = signal(30);
  readonly searXngDefaultResults = signal(10);
  readonly searXngMaxPages = signal(2);
  readonly searXngMaxRequests = signal(100);
  readonly searXngRequestDelay = signal(250);
  readonly searXngAllowPrivateHosts = signal(false);
  readonly searXngBasicAuthUsername = signal('');
  readonly searXngBasicAuthPassword = signal('');
  readonly searXngBasicAuthConfigured = signal(false);
  readonly searXngStatus = signal<ProviderConnectionStatus>('NotConfigured');
  readonly searXngStatusLabelKey = computed(() => {
    const status = this.searXngConnectionStatus() || this.searXngStatus();
    return `appSettings.searxngStatus.${status}`;
  });

  readonly whatsAppToken = signal('');
  readonly whatsAppTokenConfigured = signal(false);
  readonly whatsAppPhoneNumberId = signal('');
  readonly whatsAppApiVersion = signal('');
  readonly whatsAppBaseUrl = signal('');
  readonly whatsAppLanguage = signal('');
  readonly whatsAppPreferTemplate = signal(true);
  readonly whatsAppTimeout = signal(30);

  readonly graphClientId = signal('');
  readonly graphClientSecret = signal('');
  readonly graphClientSecretConfigured = signal(false);
  readonly graphTenantId = signal('');
  readonly graphRedirectUri = signal('');
  readonly graphSuccessUri = signal('');
  readonly graphErrorUri = signal('');

  readonly emailDelay = signal(1000);
  readonly emailBatchSize = signal(50);
  readonly emailRetries = signal(2);
  readonly emailTimeout = signal(30);

  readonly attachmentMaxFile = signal(5242880);
  readonly attachmentMaxTotal = signal(20971520);
  readonly attachmentTypesText = signal('');
  readonly attachmentStorageRoot = signal('');

  readonly queueProvider = signal('InMemory');

  readonly queueProviderOptions = [
    { value: 'InMemory', labelKey: 'appSettings.queueInMemory' },
    { value: 'Ef', labelKey: 'appSettings.queueEf' }
  ];

  constructor() {
    effect(() => {
      this.applyFromSettings(this.settings());
    });
  }

  onOpenAiApiKeyChange(value: string): void {
    this.openAiApiKey.set(value);
  }

  onOpenAiModelChange(value: string): void {
    this.openAiModel.set(value);
  }

  onOpenAiOrganizationIdChange(value: string): void {
    this.openAiOrganizationId.set(value);
  }

  onOpenAiProjectIdChange(value: string): void {
    this.openAiProjectId.set(value);
  }

  onOpenAiBaseUrlChange(value: string): void {
    this.openAiBaseUrl.set(value);
  }

  onOpenAiTimeoutChange(value: string): void {
    this.openAiTimeoutSeconds.set(this.parseIntOr(value, 60));
  }

  onResearchMaxCandidatesChange(value: string): void {
    this.researchMaxCandidates.set(this.parseIntOr(value, 200));
  }

  onResearchMinScoreChange(value: string): void {
    this.researchMinScore.set(this.parseIntOr(value, 70));
  }

  onResearchRequestedCountChange(value: string): void {
    this.researchRequestedCount.set(this.parseIntOr(value, 50));
  }

  onResearchTimeoutChange(value: string): void {
    this.researchTimeout.set(this.parseIntOr(value, 30));
  }

  onResearchEnableSeedChange(checked: boolean): void {
    this.researchEnableSeed.set(checked);
  }

  onResearchEnableCrawlerChange(checked: boolean): void {
    this.researchEnableCrawler.set(checked);
  }

  onResearchEnableSearchChange(checked: boolean): void {
    this.researchEnableSearch.set(checked);
  }

  onResearchEnableHtmlFallbackChange(checked: boolean): void {
    this.researchEnableHtmlFallback.set(checked);
  }

  onResearchMaxPagesChange(value: string): void {
    this.researchMaxPages.set(this.parseIntOr(value, 5));
  }

  onResearchMaxResultsChange(value: string): void {
    this.researchMaxResults.set(this.parseIntOr(value, 20));
  }

  onResearchMaxQueriesChange(value: string): void {
    this.researchMaxQueries.set(this.parseIntOr(value, 50));
  }

  onResearchMaxRequestsChange(value: string): void {
    this.researchMaxRequests.set(this.parseIntOr(value, 100));
  }

  onResearchMaxDurationMinutesChange(value: string): void {
    this.researchMaxDurationMinutes.set(this.parseIntOr(value, 30));
  }

  onResearchOversampleFactorChange(value: string): void {
    this.researchOversampleFactor.set(this.parseIntOr(value, 4));
  }

  onResearchMinRelevanceChange(value: string): void {
    this.researchMinRelevance.set(this.parseIntOr(value, 0));
  }

  onResearchBingKeyChange(value: string): void {
    this.researchBingKey.set(value);
  }

  onResearchBingEndpointChange(value: string): void {
    this.researchBingEndpoint.set(value);
  }

  onResearchGoogleKeyChange(value: string): void {
    this.researchGoogleKey.set(value);
  }

  onResearchGoogleCxChange(value: string): void {
    this.researchGoogleCx.set(value);
  }

  onResearchHostDenylistChange(value: string): void {
    this.researchHostDenylistText.set(value);
  }

  onBraveEnabledChange(checked: boolean): void {
    this.braveEnabled.set(checked);
    if (checked) {
      this.researchEnableSearch.set(true);
    }
  }

  onBraveApiKeyChange(value: string): void {
    this.braveApiKey.set(value);
  }

  onBraveBaseUrlChange(value: string): void {
    this.braveBaseUrl.set(value);
  }

  onBraveTimeoutChange(value: string): void {
    this.braveTimeout.set(this.parseIntOr(value, 30));
  }

  onBraveMaxResultsChange(value: string): void {
    this.braveMaxResults.set(this.parseIntOr(value, 10));
  }

  onBraveMaxRequestsChange(value: string): void {
    this.braveMaxRequests.set(this.parseIntOr(value, 20));
  }

  onBraveRequestDelayChange(value: string): void {
    this.braveRequestDelay.set(this.parseIntOr(value, 250));
  }

  testBraveConnection(): void {
    this.testBraveRequested.emit();
  }

  onSearXngEnabledChange(checked: boolean): void {
    this.searXngEnabled.set(checked);
  }

  onSearXngBaseUrlChange(value: string): void {
    this.searXngBaseUrl.set(value);
  }

  onSearXngDefaultLanguageChange(value: string): void {
    this.searXngDefaultLanguage.set(value);
  }

  onSearXngTimeoutChange(value: string): void {
    this.searXngTimeout.set(this.parseIntOr(value, 30));
  }

  onSearXngDefaultResultsChange(value: string): void {
    this.searXngDefaultResults.set(this.parseIntOr(value, 10));
  }

  onSearXngMaxPagesChange(value: string): void {
    this.searXngMaxPages.set(this.parseIntOr(value, 2));
  }

  onSearXngMaxRequestsChange(value: string): void {
    this.searXngMaxRequests.set(this.parseIntOr(value, 100));
  }

  onSearXngRequestDelayChange(value: string): void {
    this.searXngRequestDelay.set(this.parseIntOr(value, 250));
  }

  onSearXngAllowPrivateHostsChange(checked: boolean): void {
    this.searXngAllowPrivateHosts.set(checked);
  }

  onSearXngBasicAuthUsernameChange(value: string): void {
    this.searXngBasicAuthUsername.set(value);
  }

  onSearXngBasicAuthPasswordChange(value: string): void {
    this.searXngBasicAuthPassword.set(value);
  }

  testSearXngConnection(): void {
    this.testSearXngRequested.emit();
  }

  onWhatsAppTokenChange(value: string): void {
    this.whatsAppToken.set(value);
  }

  onWhatsAppPhoneNumberIdChange(value: string): void {
    this.whatsAppPhoneNumberId.set(value);
  }

  onWhatsAppApiVersionChange(value: string): void {
    this.whatsAppApiVersion.set(value);
  }

  onWhatsAppBaseUrlChange(value: string): void {
    this.whatsAppBaseUrl.set(value);
  }

  onWhatsAppLanguageChange(value: string): void {
    this.whatsAppLanguage.set(value);
  }

  onWhatsAppPreferTemplateChange(checked: boolean): void {
    this.whatsAppPreferTemplate.set(checked);
  }

  onWhatsAppTimeoutChange(value: string): void {
    this.whatsAppTimeout.set(this.parseIntOr(value, 30));
  }

  onGraphClientIdChange(value: string): void {
    this.graphClientId.set(value);
  }

  onGraphClientSecretChange(value: string): void {
    this.graphClientSecret.set(value);
  }

  onGraphTenantIdChange(value: string): void {
    this.graphTenantId.set(value);
  }

  onGraphRedirectUriChange(value: string): void {
    this.graphRedirectUri.set(value);
  }

  onGraphSuccessUriChange(value: string): void {
    this.graphSuccessUri.set(value);
  }

  onGraphErrorUriChange(value: string): void {
    this.graphErrorUri.set(value);
  }

  onEmailDelayChange(value: string): void {
    this.emailDelay.set(this.parseIntOr(value, 1000));
  }

  onEmailBatchSizeChange(value: string): void {
    this.emailBatchSize.set(this.parseIntOr(value, 50));
  }

  onEmailRetriesChange(value: string): void {
    this.emailRetries.set(this.parseIntOr(value, 2));
  }

  onEmailTimeoutChange(value: string): void {
    this.emailTimeout.set(this.parseIntOr(value, 30));
  }

  onAttachmentMaxFileChange(value: string): void {
    this.attachmentMaxFile.set(this.parseIntOr(value, 5242880));
  }

  onAttachmentMaxTotalChange(value: string): void {
    this.attachmentMaxTotal.set(this.parseIntOr(value, 20971520));
  }

  onAttachmentTypesChange(value: string): void {
    this.attachmentTypesText.set(value);
  }

  onAttachmentStorageRootChange(value: string): void {
    this.attachmentStorageRoot.set(value);
  }

  onQueueProviderChange(value: string): void {
    this.queueProvider.set(value);
  }

  save(): void {
    const current = this.settings();
    this.saveRequested.emit({
      openAi: {
        apiKey: this.openAiApiKey().trim() || null,
        apiKeyConfigured: this.openAiApiKeyConfigured(),
        model: this.openAiModel().trim() || 'gpt-4o-mini',
        organizationId: this.openAiOrganizationId().trim() || null,
        projectId: this.openAiProjectId().trim() || null,
        baseUrl: this.openAiBaseUrl().trim() || 'https://api.openai.com/v1',
        timeoutSeconds: this.openAiTimeoutSeconds()
      },
      research: {
        maxCandidatesPerRun: this.researchMaxCandidates(),
        defaultMinQualificationScore: this.researchMinScore(),
        defaultRequestedProspectCount: this.researchRequestedCount(),
        providerTimeoutSeconds: this.researchTimeout(),
        enableSeedProvider: this.researchEnableSeed(),
        enableWebsiteCrawler: this.researchEnableCrawler(),
        enableSearchProvider: this.researchEnableSearch(),
        enableHtmlSearchFallback: this.researchEnableHtmlFallback(),
        maxPagesPerCrawl: this.researchMaxPages(),
        maxSearchResults: this.researchMaxResults(),
        maxSearchQueries: this.researchMaxQueries(),
        maxSearchRequests: this.researchMaxRequests(),
        maxResearchDurationMinutes: this.researchMaxDurationMinutes(),
        discoveryOversampleFactor: this.researchOversampleFactor(),
        minSearchRelevanceScore: this.researchMinRelevance(),
        bingSearchApiKey: this.researchBingKey().trim() || null,
        bingSearchApiKeyConfigured: this.researchBingKeyConfigured(),
        bingSearchEndpoint: this.researchBingEndpoint().trim() || null,
        googleSearchApiKey: this.researchGoogleKey().trim() || null,
        googleSearchApiKeyConfigured: this.researchGoogleKeyConfigured(),
        googleSearchEngineId: this.researchGoogleCx().trim() || null,
        hostDenylist: this.splitLines(this.researchHostDenylistText())
      },
      braveSearch: {
        enabled: this.braveEnabled(),
        apiKey: this.braveApiKey().trim() || null,
        apiKeyConfigured: this.braveApiKeyConfigured(),
        baseUrl: this.braveBaseUrl().trim() || 'https://api.search.brave.com/res/v1/web/search',
        timeoutSeconds: this.braveTimeout(),
        maxResultsPerQuery: this.braveMaxResults(),
        maxRequestsPerResearchRun: this.braveMaxRequests(),
        requestDelayMilliseconds: this.braveRequestDelay(),
        connectionStatus: this.braveStatus()
      },
      searXng: {
        enabled: this.searXngEnabled(),
        baseUrl: this.searXngBaseUrl().trim(),
        defaultLanguage: this.searXngDefaultLanguage().trim() || 'en',
        requestTimeoutSeconds: this.searXngTimeout(),
        defaultResultsPerQuery: this.searXngDefaultResults(),
        maximumPagesPerQuery: this.searXngMaxPages(),
        maxRequestsPerResearchRun: this.searXngMaxRequests(),
        requestDelayMilliseconds: this.searXngRequestDelay(),
        allowPrivateNetworkHosts: this.searXngAllowPrivateHosts(),
        basicAuthUsername: this.searXngBasicAuthUsername().trim() || null,
        basicAuthPassword: this.searXngBasicAuthPassword().trim() || null,
        basicAuthConfigured: this.searXngBasicAuthConfigured(),
        connectionStatus: this.searXngStatus()
      },
      whatsApp: {
        accessToken: this.whatsAppToken().trim() || null,
        accessTokenConfigured: this.whatsAppTokenConfigured(),
        phoneNumberId: this.whatsAppPhoneNumberId().trim(),
        apiVersion: this.whatsAppApiVersion().trim() || 'v21.0',
        baseUrl: this.whatsAppBaseUrl().trim() || 'https://graph.facebook.com',
        defaultTemplateLanguage: this.whatsAppLanguage().trim() || 'en',
        preferTemplateMessages: this.whatsAppPreferTemplate(),
        timeoutSeconds: this.whatsAppTimeout()
      },
      microsoftGraph: {
        clientId: this.graphClientId().trim(),
        clientSecret: this.graphClientSecret().trim() || null,
        clientSecretConfigured: this.graphClientSecretConfigured(),
        tenantId: this.graphTenantId().trim() || 'common',
        redirectUri: this.graphRedirectUri().trim(),
        frontendSuccessUri: this.graphSuccessUri().trim(),
        frontendErrorUri: this.graphErrorUri().trim()
      },
      emailSending: {
        defaultDelayMilliseconds: this.emailDelay(),
        defaultBatchSize: this.emailBatchSize(),
        defaultMaxRetryCount: this.emailRetries(),
        connectionTimeoutSeconds: this.emailTimeout()
      },
      attachments: {
        maxFileSizeBytes: this.attachmentMaxFile(),
        maxTotalSizeBytes: this.attachmentMaxTotal(),
        allowedContentTypes: this.splitLines(this.attachmentTypesText()),
        storageRoot: this.attachmentStorageRoot().trim() || 'App_Data/attachments'
      },
      backgroundQueues: {
        provider: this.queueProvider()
      }
    });
  }

  private applyFromSettings(settings: AppSettings): void {
    this.openAiApiKey.set('');
    this.openAiApiKeyConfigured.set(settings.openAi.apiKeyConfigured);
    this.openAiModel.set(settings.openAi.model ?? '');
    this.openAiOrganizationId.set(settings.openAi.organizationId ?? '');
    this.openAiProjectId.set(settings.openAi.projectId ?? '');
    this.openAiBaseUrl.set(settings.openAi.baseUrl ?? '');
    this.openAiTimeoutSeconds.set(settings.openAi.timeoutSeconds);

    this.researchMaxCandidates.set(settings.research.maxCandidatesPerRun);
    this.researchMinScore.set(settings.research.defaultMinQualificationScore);
    this.researchRequestedCount.set(settings.research.defaultRequestedProspectCount);
    this.researchTimeout.set(settings.research.providerTimeoutSeconds);
    this.researchEnableSeed.set(settings.research.enableSeedProvider);
    this.researchEnableCrawler.set(settings.research.enableWebsiteCrawler);
    this.researchEnableSearch.set(settings.research.enableSearchProvider);
    this.researchEnableHtmlFallback.set(settings.research.enableHtmlSearchFallback);
    this.researchMaxPages.set(settings.research.maxPagesPerCrawl);
    this.researchMaxResults.set(settings.research.maxSearchResults);
    this.researchMaxQueries.set(settings.research.maxSearchQueries ?? 50);
    this.researchMaxRequests.set(settings.research.maxSearchRequests ?? 100);
    this.researchMaxDurationMinutes.set(settings.research.maxResearchDurationMinutes ?? 30);
    this.researchOversampleFactor.set(settings.research.discoveryOversampleFactor ?? 4);
    this.researchMinRelevance.set(settings.research.minSearchRelevanceScore);
    this.researchBingKey.set('');
    this.researchBingKeyConfigured.set(settings.research.bingSearchApiKeyConfigured);
    this.researchBingEndpoint.set(settings.research.bingSearchEndpoint ?? '');
    this.researchGoogleKey.set('');
    this.researchGoogleKeyConfigured.set(settings.research.googleSearchApiKeyConfigured);
    this.researchGoogleCx.set(settings.research.googleSearchEngineId ?? '');
    this.researchHostDenylistText.set((settings.research.hostDenylist ?? []).join('\n'));

    const brave = settings.braveSearch;
    this.braveEnabled.set(brave?.enabled ?? true);
    this.braveApiKey.set('');
    this.braveApiKeyConfigured.set(brave?.apiKeyConfigured ?? false);
    this.braveBaseUrl.set(brave?.baseUrl ?? 'https://api.search.brave.com/res/v1/web/search');
    this.braveTimeout.set(brave?.timeoutSeconds ?? 30);
    this.braveMaxResults.set(brave?.maxResultsPerQuery ?? 10);
    this.braveMaxRequests.set(brave?.maxRequestsPerResearchRun ?? 20);
    this.braveRequestDelay.set(brave?.requestDelayMilliseconds ?? 250);
    this.braveStatus.set(brave?.connectionStatus ?? 'NotConfigured');

    const searXng = settings.searXng;
    this.searXngEnabled.set(searXng?.enabled ?? true);
    this.searXngBaseUrl.set(searXng?.baseUrl ?? '');
    this.searXngDefaultLanguage.set(searXng?.defaultLanguage ?? 'en');
    this.searXngTimeout.set(searXng?.requestTimeoutSeconds ?? 30);
    this.searXngDefaultResults.set(searXng?.defaultResultsPerQuery ?? 10);
    this.searXngMaxPages.set(searXng?.maximumPagesPerQuery ?? 2);
    this.searXngMaxRequests.set(searXng?.maxRequestsPerResearchRun ?? 100);
    this.searXngRequestDelay.set(searXng?.requestDelayMilliseconds ?? 250);
    this.searXngAllowPrivateHosts.set(searXng?.allowPrivateNetworkHosts ?? false);
    this.searXngBasicAuthUsername.set('');
    this.searXngBasicAuthPassword.set('');
    this.searXngBasicAuthConfigured.set(searXng?.basicAuthConfigured ?? false);
    this.searXngStatus.set(searXng?.connectionStatus ?? 'NotConfigured');

    this.whatsAppToken.set('');
    this.whatsAppTokenConfigured.set(settings.whatsApp.accessTokenConfigured);
    this.whatsAppPhoneNumberId.set(settings.whatsApp.phoneNumberId ?? '');
    this.whatsAppApiVersion.set(settings.whatsApp.apiVersion ?? '');
    this.whatsAppBaseUrl.set(settings.whatsApp.baseUrl ?? '');
    this.whatsAppLanguage.set(settings.whatsApp.defaultTemplateLanguage ?? '');
    this.whatsAppPreferTemplate.set(settings.whatsApp.preferTemplateMessages);
    this.whatsAppTimeout.set(settings.whatsApp.timeoutSeconds);

    this.graphClientId.set(settings.microsoftGraph.clientId ?? '');
    this.graphClientSecret.set('');
    this.graphClientSecretConfigured.set(settings.microsoftGraph.clientSecretConfigured);
    this.graphTenantId.set(settings.microsoftGraph.tenantId ?? '');
    this.graphRedirectUri.set(settings.microsoftGraph.redirectUri ?? '');
    this.graphSuccessUri.set(settings.microsoftGraph.frontendSuccessUri ?? '');
    this.graphErrorUri.set(settings.microsoftGraph.frontendErrorUri ?? '');

    this.emailDelay.set(settings.emailSending.defaultDelayMilliseconds);
    this.emailBatchSize.set(settings.emailSending.defaultBatchSize);
    this.emailRetries.set(settings.emailSending.defaultMaxRetryCount);
    this.emailTimeout.set(settings.emailSending.connectionTimeoutSeconds);

    this.attachmentMaxFile.set(settings.attachments.maxFileSizeBytes);
    this.attachmentMaxTotal.set(settings.attachments.maxTotalSizeBytes);
    this.attachmentTypesText.set((settings.attachments.allowedContentTypes ?? []).join('\n'));
    this.attachmentStorageRoot.set(settings.attachments.storageRoot ?? '');

    this.queueProvider.set(settings.backgroundQueues.provider || 'InMemory');
  }

  private splitLines(value: string): string[] {
    const rows: string[] = [];
    for (const line of value.split(/\r?\n/)) {
      const trimmed = line.trim();
      if (trimmed) {
        rows.push(trimmed);
      }
    }
    return rows;
  }

  private parseIntOr(value: string, fallback: number): number {
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      return fallback;
    }
    return Math.floor(parsed);
  }
}

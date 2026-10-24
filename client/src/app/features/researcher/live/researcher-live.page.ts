import { Component, computed, effect, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription, filter, switchMap, takeWhile, timer } from 'rxjs';
import { toObservable } from '@angular/core/rxjs-interop';
import {
  DiscoveryProviderRow,
  DiscoveryStageRow,
  OutreachChannelType,
  PrepareOutreachRequest,
  ResearchRunDetail,
  ResearchRunProspectItem
} from '../../../core/models/prospecting.model';
import { ResearchRunEventItem } from '../../../core/models/research-query.model';
import { OllamaHealthResult } from '../../../core/models/app-settings.model';
import { AppSettingsApiService } from '../../../core/services/app-settings-api.service';
import { ResearchRunApiService } from '../../../core/services/research-run-api.service';
import { ResearchHubService } from '../../../core/services/research-hub.service';
import { ProjectApiService } from '../../../core/services/project-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { formatDate } from '../../../core/utils/format.utils';
import {
  buildPipelineStages,
  formatElapsed,
  mapEventToRow
} from '../researcher.mappers';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import { ConnectionStatusComponent } from '../../../shared/components/researcher/connection-status.component';
import { EventRowComponent } from '../../../shared/components/researcher/event-row.component';
import { MetricTileComponent } from '../../../shared/components/researcher/metric-tile.component';
import { PipelineStageComponent } from '../../../shared/components/researcher/pipeline-stage.component';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-researcher-live-page',
  imports: [
    RouterLink,
    TranslatePipe,
    ConfirmDialogComponent,
    ConnectionStatusComponent,
    EventRowComponent,
    MetricTileComponent,
    PipelineStageComponent,
    EmptyStateComponent,
    StatusBadgeComponent
  ],
  templateUrl: './researcher-live.page.html',
  styleUrl: './researcher-live.page.scss'
})
export class ResearcherLivePage implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly appSettingsApi = inject(AppSettingsApiService);
  private readonly researchHub = inject(ResearchHubService);
  private readonly projectApi = inject(ProjectApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private pollSub: Subscription | null = null;
  private connectionSub: Subscription | null = null;
  private elapsedSub: Subscription | null = null;
  private readonly connected$ = toObservable(this.researchHub.connected);

  readonly loading = signal(true);
  readonly actionLoading = signal(false);
  readonly showCancelConfirm = signal(false);
  readonly campaignOpen = signal(false);
  readonly campaignSaving = signal(false);
  readonly campaignChannel = signal<OutreachChannelType>('email');
  readonly useAiRecommendedTemplate = signal(true);
  readonly selectedProspectIds = signal<string[]>([]);
  readonly streamPaused = signal(false);
  readonly run = signal<ResearchRunDetail | null>(null);
  readonly runId = signal('');
  readonly nowMs = signal(Date.now());
  readonly severityFilter = signal('');
  readonly stageFilter = signal('');
  readonly searchFilter = signal('');
  readonly expandedEventIds = signal<Record<string, boolean>>({});
  readonly historicalEvents = signal<ResearchRunEventItem[]>([]);
  readonly ollamaHealth = signal<OllamaHealthResult | null>(null);
  readonly pausedSnapshot = signal<ResearchRunEventItem[]>([]);

  readonly connectionState = computed(() => this.researchHub.connectionState());
  readonly isActive = computed(() => {
    const status = this.run()?.status;
    return status === 'pending' || status === 'running';
  });
  readonly titleLabel = computed(() => {
    const run = this.run();
    if (!run) {
      return '';
    }
    return run.researchQueryName || run.projectName || run.id;
  });
  readonly projectLabel = computed(() => this.run()?.projectName || '—');
  readonly startedLabel = computed(() => formatDate(this.run()?.startedAtUtc ?? null));
  readonly lastActivityAtUtc = computed(() => {
    const live = this.researchHub.eventsBuffer();
    const historical = this.historicalEvents();
    const source = live.length > 0 ? live : historical;
    const newest = source.length > 0 ? source[0] : null;
    return newest?.createdAtUtc ?? null;
  });
  readonly processingElapsedLabel = computed(() => {
    const run = this.run();
    if (!run?.startedAtUtc) {
      return '—';
    }
    const endMs = this.resolveProcessingEndMs(run);
    return formatElapsed(run.startedAtUtc, endMs);
  });
  readonly wallClockElapsedLabel = computed(() => {
    const run = this.run();
    if (!run?.startedAtUtc) {
      return '—';
    }
    if (!this.isActive() && run.completedAtUtc) {
      return formatElapsed(run.startedAtUtc, new Date(run.completedAtUtc).getTime());
    }
    return formatElapsed(run.startedAtUtc, this.nowMs());
  });
  readonly elapsedLabel = computed(() => this.processingElapsedLabel());
  readonly ollamaStatusKey = computed(() => {
    const diag = this.run()?.searchProviderDiagnostics;
    if (diag?.ollamaModelAvailable) {
      return 'researcher.live.ollamaRunning';
    }
    if (diag?.ollamaChecked) {
      return 'researcher.live.ollamaUnavailable';
    }
    const health = this.ollamaHealth();
    if (health?.modelAvailable) {
      return 'researcher.live.ollamaRunning';
    }
    if (health?.isReachable) {
      return 'researcher.live.ollamaModelMissing';
    }
    if (health) {
      return 'researcher.live.ollamaUnavailable';
    }
    return 'researcher.live.ollamaNotChecked';
  });
  readonly ollamaDetailLabel = computed(() => {
    const diag = this.run()?.searchProviderDiagnostics;
    const model = diag?.ollamaModel || this.ollamaHealth()?.model || 'qwen3:4b';
    const queries = diag?.ollamaQueriesGenerated ?? 0;
    if (diag?.ollamaChecked && queries > 0) {
      return `${model} · ${this.translate.instant('researcher.live.ollamaQueries', { count: queries })}`;
    }
    return model;
  });
  readonly ollamaTileValue = computed(() => {
    const status = this.translate.instant(this.ollamaStatusKey());
    const diag = this.run()?.searchProviderDiagnostics;
    const model = diag?.ollamaModel || this.ollamaHealth()?.model || 'qwen3:4b';
    return `${status} · ${model}`;
  });
  readonly lastEventLabel = computed(() => {
    const at = this.lastActivityAtUtc();
    return at ? formatDate(at) : '—';
  });

  readonly metricTiles = computed(() => {
    const run = this.run();
    if (!run) {
      return [];
    }
    const diag = run.searchProviderDiagnostics;
    return [
      {
        labelKey: 'researcher.live.ollama',
        value: this.ollamaTileValue(),
        tone: (diag?.ollamaModelAvailable ? 'success' : 'warning') as 'success' | 'warning'
      },
      { labelKey: 'researcher.live.urls', value: String(run.urlsDiscovered || 0), tone: 'default' as const },
      { labelKey: 'researcher.live.pages', value: String(run.pagesCrawled || 0), tone: 'active' as const },
      { labelKey: 'researcher.live.errors', value: String(run.crawlErrors || 0), tone: 'danger' as const },
      {
        labelKey: 'researcher.live.relevant',
        value: String(diag?.companiesIdentified ?? run.companiesFound ?? 0),
        tone: 'default' as const
      },
      {
        labelKey: 'researcher.live.identityVerified',
        value: String(diag?.identityVerifiedBusinesses ?? 0),
        tone: 'default' as const
      },
      {
        labelKey: 'researcher.live.emailReady',
        value: String(diag?.emailReadyProspects ?? 0),
        tone: 'success' as const
      },
      {
        labelKey: 'researcher.live.needsReview',
        value: String(this.liveProspects().filter((p) => p.status === 'needsReview').length),
        tone: 'warning' as const
      },
      {
        labelKey: 'researcher.live.previouslyContacted',
        value: String(diag?.previouslyContactedProspects ?? 0),
        tone: 'warning' as const
      },
      { labelKey: 'researcher.live.qualified', value: String(run.qualifiedCount || 0), tone: 'success' as const },
      { labelKey: 'researcher.live.duplicates', value: String(run.duplicatesFound || 0), tone: 'warning' as const }
    ];
  });

  readonly displayedEventsRaw = computed(() => {
    let source: ResearchRunEventItem[];
    if (this.streamPaused()) {
      source = this.pausedSnapshot();
    } else {
      const live = this.researchHub.eventsBuffer();
      const historical = this.historicalEvents();
      source = live.length > 0 ? live : historical;
    }
    return this.applyEventFilters(source);
  });

  readonly eventRows = computed(() => {
    const runId = this.runId();
    return this.displayedEventsRaw().map((item) => {
      const row = mapEventToRow(item);
      if (!row.researchRunId && runId) {
        row.researchRunId = runId;
      }
      return row;
    });
  });
  readonly streamSourceKey = computed(() => {
    if (this.streamPaused()) {
      return 'researcher.live.streamSourceHistorical';
    }
    if (this.researchHub.eventsBuffer().length > 0) {
      return 'researcher.live.streamSourceLive';
    }
    if (this.historicalEvents().length > 0) {
      return 'researcher.live.streamSourceHistorical';
    }
    return 'researcher.live.streamSourceNone';
  });
  readonly pipelineStages = computed(() => {
    const live = this.researchHub.eventsBuffer();
    const source = live.length > 0 ? live : this.historicalEvents();
    return buildPipelineStages(source);
  });
  readonly investigationActivityKey = computed(() => {
    const activity = this.run()?.searchProviderDiagnostics?.currentInvestigationActivity ?? '';
    switch (activity) {
      case 'searching_for_sources':
        return 'researcher.live.activity.searching';
      case 'exploring_directory':
        return 'researcher.live.activity.directory';
      case 'investigating_organization_website':
        return 'researcher.live.activity.website';
      case 'looking_for_contacts':
        return 'researcher.live.activity.contacts';
      case 'verifying_evidence':
        return 'researcher.live.activity.verifying';
      case 'waiting_for_retry':
        return 'researcher.live.activity.retry';
      case 'blocked_by_limitation':
        return 'researcher.live.activity.blocked';
      case 'search_provider_unhealthy':
        return 'researcher.live.activity.searchUnhealthy';
      case 'search_exhausted_fallback_to_other_sources':
        return 'researcher.live.activity.searchFallback';
      case 'search_exhausted':
        return 'researcher.live.activity.searchExhausted';
      case 'search_no_results':
        return 'researcher.live.activity.searchNoResults';
      case 'results_filtered':
        return 'researcher.live.activity.resultsFiltered';
      case 'crawling':
        return 'researcher.live.activity.crawling';
      case 'crawl_queue_empty':
        return 'researcher.live.activity.queueEmpty';
      case 'crawl_stalled':
        return 'researcher.live.activity.stalled';
      case 'budget_exhausted':
        return 'researcher.live.activity.budgetExhausted';
      case 'discovery_blocked':
        return 'researcher.live.activity.discoveryBlocked';
      case 'finished':
        return 'researcher.live.activity.finished';
      default:
        return 'researcher.live.activity.unknown';
    }
  });
  readonly nextBestStrategy = computed(
    () => this.run()?.searchProviderDiagnostics?.nextBestInvestigationStrategy ?? null
  );
  readonly expandedMap = computed(() => this.expandedEventIds());

  readonly liveProspects = computed(() => this.run()?.prospects ?? []);
  readonly hasLiveProspects = computed(() => this.liveProspects().length > 0);
  readonly readyCampaignProspects = computed(() =>
    this.liveProspects().filter((prospect) => this.isCampaignReady(prospect))
  );
  readonly readyCampaignCount = computed(() => this.readyCampaignProspects().length);
  readonly selectedCampaignCount = computed(() => this.selectedProspectIds().length);
  readonly canCreateCampaign = computed(
    () => this.readyCampaignCount() > 0 || this.selectedCampaignCount() > 0
  );
  readonly selectedMap = computed(() => {
    const map: Record<string, boolean> = {};
    for (const id of this.selectedProspectIds()) {
      map[id] = true;
    }
    return map;
  });

  readonly discoverySummary = computed(
    () => this.run()?.searchProviderDiagnostics?.automaticDiscoverySummary ?? null
  );
  readonly discoveryUniqueDomains = computed(
    () => this.run()?.searchProviderDiagnostics?.uniqueDomainsDiscovered ?? 0
  );
  readonly discoveryLiveVerified = computed(
    () => this.run()?.searchProviderDiagnostics?.liveVerifiedCompanies ?? 0
  );
  readonly discoveryPublicContacts = computed(
    () => this.run()?.searchProviderDiagnostics?.publicContactsExtracted ?? 0
  );
  readonly discoveryProspectsQualified = computed(
    () => this.run()?.searchProviderDiagnostics?.prospectsQualified ?? 0
  );
  readonly discoveryProviderRows = computed((): DiscoveryProviderRow[] => {
    const providers = this.run()?.searchProviderDiagnostics?.discoveryProviders ?? [];
    const rows: DiscoveryProviderRow[] = [];
    for (const item of providers) {
      let statusLabel = '—';
      if (!item.attempted) {
        statusLabel = this.translate.instant('researcher.live.providerNotAttempted');
      } else if (item.producedUsefulCandidates) {
        statusLabel = this.translate.instant('researchRuns.providersSucceeded');
      } else if (item.attempted && item.error) {
        statusLabel = item.error;
      } else if (item.zeroYieldReason) {
        statusLabel = item.zeroYieldReason;
      } else if (item.attempted) {
        statusLabel = this.translate.instant('researcher.live.providerZeroYield');
      }
      rows.push({
        providerName: item.providerName,
        statusLabel,
        candidatesReturned: item.candidatesReturned || 0,
        zeroYieldReason: item.zeroYieldReason || '—',
        producedUseful: !!item.producedUsefulCandidates
      });
    }
    return rows;
  });
  readonly stopReasonLabel = computed(
    () => this.run()?.searchProviderDiagnostics?.lastBudgetStopReason
      || this.run()?.researchLimitsReachedReason
      || this.run()?.warning
      || '—'
  );
  readonly pendingLeftLabel = computed(
    () => String(this.run()?.searchProviderDiagnostics?.usefulUrlsLeftPendingAtStop ?? 0)
  );
  readonly fetchBudgetLabel = computed(() => {
    const diag = this.run()?.searchProviderDiagnostics;
    if (!diag) {
      return '—';
    }
    return `${diag.fetchBudgetConsumed ?? 0}/${diag.fetchBudgetLimit ?? 0}`;
  });
  readonly discoveryStageRows = computed((): DiscoveryStageRow[] => {
    const stages = this.run()?.searchProviderDiagnostics?.discoveryStages ?? [];
    const rows: DiscoveryStageRow[] = [];
    for (const stage of stages) {
      rows.push({
        stageLabel: `${stage.stageNumber}. ${stage.stageName}`,
        providersLabel: (stage.providersAttempted ?? []).join(', ') || '—',
        candidatesAdded: stage.candidatesAdded || 0,
        recoveryLabel: stage.recoveryTriggered
          ? this.translate.instant('researchRuns.recoveryTriggered')
          : this.translate.instant('researchRuns.recoveryNotNeeded')
      });
    }
    return rows;
  });

  readonly severityOptions = [
    { value: '', labelKey: 'researcher.filters.allSeverities' },
    { value: 'Info', labelKey: 'researcher.severity.Info' },
    { value: 'Warning', labelKey: 'researcher.severity.Warning' },
    { value: 'Error', labelKey: 'researcher.severity.Error' },
    { value: 'Critical', labelKey: 'researcher.severity.Critical' }
  ];

  readonly stageOptions = [
    { value: '', labelKey: 'researcher.filters.allStages' },
    { value: 'Discovery', labelKey: 'researcher.stages.Discovery' },
    { value: 'Normalization', labelKey: 'researcher.stages.Normalization' },
    { value: 'Safety', labelKey: 'researcher.stages.Safety' },
    { value: 'Queue', labelKey: 'researcher.stages.Queue' },
    { value: 'Fetch', labelKey: 'researcher.stages.Fetch' },
    { value: 'Parse', labelKey: 'researcher.stages.Parse' },
    { value: 'CompanyExtraction', labelKey: 'researcher.stages.CompanyExtraction' },
    { value: 'ContactExtraction', labelKey: 'researcher.stages.ContactExtraction' },
    { value: 'Qualification', labelKey: 'researcher.stages.Qualification' },
    { value: 'ProspectMerge', labelKey: 'researcher.stages.ProspectMerge' },
    { value: 'RunLifecycle', labelKey: 'researcher.stages.RunLifecycle' }
  ];

  constructor() {
    effect(() => {
      const progress = this.researchHub.latestProgress();
      const currentId = this.runId();
      if (!progress || !currentId) {
        return;
      }
      if (this.normalizeId(progress.id) !== this.normalizeId(currentId)) {
        return;
      }
      this.run.set(progress);
      this.loading.set(false);
      if (!this.isActiveStatus(progress.status)) {
        this.stopPolling();
      }
    });
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.runId.set(id);
    if (!id) {
      this.loading.set(false);
      return;
    }
    this.researchHub.clearProgress();
    this.researchHub.clearEvents();
    this.researchHub.joinResearchRun(id);
    this.loadOllamaStatus();
    this.hydrateOnce(id);
    this.startElapsedTicker();
    this.connectionSub = this.connected$.subscribe((connected) => {
      if (!this.isActive()) {
        this.stopPolling();
        return;
      }
      if (connected) {
        this.stopPolling();
        return;
      }
      this.startPollingFallback(id);
    });
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.stopElapsedTicker();
    if (this.connectionSub) {
      this.connectionSub.unsubscribe();
      this.connectionSub = null;
    }
    this.researchHub.leaveResearchRun();
    this.researchHub.clearProgress();
    this.researchHub.clearEvents();
  }

  onSeverityChange(value: string): void {
    this.severityFilter.set(value);
  }

  onStageChange(value: string): void {
    this.stageFilter.set(value);
  }

  onSearchChange(value: string): void {
    this.searchFilter.set(value);
  }

  toggleStreamPause(): void {
    if (this.streamPaused()) {
      this.streamPaused.set(false);
      this.pausedSnapshot.set([]);
      return;
    }
    this.pausedSnapshot.set([...this.displayedEventsRaw()]);
    this.streamPaused.set(true);
  }

  toggleExpand(eventId: string): void {
    const current = { ...this.expandedEventIds() };
    current[eventId] = !current[eventId];
    this.expandedEventIds.set(current);
  }

  copyEventDetails(eventId: string): void {
    const row = this.eventRows().find((item) => item.id === eventId);
    if (!row) {
      return;
    }
    const payload = [
      row.createdLabel,
      row.severity,
      row.eventType,
      row.stage,
      row.message,
      row.url,
      row.dataJson
    ]
      .filter((part) => !!part)
      .join('\n');
    void navigator.clipboard.writeText(payload).then(
      () => this.notification.success(this.translate.instant('researcher.events.copySuccess')),
      () => this.notification.error(this.translate.instant('common.errorGeneric'))
    );
  }

  openCancelConfirm(): void {
    this.showCancelConfirm.set(true);
  }

  closeCancelConfirm(): void {
    this.showCancelConfirm.set(false);
  }

  confirmCancel(): void {
    const id = this.runId();
    if (!id) {
      return;
    }
    this.actionLoading.set(true);
    this.researchRunApi.cancel(id).subscribe({
      next: () => {
        this.actionLoading.set(false);
        this.showCancelConfirm.set(false);
        this.notification.success(this.translate.instant('researchRuns.cancelSuccess'));
        this.hydrateOnce(id);
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notifyError(err);
      }
    });
  }

  openProspect(prospectId: string): void {
    if (!prospectId) {
      return;
    }
    this.router.navigate(['/prospects', prospectId]);
  }

  toggleProspectSelection(prospectId: string, checked: boolean): void {
    const current = this.selectedProspectIds();
    if (checked) {
      if (current.includes(prospectId)) {
        return;
      }
      this.selectedProspectIds.set([...current, prospectId]);
      return;
    }
    this.selectedProspectIds.set(current.filter((id) => id !== prospectId));
  }

  selectAllReadyProspects(): void {
    this.selectedProspectIds.set(this.readyCampaignProspects().map((prospect) => prospect.id));
  }

  openCampaignForReady(): void {
    const ids = this.readyCampaignProspects().map((prospect) => prospect.id);
    if (ids.length === 0) {
      this.notification.error(this.translate.instant('researcher.live.createCampaignNone'));
      return;
    }
    this.selectedProspectIds.set(ids);
    this.campaignChannel.set(this.run()?.includeWhatsApp && !this.run()?.includeEmail ? 'whatsApp' : 'email');
    this.useAiRecommendedTemplate.set(true);
    this.campaignOpen.set(true);
  }

  openCampaignForSelected(): void {
    if (this.selectedProspectIds().length === 0) {
      this.notification.error(this.translate.instant('prospects.selectRequired'));
      return;
    }
    this.campaignChannel.set(this.run()?.includeWhatsApp && !this.run()?.includeEmail ? 'whatsApp' : 'email');
    this.useAiRecommendedTemplate.set(true);
    this.campaignOpen.set(true);
  }

  closeCampaign(): void {
    this.campaignOpen.set(false);
  }

  onCampaignChannelChange(value: string): void {
    this.campaignChannel.set(value as OutreachChannelType);
  }

  onUseAiTemplateChange(checked: boolean): void {
    this.useAiRecommendedTemplate.set(checked);
  }

  confirmCampaign(): void {
    const run = this.run();
    const prospectIds = this.selectedProspectIds();
    if (!run?.projectId || prospectIds.length === 0) {
      this.notification.error(this.translate.instant('prospects.selectRequired'));
      return;
    }
    const request: PrepareOutreachRequest = {
      prospectIds,
      channel: this.campaignChannel(),
      templateId: null,
      useAiRecommendedTemplate: this.useAiRecommendedTemplate(),
      emailAccountId: null,
      signatureId: null,
      allowAlreadyContacted: false
    };
    this.campaignSaving.set(true);
    this.projectApi.prepareOutreach(run.projectId, request).subscribe({
      next: (result) => {
        this.campaignSaving.set(false);
        this.campaignOpen.set(false);
        this.selectedProspectIds.set([]);
        this.notification.success(this.translate.instant('prospects.campaignSuccess'));
        if (result.campaignId) {
          this.router.navigate(['/history', result.campaignId]);
          return;
        }
        this.hydrateOnce(run.id);
      },
      error: (err: HttpErrorResponse) => {
        this.campaignSaving.set(false);
        this.notifyError(err);
      }
    });
  }

  openFetchExplorer(): void {
    const id = this.runId();
    if (!id) {
      return;
    }
    this.router.navigate(['/researcher/fetch'], { queryParams: { runId: id } });
  }

  private isCampaignReady(prospect: ResearchRunProspectItem): boolean {
    if (prospect.status === 'doNotContact' || prospect.status === 'contacted') {
      return false;
    }
    return !!prospect.email || !!prospect.whatsApp;
  }

  private resolveProcessingEndMs(run: ResearchRunDetail): number {
    const lastActivity = this.lastActivityAtUtc();
    if (lastActivity) {
      const activityMs = new Date(lastActivity).getTime();
      if (!Number.isNaN(activityMs)) {
        if (!this.isActive()) {
          return activityMs;
        }
        return Math.min(activityMs + 60_000, this.nowMs());
      }
    }
    if (!this.isActive() && run.completedAtUtc) {
      return new Date(run.completedAtUtc).getTime();
    }
    return this.nowMs();
  }

  private applyEventFilters(source: ResearchRunEventItem[]): ResearchRunEventItem[] {
    const severity = this.severityFilter().toLowerCase();
    const stage = this.stageFilter().toLowerCase();
    const search = this.searchFilter().trim().toLowerCase();
    const filtered: ResearchRunEventItem[] = [];
    for (const item of source) {
      if (severity && (item.severity ?? '').toLowerCase() !== severity) {
        continue;
      }
      if (stage && (item.stage ?? '').toLowerCase() !== stage) {
        continue;
      }
      if (search) {
        const haystack = `${item.message ?? ''} ${item.url ?? ''} ${item.domain ?? ''} ${item.provider ?? ''}`.toLowerCase();
        if (!haystack.includes(search)) {
          continue;
        }
      }
      filtered.push(item);
    }
    return filtered;
  }

  private loadOllamaStatus(): void {
    this.appSettingsApi.testOllama().subscribe({
      next: (result) => {
        this.ollamaHealth.set(result);
      },
      error: (err: HttpErrorResponse) => {
        const body = err.error as OllamaHealthResult | null;
        if (body && typeof body.isReachable === 'boolean') {
          this.ollamaHealth.set(body);
          return;
        }
        this.ollamaHealth.set({
          isReachable: false,
          modelAvailable: false,
          model: 'qwen3:4b',
          baseUrl: 'http://localhost:11434',
          availableModels: [],
          errorSummary: 'Ollama status could not be loaded.'
        });
      }
    });
  }

  private hydrateOnce(id: string): void {
    this.loading.set(true);
    this.researchRunApi.get(id).subscribe({
      next: (detail) => {
        this.run.set(detail);
        this.loading.set(false);
        this.reloadEvents();
        if (this.isActiveStatus(detail.status) && !this.researchHub.connected()) {
          this.startPollingFallback(id);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/researcher/runs']);
      }
    });
  }

  private reloadEvents(): void {
    const id = this.runId();
    if (!id) {
      return;
    }
    this.researchRunApi.listEvents(id, { take: 200 }).subscribe({
      next: (items) => {
        const events = items ?? [];
        this.historicalEvents.set(events);
        if (!this.streamPaused()) {
          this.researchHub.seedEvents(events);
        }
      },
      error: () => {
        this.historicalEvents.set([]);
      }
    });
  }

  private startPollingFallback(id: string): void {
    if (this.pollSub || this.researchHub.connected()) {
      return;
    }
    this.pollSub = timer(0, 5000)
      .pipe(
        filter(() => !this.researchHub.connected()),
        switchMap(() => this.researchRunApi.get(id)),
        takeWhile((detail) => this.isActiveStatus(detail.status), true)
      )
      .subscribe({
        next: (detail) => {
          this.run.set(detail);
          this.loading.set(false);
          this.reloadEvents();
        },
        error: () => {
          this.stopPolling();
        }
      });
  }

  private startElapsedTicker(): void {
    this.stopElapsedTicker();
    this.elapsedSub = timer(0, 1000).subscribe(() => {
      if (this.isActive()) {
        this.nowMs.set(Date.now());
      }
    });
  }

  private stopElapsedTicker(): void {
    if (this.elapsedSub) {
      this.elapsedSub.unsubscribe();
      this.elapsedSub = null;
    }
  }

  private isActiveStatus(status: string): boolean {
    return status === 'pending' || status === 'running';
  }

  private stopPolling(): void {
    if (this.pollSub) {
      this.pollSub.unsubscribe();
      this.pollSub = null;
    }
  }

  private normalizeId(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    return String(value).trim().toLowerCase();
  }

  private notifyError(err: HttpErrorResponse): void {
    if (err.status === 0) {
      this.notification.error(this.translate.instant('common.apiOffline'));
      return;
    }
    if (err.error?.message) {
      this.notification.error(err.error.message);
      return;
    }
    this.notification.error(this.translate.instant('common.errorGeneric'));
  }
}

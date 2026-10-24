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
} from '../../core/models/prospecting.model';
import {
  CrawledPageItem,
  DiscoveredUrlItem,
  ResearchRunEventItem
} from '../../core/models/research-query.model';
import { ResearchRunApiService } from '../../core/services/research-run-api.service';
import { ResearchHubService } from '../../core/services/research-hub.service';
import { ProjectApiService } from '../../core/services/project-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatDate } from '../../core/utils/format.utils';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-research-run-detail-page',
  imports: [
    RouterLink,
    TranslatePipe,
    LoadingComponent,
    StatusBadgeComponent,
    ConfirmDialogComponent
  ],
  templateUrl: './research-run-detail.page.html',
  styleUrl: './research-run-detail.page.scss'
})
export class ResearchRunDetailPage implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly researchHub = inject(ResearchHubService);
  private readonly projectApi = inject(ProjectApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private pollSub: Subscription | null = null;
  private connectionSub: Subscription | null = null;
  private readonly connected$ = toObservable(this.researchHub.connected);

  readonly loading = signal(true);
  readonly actionLoading = signal(false);
  readonly showCancelConfirm = signal(false);
  readonly showDeleteConfirm = signal(false);
  readonly campaignOpen = signal(false);
  readonly campaignSaving = signal(false);
  readonly campaignChannel = signal<OutreachChannelType>('email');
  readonly useAiRecommendedTemplate = signal(true);
  readonly selectedProspectIds = signal<string[]>([]);
  readonly run = signal<ResearchRunDetail | null>(null);
  readonly runId = signal('');
  readonly liveConnected = computed(() => this.researchHub.connected());

  readonly createdLabel = computed(() => formatDate(this.run()?.createdAtUtc ?? null));
  readonly startedLabel = computed(() => formatDate(this.run()?.startedAtUtc ?? null));
  readonly completedLabel = computed(() => formatDate(this.run()?.completedAtUtc ?? null));
  readonly progressPercent = computed(() => this.run()?.progressPercent ?? 0);
  readonly isActive = computed(() => {
    const status = this.run()?.status;
    return status === 'pending' || status === 'running';
  });
  readonly channelsLabel = computed(() => {
    const run = this.run();
    if (!run) {
      return '—';
    }
    const parts: string[] = [];
    if (run.includeEmail) {
      parts.push(this.translate.instant('projects.channelEmail'));
    }
    if (run.includeWhatsApp) {
      parts.push(this.translate.instant('projects.channelWhatsApp'));
    }
    return parts.length > 0 ? parts.join(', ') : this.translate.instant('projects.noChannels');
  });
  readonly duplicatesRemoved = computed(() => {
    const run = this.run();
    if (!run) {
      return 0;
    }
    return Math.max(0, (run.candidatesDiscovered ?? 0) - (run.candidatesAfterDeduplication ?? 0));
  });
  readonly rejectionReasonEntries = computed(() => {
    const reasons = this.run()?.rejectionReasons ?? {};
    return Object.entries(reasons)
      .map(([reason, count]) => ({ reason, count }))
      .sort((a, b) => b.count - a.count);
  });
  readonly providerDiagnostics = computed(() => this.run()?.searchProviderDiagnostics ?? null);
  readonly discoverySummary = computed(
    () => this.providerDiagnostics()?.automaticDiscoverySummary ?? null
  );
  readonly discoveryPlanSummary = computed(
    () => this.providerDiagnostics()?.discoveryPlanSummary ?? null
  );
  readonly discoveryStopReason = computed(
    () => this.providerDiagnostics()?.discoveryStopReason ?? null
  );
  readonly discoveryUniqueDomains = computed(
    () => this.providerDiagnostics()?.uniqueDomainsDiscovered ?? 0
  );
  readonly discoveryRecoveryAttempts = computed(
    () => this.providerDiagnostics()?.recoveryAttempts ?? 0
  );
  readonly discoveryRecoveryStrategies = computed(
    () => (this.providerDiagnostics()?.recoveryStrategies ?? []).join(', ') || '—'
  );
  readonly discoverySourcesReused = computed(() => this.providerDiagnostics()?.sourcesReused ?? 0);
  readonly discoverySourcesAdded = computed(() => this.providerDiagnostics()?.sourcesAdded ?? 0);
  readonly discoverySourcesSkipped = computed(() => this.providerDiagnostics()?.sourcesSkipped ?? 0);
  readonly discoverySourcesUnavailable = computed(
    () => this.providerDiagnostics()?.sourcesUnavailable ?? 0
  );
  readonly discoveryUrlsAccessible = computed(() => this.providerDiagnostics()?.urlsAccessible ?? 0);
  readonly discoveryUrlsParsed = computed(() => this.providerDiagnostics()?.urlsParsed ?? 0);
  readonly discoveryUrlsUnavailable = computed(() => this.providerDiagnostics()?.urlsUnavailable ?? 0);
  readonly discoveryCompaniesIdentified = computed(
    () => this.providerDiagnostics()?.companiesIdentified ?? 0
  );
  readonly discoveryPublicContacts = computed(
    () => this.providerDiagnostics()?.publicContactsExtracted ?? 0
  );
  readonly discoveryLiveVerified = computed(
    () => this.providerDiagnostics()?.liveVerifiedCompanies ?? 0
  );
  readonly discoveryProspectsQualified = computed(
    () => this.providerDiagnostics()?.prospectsQualified ?? 0
  );
  readonly discoveryProviderRows = computed((): DiscoveryProviderRow[] => {
    const providers = this.providerDiagnostics()?.discoveryProviders ?? [];
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
  readonly discoveryStageRows = computed((): DiscoveryStageRow[] => {
    const stages = this.providerDiagnostics()?.discoveryStages ?? [];
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
  readonly discoveryDuplicatesRemoved = computed(
    () => this.providerDiagnostics()?.automaticDiscoveryDuplicatesRemoved ?? 0
  );
  readonly discoveryRejectedUnsafe = computed(
    () => this.providerDiagnostics()?.automaticDiscoveryRejectedUnsafe ?? 0
  );
  readonly continueSearching = computed(() => {
    const run = this.run();
    if (!run || !this.isActive()) {
      return false;
    }
    return (run.qualifiedCount ?? 0) < (run.requestedProspectCount ?? 0);
  });
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
  readonly discoveredUrls = signal<DiscoveredUrlItem[]>([]);
  readonly crawledPages = signal<CrawledPageItem[]>([]);
  readonly runEvents = signal<ResearchRunEventItem[]>([]);
  readonly crawlErrors = computed(() =>
    this.discoveredUrls().filter((item) => !!item.lastError || item.status === 'Failed')
  );
  readonly hasDiagnostics = computed(
    () =>
      this.discoveredUrls().length > 0 ||
      this.crawledPages().length > 0 ||
      this.runEvents().length > 0
  );

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
    this.researchHub.joinResearchRun(id);
    this.hydrateOnce(id);
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
    if (this.connectionSub) {
      this.connectionSub.unsubscribe();
      this.connectionSub = null;
    }
    this.researchHub.leaveResearchRun();
    this.researchHub.clearProgress();
  }

  openProject(): void {
    const projectId = this.run()?.projectId;
    if (!projectId) {
      return;
    }
    this.router.navigate(['/projects', projectId]);
  }

  openProspects(): void {
    const projectId = this.run()?.projectId;
    if (!projectId) {
      return;
    }
    this.router.navigate(['/prospects'], { queryParams: { projectId } });
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
      this.notification.error(this.translate.instant('researchRuns.createCampaignNone'));
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

  openDeleteConfirm(): void {
    this.showDeleteConfirm.set(true);
  }

  closeDeleteConfirm(): void {
    this.showDeleteConfirm.set(false);
  }

  confirmDelete(): void {
    const id = this.runId();
    if (!id) {
      return;
    }
    this.actionLoading.set(true);
    this.researchRunApi.delete(id).subscribe({
      next: () => {
        this.actionLoading.set(false);
        this.showDeleteConfirm.set(false);
        this.notification.success(this.translate.instant('researchRuns.deleteSuccess'));
        this.router.navigate(['/research-runs']);
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notifyError(err);
      }
    });
  }

  private isCampaignReady(prospect: ResearchRunProspectItem): boolean {
    if (prospect.status === 'doNotContact' || prospect.status === 'contacted') {
      return false;
    }
    return !!prospect.email || !!prospect.whatsApp;
  }

  private hydrateOnce(id: string): void {
    this.loading.set(true);
    this.researchRunApi.get(id).subscribe({
      next: (detail) => {
        this.run.set(detail);
        this.loading.set(false);
        this.loadDiagnostics(id);
        if (this.isActiveStatus(detail.status) && !this.researchHub.connected()) {
          this.startPollingFallback(id);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/research-runs']);
      }
    });
  }

  private loadDiagnostics(id: string): void {
    this.researchRunApi.listDiscoveredUrls(id).subscribe({
      next: (items) => this.discoveredUrls.set(items ?? []),
      error: () => this.discoveredUrls.set([])
    });
    this.researchRunApi.listCrawledPages(id).subscribe({
      next: (items) => this.crawledPages.set(items ?? []),
      error: () => this.crawledPages.set([])
    });
    this.researchRunApi.listEvents(id).subscribe({
      next: (items) => this.runEvents.set(items ?? []),
      error: () => this.runEvents.set([])
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
        },
        error: () => {
          this.stopPolling();
        }
      });
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

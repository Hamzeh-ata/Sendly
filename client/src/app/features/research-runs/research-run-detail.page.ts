import { Component, computed, effect, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription, filter, switchMap, takeWhile, timer } from 'rxjs';
import { toObservable } from '@angular/core/rxjs-interop';
import { ResearchRunDetail } from '../../core/models/prospecting.model';
import { ResearchRunApiService } from '../../core/services/research-run-api.service';
import { ResearchHubService } from '../../core/services/research-hub.service';
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
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private pollSub: Subscription | null = null;
  private connectionSub: Subscription | null = null;
  private readonly connected$ = toObservable(this.researchHub.connected);

  readonly loading = signal(true);
  readonly actionLoading = signal(false);
  readonly showCancelConfirm = signal(false);
  readonly showDeleteConfirm = signal(false);
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
  readonly continueSearching = computed(() => {
    const run = this.run();
    if (!run || !this.isActive()) {
      return false;
    }
    return (run.qualifiedCount ?? 0) < (run.requestedProspectCount ?? 0);
  });
  readonly liveProspects = computed(() => this.run()?.prospects ?? []);
  readonly hasLiveProspects = computed(() => this.liveProspects().length > 0);

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

  private hydrateOnce(id: string): void {
    this.loading.set(true);
    this.researchRunApi.get(id).subscribe({
      next: (detail) => {
        this.run.set(detail);
        this.loading.set(false);
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

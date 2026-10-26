import { Component, computed, effect, inject, OnDestroy, OnInit, signal, untracked } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  CampaignDetail,
  CampaignProgress,
  CampaignRecipient,
  RecipientFilter
} from '../../core/models/campaign.model';
import { RecipientRow } from '../../core/models/recipient-row.model';
import { TemplateRecipientCheckItem } from '../../core/models/template-recipient.model';
import { CampaignApiService } from '../../core/services/campaign-api.service';
import { CampaignHubService } from '../../core/services/campaign-hub.service';
import { NotificationService } from '../../core/services/notification.service';
import { downloadBlob, formatDate } from '../../core/utils/format.utils';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-history-detail-page',
  imports: [
    RouterLink,
    TranslatePipe,
    LoadingComponent,
    StatusBadgeComponent,
    ConfirmDialogComponent
  ],
  templateUrl: './history-detail.page.html',
  styleUrl: './history-detail.page.scss'
})
export class HistoryDetailPage implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly campaignApi = inject(CampaignApiService);
  private readonly hub = inject(CampaignHubService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly campaign = signal<CampaignDetail | null>(null);
  readonly recipients = signal<RecipientRow[]>([]);
  readonly loading = signal(true);
  readonly recipientsLoading = signal(false);
  readonly actionLoading = signal(false);
  readonly checkingTemplateRecipients = signal(false);
  readonly filter = signal<RecipientFilter>('all');
  readonly showCancelConfirm = signal(false);
  readonly campaignId = signal('');
  readonly templateContactedCount = signal(0);
  readonly templateNewCount = signal(0);
  readonly showContactedOnly = signal(false);
  readonly allowAlreadyContacted = signal(false);
  private templateCheckByEmail = new Map<string, TemplateRecipientCheckItem>();

  readonly progressPercent = computed(() => this.campaign()?.progressPercent ?? 0);
  readonly templateLabel = computed(
    () => this.campaign()?.templateName || this.translate.instant('history.noTemplate')
  );
  readonly providerLabel = computed(() => {
    const provider = this.campaign()?.provider;
    if (provider === 'microsoftGraph') {
      return this.translate.instant('provider.microsoftGraph');
    }
    if (provider === 'smtp') {
      return this.translate.instant('provider.smtp');
    }
    return this.translate.instant('history.noProvider');
  });
  readonly sendingAccountLabel = computed(() => {
    const campaign = this.campaign();
    if (!campaign) {
      return this.translate.instant('history.noSendingAccount');
    }
    const name = campaign.sendingAccountName?.trim() ?? '';
    const email = campaign.sendingAccountEmail?.trim() ?? '';
    if (name && email) {
      return `${name} (${email})`;
    }
    if (email) {
      return email;
    }
    if (name) {
      return name;
    }
    return this.translate.instant('history.noSendingAccount');
  });
  readonly isActive = computed(() => {
    const status = this.campaign()?.status;
    return status === 'queued' || status === 'running';
  });
  readonly canStart = computed(() => this.campaign()?.status === 'draft');
  readonly canRetry = computed(() => (this.campaign()?.failedCount ?? 0) > 0);
  readonly hasTemplate = computed(() => !!this.campaign()?.templateId);
  readonly showTemplateCheck = computed(() => this.canStart() && this.hasTemplate());
  readonly hasAlreadyContacted = computed(
    () => this.showTemplateCheck() && this.templateContactedCount() > 0
  );
  readonly duplicateWarningParams = computed(() => ({
    count: this.templateContactedCount(),
    total: this.templateContactedCount() + this.templateNewCount()
  }));
  readonly canConfirmStart = computed(() => {
    if (!this.canStart() || this.actionLoading() || this.checkingTemplateRecipients()) {
      return false;
    }
    if (!this.hasAlreadyContacted()) {
      return true;
    }
    return this.allowAlreadyContacted();
  });
  readonly visibleRecipients = computed(() => {
    const rows = this.recipients();
    if (!this.showContactedOnly()) {
      return rows;
    }
    const filtered: RecipientRow[] = [];
    for (const row of rows) {
      if (row.alreadyContacted) {
        filtered.push(row);
      }
    }
    return filtered;
  });
  readonly filterButtons = [
    { value: 'all' as RecipientFilter, labelKey: 'filter.all' },
    { value: 'pending' as RecipientFilter, labelKey: 'filter.pending' },
    { value: 'sent' as RecipientFilter, labelKey: 'filter.sent' },
    { value: 'failed' as RecipientFilter, labelKey: 'filter.failed' },
    { value: 'cancelled' as RecipientFilter, labelKey: 'filter.cancelled' }
  ];

  constructor() {
    effect(() => {
      const progress = this.hub.latestProgress();
      const id = this.campaignId();
      if (!progress || !id || !this.sameCampaign(progress.campaignId, id)) {
        return;
      }
      untracked(() => this.applyProgress(progress));
    });
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    this.campaignId.set(id);
    if (!id) {
      this.loading.set(false);
      return;
    }
    this.loadCampaign(id);
    this.loadRecipients(id);
    this.hub.joinCampaign(id);
  }

  ngOnDestroy(): void {
    this.hub.leaveCampaign();
  }

  setFilter(value: RecipientFilter): void {
    this.filter.set(value);
    this.loadRecipients(this.campaignId());
  }

  openCancelConfirm(): void {
    this.showCancelConfirm.set(true);
  }

  closeCancelConfirm(): void {
    this.showCancelConfirm.set(false);
  }

  cancelCampaign(): void {
    this.actionLoading.set(true);
    this.campaignApi.cancelCampaign(this.campaignId()).subscribe({
      next: () => {
        this.actionLoading.set(false);
        this.showCancelConfirm.set(false);
        this.notification.success(this.translate.instant('detail.cancelSuccess'));
        this.loadCampaign(this.campaignId());
        this.loadRecipients(this.campaignId());
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  startCampaign(): void {
    if (!this.canConfirmStart()) {
      this.notification.error(this.translate.instant('detail.startBlockedAlreadyContacted'));
      return;
    }
    this.actionLoading.set(true);
    this.campaignApi.startCampaign(this.campaignId()).subscribe({
      next: () => {
        this.actionLoading.set(false);
        this.notification.success(this.translate.instant('detail.startSuccess'));
        this.loadCampaign(this.campaignId());
        this.loadRecipients(this.campaignId());
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  excludeAlreadyContacted(): void {
    this.actionLoading.set(true);
    this.campaignApi.excludeAlreadyContacted(this.campaignId()).subscribe({
      next: (detail) => {
        this.actionLoading.set(false);
        this.applyCampaignDetail(detail);
        this.allowAlreadyContacted.set(false);
        this.showContactedOnly.set(false);
        this.notification.success(this.translate.instant('detail.excludeAlreadyContactedSuccess'));
        this.loadRecipients(this.campaignId());
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  keepAlreadyContacted(): void {
    this.actionLoading.set(true);
    this.campaignApi.setAllowAlreadyContacted(this.campaignId(), true).subscribe({
      next: (detail) => {
        this.actionLoading.set(false);
        this.applyCampaignDetail(detail);
        this.allowAlreadyContacted.set(true);
        this.notification.success(this.translate.instant('detail.keepAlreadyContactedSuccess'));
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  useRecommendedDuplicateHandling(): void {
    this.excludeAlreadyContacted();
  }

  toggleContactedOnly(): void {
    this.showContactedOnly.update((value) => !value);
  }

  retryFailed(): void {
    this.actionLoading.set(true);
    this.campaignApi.retryFailed(this.campaignId()).subscribe({
      next: () => {
        this.actionLoading.set(false);
        this.notification.success(this.translate.instant('detail.retrySuccess'));
        this.loadCampaign(this.campaignId());
        this.loadRecipients(this.campaignId());
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  exportCsv(): void {
    this.campaignApi.exportCsv(this.campaignId()).subscribe({
      next: (blob) => {
        downloadBlob(blob, `campaign-${this.campaignId()}.csv`);
      },
      error: (err: HttpErrorResponse) => {
        this.notification.error(this.extractError(err));
      }
    });
  }

  private loadCampaign(id: string): void {
    this.loading.set(true);
    this.campaignApi.getCampaign(id).subscribe({
      next: (detail) => {
        this.applyCampaignDetail(detail);
        this.allowAlreadyContacted.set(!!detail.allowAlreadyContacted);
        this.loading.set(false);
        if (detail.status === 'draft' && detail.templateId) {
          this.checkTemplateRecipients();
        } else {
          this.clearTemplateCheck();
          this.recipients.set(this.mapRecipientRows(this.recipients()));
        }
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  private applyCampaignDetail(detail: CampaignDetail): void {
    this.campaign.set({
      ...detail,
      id: String(detail.id),
      allowAlreadyContacted: !!detail.allowAlreadyContacted
    });
  }

  private loadRecipients(id: string): void {
    this.recipientsLoading.set(true);
    const status = this.filter();
    this.campaignApi.getRecipients(id, status).subscribe({
      next: (items) => {
        this.recipients.set(this.mapRecipientRows(items ?? []));
        this.recipientsLoading.set(false);
        this.checkTemplateRecipients();
      },
      error: () => {
        this.recipients.set([]);
        this.recipientsLoading.set(false);
        this.clearTemplateCheck();
      }
    });
  }

  private checkTemplateRecipients(): void {
    const campaign = this.campaign();
    if (!campaign || campaign.status !== 'draft' || !campaign.templateId) {
      this.clearTemplateCheck();
      this.recipients.set(this.mapRecipientRows(this.recipients()));
      return;
    }

    this.checkingTemplateRecipients.set(true);
    this.campaignApi.checkTemplateRecipients(this.campaignId()).subscribe({
      next: (response) => {
        const byEmail = new Map<string, TemplateRecipientCheckItem>();
        for (const item of response.recipients ?? []) {
          byEmail.set(item.email.trim().toLowerCase(), item);
        }
        this.templateCheckByEmail = byEmail;
        this.templateContactedCount.set(response.alreadyContactedCount ?? 0);
        this.templateNewCount.set(response.newCount ?? 0);
        this.checkingTemplateRecipients.set(false);
        this.recipients.set(this.mapRecipientRows(this.recipients()));
      },
      error: () => {
        this.checkingTemplateRecipients.set(false);
        this.clearTemplateCheck();
      }
    });
  }

  private clearTemplateCheck(): void {
    this.templateCheckByEmail = new Map();
    this.templateContactedCount.set(0);
    this.templateNewCount.set(0);
    this.checkingTemplateRecipients.set(false);
  }

  private mapRecipientRows(items: CampaignRecipient[]): RecipientRow[] {
    const rows: RecipientRow[] = [];
    for (const item of items) {
      const check = this.templateCheckByEmail.get(item.email.trim().toLowerCase());
      const alreadyContacted = !!check?.alreadyContacted;
      rows.push({
        ...item,
        id: String(item.id),
        lastAttemptLabel: formatDate(item.lastAttemptAtUtc ?? null),
        alreadyContacted,
        templateStatusLabelKey: alreadyContacted
          ? 'compose.recipientAlreadyContacted'
          : 'compose.recipientNew',
        previousSendLabel: alreadyContacted
          ? formatDate(check?.previousSentAtUtc ?? null)
          : this.translate.instant('compose.noPreviousSend')
      });
    }
    return rows;
  }

  private applyProgress(progress: CampaignProgress): void {
    const current = this.campaign();
    if (!current) {
      return;
    }
    this.campaign.set({
      ...current,
      status: progress.status,
      successCount: progress.successCount,
      failedCount: progress.failedCount,
      cancelledCount: progress.cancelledCount,
      pendingCount: progress.pendingCount,
      progressPercent: progress.progressPercent,
      currentRecipientEmail: progress.currentRecipientEmail,
      startedAtUtc: progress.startedAtUtc,
      completedAtUtc: progress.completedAtUtc
    });
    if (progress.updatedRecipient) {
      this.updateRecipientInList(progress.updatedRecipient);
    }
  }

  private updateRecipientInList(updated: CampaignRecipient): void {
    const filter = this.filter();
    if (filter !== 'all' && updated.status !== filter) {
      this.recipients.update((items) => items.filter((item) => item.id !== updated.id));
      return;
    }
    this.recipients.update((items) => {
      const index = items.findIndex((item) => item.id === updated.id);
      const mapped = this.mapRecipientRows([updated])[0];
      if (index >= 0) {
        const copy = [...items];
        copy[index] = mapped;
        return copy;
      }
      if (filter === 'all' || updated.status === filter) {
        return [...items, mapped];
      }
      return items;
    });
  }

  private sameCampaign(left: string, right: string): boolean {
    return left.trim().toLowerCase() === right.trim().toLowerCase();
  }

  private extractError(err: HttpErrorResponse): string {
    if (err.error?.message) {
      return err.error.message;
    }
    return this.translate.instant('common.errorGeneric');
  }
}

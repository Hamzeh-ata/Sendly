import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ProspectSummary } from '../../core/models/prospecting.model';
import {
  OutreachSequenceDetail,
  OutreachSequenceStep,
  SequenceAnalytics,
  SequenceAnalyticsView,
  SequenceEnrollment,
  SequenceEnrollmentRow,
  SequenceProspectOption,
  SequenceStepAnalyticsRow,
  SequenceStepViewRow,
  SequenceVariantAnalyticsRow
} from '../../core/models/sequence.model';
import { ProspectApiService } from '../../core/services/prospect-api.service';
import { SequenceApiService } from '../../core/services/sequence-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatDate } from '../../core/utils/format.utils';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-sequence-detail-page',
  imports: [
    RouterLink,
    TranslatePipe,
    LoadingComponent,
    StatusBadgeComponent,
    ConfirmDialogComponent
  ],
  templateUrl: './sequence-detail.page.html',
  styleUrl: './sequence-detail.page.scss'
})
export class SequenceDetailPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly sequenceApi = inject(SequenceApiService);
  private readonly prospectApi = inject(ProspectApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly sequence = signal<OutreachSequenceDetail | null>(null);
  readonly steps = signal<SequenceStepViewRow[]>([]);
  readonly enrollments = signal<SequenceEnrollmentRow[]>([]);
  readonly analytics = signal<SequenceAnalyticsView | null>(null);
  readonly enrollOpen = signal(false);
  readonly prospectOptions = signal<SequenceProspectOption[]>([]);
  readonly selectedProspectIds = signal<string[]>([]);
  readonly cancelEnrollmentId = signal<string | null>(null);

  readonly statusLabelKey = computed(() =>
    this.sequence()?.isActive ? 'sequences.statusActive' : 'sequences.statusInactive'
  );
  readonly descriptionLabel = computed(() => {
    const value = this.sequence()?.description?.trim();
    return value || this.translate.instant('sequences.noDescription');
  });
  readonly projectLabel = computed(() => this.sequence()?.projectName || '—');
  readonly hasProspectSelection = computed(() => this.selectedProspectIds().length > 0);
  readonly selectedProspectCount = computed(() => this.selectedProspectIds().length);
  readonly displayProspects = computed(() => {
    const selected = this.selectedProspectIds();
    const rows: SequenceProspectOption[] = [];
    for (const item of this.prospectOptions()) {
      rows.push({ ...item, selected: selected.includes(item.id) });
    }
    return rows;
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    if (!id) {
      this.loading.set(false);
      return;
    }
    this.loadSequence(id);
  }

  openEdit(): void {
    const id = this.sequence()?.id;
    if (!id) {
      return;
    }
    this.router.navigate(['/sequences', id, 'edit']);
  }

  openEnroll(): void {
    const sequence = this.sequence();
    if (!sequence) {
      return;
    }
    this.selectedProspectIds.set([]);
    this.enrollOpen.set(true);
    this.loadProspects(sequence.projectId);
  }

  closeEnroll(): void {
    this.enrollOpen.set(false);
  }

  toggleProspect(id: string): void {
    this.selectedProspectIds.update((ids) => {
      if (ids.includes(id)) {
        return ids.filter((item) => item !== id);
      }
      return [...ids, id];
    });
  }

  confirmEnroll(): void {
    const sequence = this.sequence();
    const prospectIds = this.selectedProspectIds();
    if (!sequence || prospectIds.length === 0) {
      this.notification.error(this.translate.instant('sequences.selectProspectsRequired'));
      return;
    }
    this.saving.set(true);
    this.sequenceApi.enroll(sequence.id, prospectIds).subscribe({
      next: () => {
        this.saving.set(false);
        this.enrollOpen.set(false);
        this.notification.success(this.translate.instant('sequences.enrollSuccess'));
        this.reloadEnrollments(sequence.id);
        this.reloadAnalytics(sequence.id);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  openCancelEnrollment(id: string): void {
    this.cancelEnrollmentId.set(id);
  }

  closeCancelEnrollment(): void {
    this.cancelEnrollmentId.set(null);
  }

  confirmCancelEnrollment(): void {
    const enrollmentId = this.cancelEnrollmentId();
    const sequenceId = this.sequence()?.id;
    if (!enrollmentId || !sequenceId) {
      return;
    }
    this.saving.set(true);
    this.sequenceApi.cancelEnrollment(enrollmentId).subscribe({
      next: () => {
        this.saving.set(false);
        this.cancelEnrollmentId.set(null);
        this.notification.success(this.translate.instant('sequences.cancelEnrollmentSuccess'));
        this.reloadEnrollments(sequenceId);
        this.reloadAnalytics(sequenceId);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadSequence(id: string): void {
    this.loading.set(true);
    this.sequenceApi.get(id).subscribe({
      next: (detail) => {
        this.sequence.set(detail);
        this.steps.set(this.mapSteps(detail.steps ?? []));
        this.loading.set(false);
        this.reloadEnrollments(id);
        this.reloadAnalytics(id);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/sequences']);
      }
    });
  }

  private reloadEnrollments(id: string): void {
    this.sequenceApi.listEnrollments(id).subscribe({
      next: (items) => this.enrollments.set(this.mapEnrollments(items ?? [])),
      error: () => this.enrollments.set([])
    });
  }

  private reloadAnalytics(id: string): void {
    this.sequenceApi.getAnalytics(id).subscribe({
      next: (items) => this.analytics.set(this.mapAnalytics(items)),
      error: () => this.analytics.set(null)
    });
  }

  private loadProspects(projectId: string): void {
    this.prospectApi.list({ projectId }).subscribe({
      next: (items) => this.prospectOptions.set(this.mapProspectOptions(items ?? [])),
      error: () => this.prospectOptions.set([])
    });
  }

  private mapSteps(items: OutreachSequenceStep[]): SequenceStepViewRow[] {
    const rows: SequenceStepViewRow[] = [];
    for (const item of items) {
      rows.push({
        id: item.id ?? null,
        stepOrder: item.stepOrder,
        channelLabelKey:
          item.channel === 'whatsApp' ? 'projects.channelWhatsApp' : 'projects.channelEmail',
        templateLabel: item.templateName || this.translate.instant('sequences.noTemplate'),
        delayHoursAfterPrevious: item.delayHoursAfterPrevious,
        whatsAppTemplateName: item.whatsAppTemplateName || '—',
        whatsAppTemplateLanguage: item.whatsAppTemplateLanguage || '—',
        variantLabel: item.variantKey?.trim() || this.translate.instant('sequences.variantDefault'),
        trafficPercent: item.trafficPercent ?? 100,
        showWhatsAppFields: item.channel === 'whatsApp'
      });
    }
    return rows;
  }

  private mapEnrollments(items: SequenceEnrollment[]): SequenceEnrollmentRow[] {
    const rows: SequenceEnrollmentRow[] = [];
    for (const item of items) {
      rows.push({
        ...item,
        enrolledLabel: formatDate(item.enrolledAtUtc ?? null),
        nextRunLabel: item.nextRunAtUtc ? formatDate(item.nextRunAtUtc) : '—',
        completedLabel: item.completedAtUtc ? formatDate(item.completedAtUtc) : '—',
        canCancel: item.status !== 'completed' && item.status !== 'cancelled',
        companyLabel: item.prospectCompanyName || '—',
        errorLabel: item.lastError || '—',
        variantLabel: item.assignedVariantKey?.trim() || this.translate.instant('sequences.variantDefault')
      });
    }
    return rows;
  }

  private mapAnalytics(data: SequenceAnalytics): SequenceAnalyticsView {
    const variants: SequenceVariantAnalyticsRow[] = [];
    for (const item of data.variants ?? []) {
      variants.push({
        variantKey: item.variantKey,
        enrollmentCount: item.enrollmentCount,
        completedCount: item.completedCount,
        failedCount: item.failedCount,
        activeCount: item.activeCount,
        completionRateLabel: `${item.completionRate}%`
      });
    }
    const steps: SequenceStepAnalyticsRow[] = [];
    for (const item of data.steps ?? []) {
      steps.push({
        stepOrder: item.stepOrder,
        variantLabel: item.variantKey?.trim() || this.translate.instant('sequences.variantDefault'),
        channelLabelKey:
          item.channel === 'whatsApp' ? 'projects.channelWhatsApp' : 'projects.channelEmail',
        templateLabel: item.templateName || this.translate.instant('sequences.noTemplate'),
        trafficPercent: item.trafficPercent,
        reachedCount: item.reachedCount
      });
    }
    return {
      totalEnrollments: data.totalEnrollments,
      activeCount: data.activeCount,
      completedCount: data.completedCount,
      failedCount: data.failedCount,
      cancelledCount: data.cancelledCount,
      completionRateLabel: `${data.completionRate}%`,
      variants,
      steps
    };
  }

  private mapProspectOptions(items: ProspectSummary[]): SequenceProspectOption[] {
    const rows: SequenceProspectOption[] = [];
    for (const item of items) {
      const contact = item.contactName ? ` — ${item.contactName}` : '';
      rows.push({
        id: item.id,
        label: `${item.companyName}${contact}`,
        selected: false
      });
    }
    return rows;
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

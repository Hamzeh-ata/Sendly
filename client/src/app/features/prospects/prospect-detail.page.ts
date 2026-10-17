import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  OutreachChannelType,
  PrepareOutreachRequest,
  ProspectDetail
} from '../../core/models/prospecting.model';
import { ContactHistoryRow } from '../../core/models/prospect-row.model';
import { ProjectApiService } from '../../core/services/project-api.service';
import { ProspectApiService } from '../../core/services/prospect-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatDate } from '../../core/utils/format.utils';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-prospect-detail-page',
  imports: [RouterLink, TranslatePipe, LoadingComponent, StatusBadgeComponent],
  templateUrl: './prospect-detail.page.html',
  styleUrl: './prospect-detail.page.scss'
})
export class ProspectDetailPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly prospectApi = inject(ProspectApiService);
  private readonly projectApi = inject(ProjectApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly prospect = signal<ProspectDetail | null>(null);
  readonly contactHistory = signal<ContactHistoryRow[]>([]);
  readonly campaignOpen = signal(false);
  readonly campaignChannel = signal<OutreachChannelType>('email');
  readonly useAiRecommendedTemplate = signal(true);

  readonly createdLabel = computed(() => formatDate(this.prospect()?.createdAtUtc ?? null));
  readonly updatedLabel = computed(() => formatDate(this.prospect()?.updatedAtUtc ?? null));
  readonly duplicateLabelKey = computed(
    () => `duplicateKind.${this.prospect()?.duplicateKind ?? 'none'}`
  );
  readonly qualificationLabel = computed(() => {
    const score = this.prospect()?.qualificationScore;
    return score != null ? String(score) : this.translate.instant('prospects.noScore');
  });
  readonly templateLabel = computed(
    () =>
      this.prospect()?.recommendedTemplateName ||
      this.translate.instant('prospects.noTemplate')
  );
  readonly templateReason = computed(
    () => this.prospect()?.recommendedTemplateReason || '—'
  );
  readonly templateConfidence = computed(() => {
    const value = this.prospect()?.recommendedTemplateConfidence;
    return value != null ? String(value) : '—';
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    if (!id) {
      this.loading.set(false);
      return;
    }
    this.loadProspect(id);
  }

  approve(): void {
    const id = this.prospect()?.id;
    if (!id) {
      return;
    }
    this.saving.set(true);
    this.prospectApi.approve(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('prospects.approveSuccess'));
        this.loadProspect(id);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  reject(): void {
    const id = this.prospect()?.id;
    if (!id) {
      return;
    }
    this.saving.set(true);
    this.prospectApi.reject(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('prospects.rejectSuccess'));
        this.loadProspect(id);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  doNotContact(): void {
    const id = this.prospect()?.id;
    if (!id) {
      return;
    }
    this.saving.set(true);
    this.prospectApi.doNotContact(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('prospects.doNotContactSuccess'));
        this.loadProspect(id);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  recommendTemplate(): void {
    const id = this.prospect()?.id;
    if (!id) {
      return;
    }
    this.saving.set(true);
    this.prospectApi.recommendTemplate(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('prospects.recommendSuccess'));
        this.loadProspect(id);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  openCampaign(): void {
    this.campaignChannel.set('email');
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
    const prospect = this.prospect();
    if (!prospect) {
      return;
    }
    const request: PrepareOutreachRequest = {
      prospectIds: [prospect.id],
      channel: this.campaignChannel(),
      templateId: null,
      useAiRecommendedTemplate: this.useAiRecommendedTemplate(),
      emailAccountId: null,
      signatureId: null,
      allowAlreadyContacted: false
    };
    this.saving.set(true);
    this.projectApi.prepareOutreach(prospect.projectId, request).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.campaignOpen.set(false);
        this.notification.success(this.translate.instant('prospects.campaignSuccess'));
        if (result.campaignId) {
          this.router.navigate(['/history', result.campaignId]);
          return;
        }
        this.loadProspect(prospect.id);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadProspect(id: string): void {
    this.loading.set(true);
    this.prospectApi.get(id).subscribe({
      next: (detail) => {
        this.prospect.set(detail);
        this.contactHistory.set(this.mapHistory(detail));
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/prospects']);
      }
    });
  }

  private mapHistory(detail: ProspectDetail): ContactHistoryRow[] {
    const rows: ContactHistoryRow[] = [];
    for (const item of detail.contactHistory ?? []) {
      rows.push({
        id: item.id,
        channelLabel:
          item.channel === 'whatsApp'
            ? this.translate.instant('projects.channelWhatsApp')
            : this.translate.instant('projects.channelEmail'),
        contactValue: item.contactValue,
        contactedLabel: formatDate(item.contactedAtUtc ?? null)
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

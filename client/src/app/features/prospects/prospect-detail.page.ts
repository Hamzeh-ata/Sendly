import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  OutreachChannelType,
  PrepareOutreachRequest,
  ProspectDetail
} from '../../core/models/prospecting.model';
import {
  QualificationCriterionItem,
  ResearchEvidenceItem
} from '../../core/models/research-query.model';
import { ContactHistoryRow, FetchedContactRow } from '../../core/models/prospect-row.model';
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
  readonly fetchedEmails = signal<FetchedContactRow[]>([]);
  readonly fetchedPhones = signal<FetchedContactRow[]>([]);
  readonly evidenceItems = signal<ResearchEvidenceItem[]>([]);
  readonly qualificationCriteria = signal<QualificationCriterionItem[]>([]);
  readonly campaignOpen = signal(false);
  readonly campaignChannel = signal<OutreachChannelType>('email');
  readonly useAiRecommendedTemplate = signal(true);
  readonly hasEvidence = computed(() => this.evidenceItems().length > 0);
  readonly hasCriteria = computed(() => this.qualificationCriteria().length > 0);
  readonly hasFetchedEmails = computed(() => this.fetchedEmails().length > 0);
  readonly hasFetchedPhones = computed(() => this.fetchedPhones().length > 0);
  readonly hasFetchedContacts = computed(
    () => this.hasFetchedEmails() || this.hasFetchedPhones()
  );

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
        this.fetchedEmails.set(this.mapFetchedEmails(detail));
        this.fetchedPhones.set(this.mapFetchedPhones(detail));
        this.loading.set(false);
        this.loadEvidence(id);
        this.loadCriteria(id);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/prospects']);
      }
    });
  }

  private loadEvidence(id: string): void {
    this.prospectApi.listEvidence(id).subscribe({
      next: (items) => this.evidenceItems.set(items ?? []),
      error: () => this.evidenceItems.set([])
    });
  }

  private loadCriteria(id: string): void {
    this.prospectApi.listQualificationCriteria(id).subscribe({
      next: (items) => this.qualificationCriteria.set(items ?? []),
      error: () => this.qualificationCriteria.set([])
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

  private mapFetchedEmails(detail: ProspectDetail): FetchedContactRow[] {
    return this.buildFetchedRows(
      detail.email,
      detail.additionalEmails ?? [],
      'email'
    );
  }

  private mapFetchedPhones(detail: ProspectDetail): FetchedContactRow[] {
    return this.buildFetchedRows(
      detail.phone,
      detail.additionalPhones ?? [],
      'phone'
    );
  }

  private buildFetchedRows(
    primary: string | null | undefined,
    additional: string[],
    channel: 'email' | 'phone'
  ): FetchedContactRow[] {
    const rows: FetchedContactRow[] = [];
    const seen = new Set<string>();
    const primaryValue = (primary ?? '').trim();
    if (primaryValue) {
      const key = primaryValue.toLowerCase();
      seen.add(key);
      rows.push({
        id: `${channel}-primary`,
        value: primaryValue,
        kindLabel: this.translate.instant('prospects.fetchedPrimary')
      });
    }

    let index = 0;
    for (const raw of additional) {
      const value = (raw ?? '').trim();
      if (!value) {
        continue;
      }
      const key = value.toLowerCase();
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      index += 1;
      rows.push({
        id: `${channel}-extra-${index}`,
        value,
        kindLabel: this.translate.instant('prospects.fetchedAdditional')
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

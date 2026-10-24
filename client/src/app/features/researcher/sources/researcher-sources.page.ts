import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DiscoverySourceRow, DiscoverySourceSummary } from '../../../core/models/researcher.model';
import { DiscoverySourceApiService } from '../../../core/services/discovery-source-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { formatDate } from '../../../core/utils/format.utils';
import { mapSourceToRow } from '../researcher.mappers';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { MetricTileComponent } from '../../../shared/components/researcher/metric-tile.component';

@Component({
  selector: 'app-researcher-sources-page',
  imports: [TranslatePipe, EmptyStateComponent, MetricTileComponent],
  templateUrl: './researcher-sources.page.html',
  styleUrl: './researcher-sources.page.scss'
})
export class ResearcherSourcesPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly discoverySourceApi = inject(DiscoverySourceApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly rows = signal<DiscoverySourceRow[]>([]);
  readonly selectedId = signal('');
  readonly selectedDetail = signal<DiscoverySourceSummary | null>(null);

  readonly hasRows = computed(() => this.rows().length > 0);
  readonly detailTiles = computed(() => {
    const detail = this.selectedDetail();
    if (!detail) {
      return [];
    }
    return [
      {
        labelKey: 'researcher.sources.candidates',
        value: String(detail.candidatesDiscovered),
        tone: 'default' as const
      },
      {
        labelKey: 'researcher.sources.companies',
        value: String(detail.uniqueCompaniesDiscovered),
        tone: 'default' as const
      },
      {
        labelKey: 'researcher.sources.verified',
        value: String(detail.liveVerifiedCompanies),
        tone: 'success' as const
      },
      {
        labelKey: 'researcher.sources.qualified',
        value: String(detail.qualifiedProspectsYielded),
        tone: 'success' as const
      },
      {
        labelKey: 'researcher.sources.failureRate',
        value: detail.failureRate == null ? '—' : `${(detail.failureRate * 100).toFixed(1)}%`,
        tone: 'warning' as const
      },
      {
        labelKey: 'researcher.sources.avgFetch',
        value: detail.averageFetchMs == null ? '—' : `${Math.round(detail.averageFetchMs)} ms`,
        tone: 'active' as const
      }
    ];
  });

  readonly detailMeta = computed(() => {
    const detail = this.selectedDetail();
    if (!detail) {
      return [];
    }
    return [
      { labelKey: 'researcher.sources.baseUrl', value: detail.baseUrl || '—' },
      { labelKey: 'researcher.sources.type', value: detail.sourceType || '—' },
      { labelKey: 'researcher.sources.status', value: detail.status || '—' },
      {
        labelKey: 'researcher.sources.lastAttempted',
        value: formatDate(detail.lastAttemptedAtUtc ?? null)
      },
      {
        labelKey: 'researcher.sources.lastSucceeded',
        value: formatDate(detail.lastSucceededAtUtc ?? null)
      },
      { labelKey: 'researcher.sources.lastError', value: detail.lastError || '—' }
    ];
  });

  ngOnInit(): void {
    const selectedId = this.route.snapshot.queryParamMap.get('id') ?? '';
    this.selectedId.set(selectedId);
    this.loadSources(selectedId);
  }

  selectSource(id: string): void {
    this.selectedId.set(id);
    this.loadDetail(id);
  }

  private loadSources(preferredId: string): void {
    this.loading.set(true);
    this.discoverySourceApi.list().subscribe({
      next: (items) => {
        const mapped = (items ?? []).map((item) => mapSourceToRow(item));
        this.rows.set(mapped);
        this.loading.set(false);
        const id = preferredId || mapped[0]?.id || '';
        if (id) {
          this.selectedId.set(id);
          this.loadDetail(id);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.rows.set([]);
        this.notifyError(err);
      }
    });
  }

  private loadDetail(id: string): void {
    this.discoverySourceApi.get(id).subscribe({
      next: (detail) => this.selectedDetail.set(detail),
      error: () => this.selectedDetail.set(null)
    });
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

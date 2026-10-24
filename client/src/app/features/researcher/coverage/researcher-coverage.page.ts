import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { DiscoverySourceSummary, ResearchOverview } from '../../../core/models/researcher.model';
import { DiscoverySourceApiService } from '../../../core/services/discovery-source-api.service';
import { ResearchRunApiService } from '../../../core/services/research-run-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { mapSourceToRow } from '../researcher.mappers';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { MetricTileComponent } from '../../../shared/components/researcher/metric-tile.component';

@Component({
  selector: 'app-researcher-coverage-page',
  imports: [TranslatePipe, EmptyStateComponent, MetricTileComponent],
  templateUrl: './researcher-coverage.page.html',
  styleUrl: './researcher-coverage.page.scss'
})
export class ResearcherCoveragePage implements OnInit {
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly discoverySourceApi = inject(DiscoverySourceApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly overview = signal<ResearchOverview | null>(null);
  readonly sources = signal<DiscoverySourceSummary[]>([]);

  readonly hasData = computed(() => !!this.overview() || this.sources().length > 0);

  readonly coverageTiles = computed(() => {
    const data = this.overview();
    if (!data) {
      return [];
    }
    return [
      {
        labelKey: 'researcher.overview.urlsDiscovered',
        value: String(data.urlsDiscovered),
        tone: 'default' as const
      },
      {
        labelKey: 'researcher.overview.pagesFetched',
        value: String(data.pagesFetched),
        tone: 'active' as const
      },
      {
        labelKey: 'researcher.overview.domainsDiscovered',
        value: String(data.domainsDiscovered),
        tone: 'default' as const
      },
      {
        labelKey: 'researcher.overview.companiesFound',
        value: String(data.companiesFound),
        tone: 'default' as const
      },
      {
        labelKey: 'researcher.overview.contactsFound',
        value: String(data.contactsFound),
        tone: 'default' as const
      },
      {
        labelKey: 'researcher.overview.qualifiedProspects',
        value: String(data.qualifiedProspects),
        tone: 'success' as const
      }
    ];
  });

  readonly unavailableTiles = computed(() => [
    {
      labelKey: 'researcher.coverage.avgPagesPerDomain',
      value: this.translate.instant('researcher.coverage.unavailable')
    },
    {
      labelKey: 'researcher.coverage.contactCoverage',
      value: this.translate.instant('researcher.coverage.unavailable')
    },
    {
      labelKey: 'researcher.coverage.qualificationRate',
      value: this.translate.instant('researcher.coverage.unavailable')
    }
  ]);

  readonly sourceRows = computed(() => this.sources().map((item) => mapSourceToRow(item)));

  ngOnInit(): void {
    this.loading.set(true);
    forkJoin({
      overview: this.researchRunApi.getOverview(),
      sources: this.discoverySourceApi.list()
    }).subscribe({
      next: ({ overview, sources }) => {
        this.overview.set(overview);
        this.sources.set(sources ?? []);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
      }
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

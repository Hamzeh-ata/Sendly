import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ResearchOverview } from '../../../core/models/researcher.model';
import { ResearchRunApiService } from '../../../core/services/research-run-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { formatDate } from '../../../core/utils/format.utils';
import { mapErrorGroupToRow, mapSourceToRow } from '../researcher.mappers';
import { MetricTileComponent } from '../../../shared/components/researcher/metric-tile.component';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-researcher-overview-page',
  imports: [
    RouterLink,
    TranslatePipe,
    MetricTileComponent,
    EmptyStateComponent,
    StatusBadgeComponent
  ],
  templateUrl: './researcher-overview.page.html',
  styleUrl: './researcher-overview.page.scss'
})
export class ResearcherOverviewPage implements OnInit {
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly overview = signal<ResearchOverview | null>(null);

  readonly hasData = computed(() => {
    const data = this.overview();
    if (!data) {
      return false;
    }
    return (
      data.activeRuns > 0 ||
      data.queuedRuns > 0 ||
      data.completedRuns > 0 ||
      data.failedRuns > 0 ||
      data.urlsDiscovered > 0 ||
      data.pagesFetched > 0 ||
      data.recentRuns.length > 0
    );
  });

  readonly statusTiles = computed(() => {
    const data = this.overview();
    if (!data) {
      return [];
    }
    return [
      { labelKey: 'researcher.overview.activeRuns', value: String(data.activeRuns), tone: 'active' as const },
      { labelKey: 'researcher.overview.queuedRuns', value: String(data.queuedRuns), tone: 'default' as const },
      { labelKey: 'researcher.overview.completedRuns', value: String(data.completedRuns), tone: 'success' as const },
      { labelKey: 'researcher.overview.failedRuns', value: String(data.failedRuns), tone: 'danger' as const },
      { labelKey: 'researcher.overview.cancelledRuns', value: String(data.cancelledRuns), tone: 'default' as const }
    ];
  });

  readonly funnelTiles = computed(() => {
    const data = this.overview();
    if (!data) {
      return [];
    }
    return [
      { labelKey: 'researcher.overview.urlsDiscovered', value: String(data.urlsDiscovered), tone: 'default' as const },
      { labelKey: 'researcher.overview.pagesFetched', value: String(data.pagesFetched), tone: 'active' as const },
      { labelKey: 'researcher.overview.domainsDiscovered', value: String(data.domainsDiscovered), tone: 'default' as const },
      { labelKey: 'researcher.overview.companiesFound', value: String(data.companiesFound), tone: 'default' as const },
      { labelKey: 'researcher.overview.contactsFound', value: String(data.contactsFound), tone: 'default' as const },
      { labelKey: 'researcher.overview.qualifiedProspects', value: String(data.qualifiedProspects), tone: 'success' as const },
      { labelKey: 'researcher.overview.crawlErrors', value: String(data.crawlErrors), tone: 'danger' as const },
      { labelKey: 'researcher.overview.eventErrors', value: String(data.eventErrors), tone: 'danger' as const },
      { labelKey: 'researcher.overview.eventWarnings', value: String(data.eventWarnings), tone: 'warning' as const }
    ];
  });

  readonly activeRuns = computed(() => {
    const data = this.overview();
    if (!data) {
      return [];
    }
    return data.activeRunDetails.map((run) => ({
      id: run.id,
      title: run.researchQueryName || run.projectName || run.id,
      projectName: run.projectName || '—',
      status: run.status,
      urlsDiscovered: run.urlsDiscovered,
      pagesCrawled: run.pagesCrawled,
      qualifiedCount: run.qualifiedCount,
      startedLabel: formatDate(run.startedAtUtc ?? run.createdAtUtc)
    }));
  });

  readonly topSources = computed(() =>
    (this.overview()?.topSources ?? []).map((item) => mapSourceToRow(item))
  );

  readonly unhealthySources = computed(() =>
    (this.overview()?.unhealthySources ?? []).map((item) => mapSourceToRow(item))
  );

  readonly recentErrors = computed(() =>
    (this.overview()?.recentCriticalErrors ?? []).map((item) => mapErrorGroupToRow(item))
  );

  ngOnInit(): void {
    this.loadOverview();
  }

  private loadOverview(): void {
    this.loading.set(true);
    this.researchRunApi.getOverview().subscribe({
      next: (data) => {
        this.overview.set(data);
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

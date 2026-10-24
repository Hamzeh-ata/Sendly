import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ResearchRunSummary } from '../../../core/models/prospecting.model';
import { CrawledPageItem } from '../../../core/models/research-query.model';
import { ResearchRunApiService } from '../../../core/services/research-run-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { formatDate } from '../../../core/utils/format.utils';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-researcher-fetch-page',
  imports: [RouterLink, TranslatePipe, EmptyStateComponent],
  templateUrl: './researcher-fetch.page.html',
  styleUrl: './researcher-fetch.page.scss'
})
export class ResearcherFetchPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly runsLoading = signal(true);
  readonly runs = signal<ResearchRunSummary[]>([]);
  readonly selectedRunId = signal('');
  readonly pages = signal<CrawledPageItem[]>([]);

  readonly pageRows = computed(() =>
    this.pages().map((page) => ({
      id: page.id,
      title: page.title || page.url,
      url: page.url,
      statusLabel: page.httpStatusCode == null ? '—' : String(page.httpStatusCode),
      durationLabel: page.durationMs == null ? '—' : `${page.durationMs} ms`,
      crawledLabel: formatDate(page.crawledAtUtc),
      hasError: !!page.error
    }))
  );

  ngOnInit(): void {
    const runId = this.route.snapshot.queryParamMap.get('runId') ?? '';
    this.selectedRunId.set(runId);
    this.loadRuns(runId);
  }

  onRunChange(runId: string): void {
    this.selectedRunId.set(runId);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { runId: runId || null },
      queryParamsHandling: 'merge'
    });
    if (runId) {
      this.loadPages(runId);
      return;
    }
    this.pages.set([]);
    this.loading.set(false);
  }

  private loadRuns(preferredRunId: string): void {
    this.runsLoading.set(true);
    this.researchRunApi.list().subscribe({
      next: (items) => {
        this.runs.set(items ?? []);
        this.runsLoading.set(false);
        const runId = preferredRunId || items?.[0]?.id || '';
        if (runId) {
          this.selectedRunId.set(runId);
          this.loadPages(runId);
          return;
        }
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.runsLoading.set(false);
        this.loading.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadPages(runId: string): void {
    this.loading.set(true);
    this.researchRunApi.listCrawledPages(runId).subscribe({
      next: (items) => {
        this.pages.set(items ?? []);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.pages.set([]);
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

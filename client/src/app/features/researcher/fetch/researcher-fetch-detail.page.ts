import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { CrawledPageItem } from '../../../core/models/research-query.model';
import { ResearchRunApiService } from '../../../core/services/research-run-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { formatBytes, formatDate } from '../../../core/utils/format.utils';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-researcher-fetch-detail-page',
  imports: [RouterLink, TranslatePipe, EmptyStateComponent],
  templateUrl: './researcher-fetch-detail.page.html',
  styleUrl: './researcher-fetch-detail.page.scss'
})
export class ResearcherFetchDetailPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly page = signal<CrawledPageItem | null>(null);
  readonly runId = signal('');
  readonly pageId = signal('');

  readonly fields = computed(() => {
    const item = this.page();
    if (!item) {
      return [];
    }
    return [
      { labelKey: 'researcher.fetch.url', value: item.url || '—' },
      { labelKey: 'researcher.fetch.normalizedUrl', value: item.normalizedUrl || '—' },
      { labelKey: 'researcher.fetch.finalUrl', value: item.finalUrl || '—' },
      { labelKey: 'researcher.fetch.canonicalUrl', value: item.canonicalUrl || '—' },
      { labelKey: 'researcher.fetch.domain', value: item.normalizedDomain || '—' },
      { labelKey: 'researcher.fetch.titleField', value: item.title || '—' },
      {
        labelKey: 'researcher.fetch.status',
        value: item.httpStatusCode == null ? '—' : String(item.httpStatusCode)
      },
      { labelKey: 'researcher.fetch.contentType', value: item.contentType || '—' },
      {
        labelKey: 'researcher.fetch.contentLength',
        value: item.contentLengthBytes == null ? '—' : formatBytes(item.contentLengthBytes)
      },
      { labelKey: 'researcher.fetch.contentHash', value: item.contentHash || '—' },
      {
        labelKey: 'researcher.fetch.duration',
        value: item.durationMs == null ? '—' : `${item.durationMs} ms`
      },
      { labelKey: 'researcher.fetch.depth', value: String(item.crawlDepth ?? 0) },
      {
        labelKey: 'researcher.fetch.robotsAllowed',
        value: item.robotsAllowed
          ? this.translate.instant('researcher.common.yes')
          : this.translate.instant('researcher.common.no')
      },
      {
        labelKey: 'researcher.fetch.blockedBySsrf',
        value: item.blockedBySsrf
          ? this.translate.instant('researcher.common.yes')
          : this.translate.instant('researcher.common.no')
      },
      {
        labelKey: 'researcher.fetch.wasRedirected',
        value: item.wasRedirected
          ? this.translate.instant('researcher.common.yes')
          : this.translate.instant('researcher.common.no')
      },
      { labelKey: 'researcher.fetch.redirectCount', value: String(item.redirectCount ?? 0) },
      { labelKey: 'researcher.fetch.error', value: item.error || '—' },
      { labelKey: 'researcher.fetch.crawledAt', value: formatDate(item.crawledAtUtc) }
    ];
  });

  readonly excerptText = computed(() => this.page()?.textExcerpt ?? '');
  readonly hasExcerpt = computed(() => !!this.page()?.textExcerpt);

  ngOnInit(): void {
    const runId = this.route.snapshot.paramMap.get('runId') ?? '';
    const pageId = this.route.snapshot.paramMap.get('pageId') ?? '';
    this.runId.set(runId);
    this.pageId.set(pageId);
    if (!runId || !pageId) {
      this.loading.set(false);
      return;
    }
    this.researchRunApi.getCrawledPage(runId, pageId).subscribe({
      next: (item) => {
        this.page.set(item);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        void this.router.navigate(['/researcher/fetch'], { queryParams: { runId } });
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

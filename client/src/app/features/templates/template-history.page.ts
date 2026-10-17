import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { EmailTemplateDetail } from '../../core/models/template.model';
import { TemplateHistoryRow } from '../../core/models/template-history-row.model';
import { TemplateRecipientHistoryItem } from '../../core/models/template-recipient.model';
import { TemplateApiService } from '../../core/services/template-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatDate } from '../../core/utils/format.utils';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';

@Component({
  selector: 'app-template-history-page',
  imports: [TranslatePipe, RouterLink, LoadingComponent, EmptyStateComponent],
  templateUrl: './template-history.page.html',
  styleUrl: './template-history.page.scss'
})
export class TemplateHistoryPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly templateApi = inject(TemplateApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly template = signal<EmailTemplateDetail | null>(null);
  readonly rows = signal<TemplateHistoryRow[]>([]);
  readonly search = signal('');

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.router.navigate(['/templates']);
      return;
    }
    this.loadTemplate(id);
    this.loadHistory(id);
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    const id = this.template()?.id;
    if (!id) {
      return;
    }
    this.loadHistory(id);
  }

  private loadTemplate(id: string): void {
    this.templateApi.get(id).subscribe({
      next: (template) => this.template.set(template),
      error: (err: HttpErrorResponse) => {
        this.notification.error(this.extractError(err));
        this.router.navigate(['/templates']);
      }
    });
  }

  private loadHistory(id: string): void {
    this.loading.set(true);
    const term = this.search().trim();
    this.templateApi.recipientHistory(id, term || undefined).subscribe({
      next: (items) => {
        this.rows.set(this.mapRows(items ?? []));
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.rows.set([]);
        this.loading.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  private mapRows(items: TemplateRecipientHistoryItem[]): TemplateHistoryRow[] {
    const rows: TemplateHistoryRow[] = [];
    for (const item of items) {
      rows.push({
        ...item,
        statusLabelKey: this.resolveStatusKey(item.status),
        sentLabel: formatDate(item.sentAtUtc ?? item.updatedAtUtc),
        campaignLabel: item.campaignSubject || item.campaignId
      });
    }
    return rows;
  }

  private resolveStatusKey(status: string): string {
    if (status === 'sent') {
      return 'templates.historyStatusSent';
    }
    if (status === 'failed') {
      return 'templates.historyStatusFailed';
    }
    return 'templates.historyStatusClaiming';
  }

  private extractError(err: HttpErrorResponse): string {
    if (err.error?.message) {
      return err.error.message;
    }
    return this.translate.instant('common.errorGeneric');
  }
}

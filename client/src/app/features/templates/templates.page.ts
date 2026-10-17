import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  EmailTemplatePreview,
  EmailTemplateSummary,
  TemplateListRow,
  TemplateStatusFilter
} from '../../core/models/template.model';
import { TemplatePreviewView } from '../../core/models/template-preview-view.model';
import { TemplateApiService } from '../../core/services/template-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatBytes, formatDate } from '../../core/utils/format.utils';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { TemplatePreviewComponent } from './template-preview/template-preview.component';

@Component({
  selector: 'app-templates-page',
  imports: [
    TranslatePipe,
    LoadingComponent,
    EmptyStateComponent,
    ConfirmDialogComponent,
    TemplatePreviewComponent
  ],
  templateUrl: './templates.page.html',
  styleUrl: './templates.page.scss'
})
export class TemplatesPage implements OnInit {
  private readonly templateApi = inject(TemplateApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly templates = signal<TemplateListRow[]>([]);
  readonly search = signal('');
  readonly statusFilter = signal<TemplateStatusFilter>('all');
  readonly deleteId = signal<string | null>(null);
  readonly previewVisible = signal(false);
  readonly previewLoading = signal(false);
  readonly preview = signal<TemplatePreviewView | null>(null);

  readonly statusOptions: { value: TemplateStatusFilter; labelKey: string }[] = [
    { value: 'all', labelKey: 'templates.filterAll' },
    { value: 'active', labelKey: 'templates.filterActive' },
    { value: 'inactive', labelKey: 'templates.filterInactive' }
  ];

  ngOnInit(): void {
    this.loadTemplates();
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    this.loadTemplates();
  }

  onStatusChange(value: string): void {
    this.statusFilter.set(value as TemplateStatusFilter);
    this.loadTemplates();
  }

  openCreate(): void {
    this.router.navigate(['/templates/new']);
  }

  openEdit(id: string): void {
    this.router.navigate(['/templates', id, 'edit']);
  }

  useTemplate(id: string): void {
    this.router.navigate(['/compose'], { queryParams: { templateId: id } });
  }

  openHistory(id: string): void {
    this.router.navigate(['/templates', id, 'history']);
  }

  duplicate(id: string): void {
    this.saving.set(true);
    this.templateApi.duplicate(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('templates.duplicateSuccess'));
        this.loadTemplates();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  openPreview(id: string): void {
    this.previewVisible.set(true);
    this.previewLoading.set(true);
    this.preview.set(null);
    this.templateApi.preview(id).subscribe({
      next: (result) => {
        this.preview.set(this.mapPreview(result));
        this.previewLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.previewLoading.set(false);
        this.previewVisible.set(false);
        this.notifyError(err);
      }
    });
  }

  closePreview(): void {
    this.previewVisible.set(false);
    this.preview.set(null);
  }

  openDelete(id: string): void {
    this.deleteId.set(id);
  }

  closeDelete(): void {
    this.deleteId.set(null);
  }

  confirmDelete(): void {
    const id = this.deleteId();
    if (!id) {
      return;
    }
    this.saving.set(true);
    this.templateApi.delete(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleteId.set(null);
        this.notification.success(this.translate.instant('templates.deleteSuccess'));
        this.loadTemplates();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadTemplates(): void {
    this.loading.set(true);
    const term = this.search().trim();
    this.templateApi.list(term || undefined, this.statusFilter()).subscribe({
      next: (items) => {
        this.templates.set(this.mapRows(items ?? []));
        this.loading.set(false);
      },
      error: () => {
        this.templates.set([]);
        this.loading.set(false);
      }
    });
  }

  private mapRows(items: EmailTemplateSummary[]): TemplateListRow[] {
    const rows: TemplateListRow[] = [];
    for (const item of items) {
      rows.push({
        ...item,
        updatedLabel: formatDate(item.updatedAtUtc ?? null),
        statusLabelKey: item.isActive ? 'templates.statusActive' : 'templates.statusInactive',
        signatureLabel: item.signatureName || this.translate.instant('templates.noSignature'),
        emailAccountLabel:
          item.emailAccountAddress ||
          item.emailAccountName ||
          this.translate.instant('templates.noEmailAccount'),
        lastUsedLabel: item.lastUsedAtUtc
          ? formatDate(item.lastUsedAtUtc)
          : this.translate.instant('templates.neverUsed')
      });
    }
    return rows;
  }

  private mapPreview(result: EmailTemplatePreview): TemplatePreviewView {
    const attachments = [];
    for (const file of result.attachments) {
      attachments.push({
        id: file.id,
        fileName: file.fileName,
        contentType: file.contentType,
        sizeLabel: formatBytes(file.sizeBytes)
      });
    }
    return {
      id: result.id,
      name: result.name,
      subject: result.subject,
      htmlBody: result.htmlBody,
      signatureName: result.signatureName,
      attachments
    };
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

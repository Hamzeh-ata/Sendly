import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { ResearchRunSummary } from '../../core/models/prospecting.model';
import { ResearchRunListRow } from '../../core/models/prospect-row.model';
import { TemplateFilterTab } from '../../core/models/template-filter-tab.model';
import { EmailTemplateSummary } from '../../core/models/template.model';
import { ResearchRunApiService } from '../../core/services/research-run-api.service';
import { TemplateApiService } from '../../core/services/template-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatDate } from '../../core/utils/format.utils';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-research-runs-page',
  imports: [
    TranslatePipe,
    LoadingComponent,
    EmptyStateComponent,
    StatusBadgeComponent,
    ConfirmDialogComponent
  ],
  templateUrl: './research-runs.page.html',
  styleUrl: './research-runs.page.scss'
})
export class ResearchRunsPage implements OnInit {
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly templateApi = inject(TemplateApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly runs = signal<ResearchRunListRow[]>([]);
  readonly tabs = signal<TemplateFilterTab[]>([]);
  readonly activeTemplateId = signal<string | null>(null);
  readonly projectId = signal<string | null>(null);
  readonly deleteId = signal<string | null>(null);

  ngOnInit(): void {
    const projectId = this.route.snapshot.queryParamMap.get('projectId');
    const templateId = this.route.snapshot.queryParamMap.get('templateId');
    this.projectId.set(projectId);
    this.activeTemplateId.set(templateId);
    this.loadPage();
  }

  selectTab(templateId: string | null): void {
    this.activeTemplateId.set(templateId);
    this.syncTemplateQuery(templateId);
    this.loadRuns();
  }

  openDetail(id: string): void {
    this.router.navigate(['/research-runs', id]);
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
    this.researchRunApi.delete(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleteId.set(null);
        this.notification.success(this.translate.instant('researchRuns.deleteSuccess'));
        this.loadRuns();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadPage(): void {
    this.loading.set(true);
    forkJoin({
      templates: this.templateApi.list(undefined, 'active'),
      runs: this.researchRunApi.list(this.projectId() || undefined, this.activeTemplateId() || undefined)
    }).subscribe({
      next: ({ templates, runs }) => {
        this.tabs.set(this.buildTabs(templates ?? []));
        this.runs.set(this.mapRows(runs ?? []));
        this.loading.set(false);
      },
      error: () => {
        this.tabs.set(this.buildTabs([]));
        this.runs.set([]);
        this.loading.set(false);
      }
    });
  }

  private loadRuns(): void {
    this.loading.set(true);
    this.researchRunApi
      .list(this.projectId() || undefined, this.activeTemplateId() || undefined)
      .subscribe({
        next: (items) => {
          this.runs.set(this.mapRows(items ?? []));
          this.loading.set(false);
        },
        error: () => {
          this.runs.set([]);
          this.loading.set(false);
        }
      });
  }

  private buildTabs(templates: EmailTemplateSummary[]): TemplateFilterTab[] {
    const tabs: TemplateFilterTab[] = [
      {
        id: null,
        label: null,
        labelKey: 'researchRuns.tabAll'
      }
    ];

    for (const template of templates) {
      tabs.push({
        id: template.id,
        label: template.name,
        labelKey: null
      });
    }

    return tabs;
  }

  private syncTemplateQuery(templateId: string | null): void {
    const queryParams: Record<string, string | null> = {
      templateId,
      projectId: this.projectId()
    };
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  private mapRows(items: ResearchRunSummary[]): ResearchRunListRow[] {
    const rows: ResearchRunListRow[] = [];
    for (const item of items) {
      rows.push({
        ...item,
        createdLabel: formatDate(item.createdAtUtc ?? null),
        statusLabelKey: `researchStatus.${item.status}`
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

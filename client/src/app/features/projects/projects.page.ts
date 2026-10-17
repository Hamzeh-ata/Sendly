import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { OutreachProjectSummary, StartResearchRequest } from '../../core/models/prospecting.model';
import { ProjectListRow } from '../../core/models/project-row.model';
import { TemplateFilterTab } from '../../core/models/template-filter-tab.model';
import { EmailTemplateSummary } from '../../core/models/template.model';
import { ProjectApiService } from '../../core/services/project-api.service';
import { TemplateApiService } from '../../core/services/template-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatDate } from '../../core/utils/format.utils';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';

@Component({
  selector: 'app-projects-page',
  imports: [TranslatePipe, LoadingComponent, EmptyStateComponent, ConfirmDialogComponent],
  templateUrl: './projects.page.html',
  styleUrl: './projects.page.scss'
})
export class ProjectsPage implements OnInit {
  private readonly projectApi = inject(ProjectApiService);
  private readonly templateApi = inject(TemplateApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly projects = signal<ProjectListRow[]>([]);
  readonly tabs = signal<TemplateFilterTab[]>([]);
  readonly activeTemplateId = signal<string | null>(null);
  readonly search = signal('');
  readonly deactivateId = signal<string | null>(null);
  readonly deleteId = signal<string | null>(null);
  readonly researchProjectId = signal<string | null>(null);
  readonly researchRequestedCount = signal(50);
  readonly researchIncludeEmail = signal(true);
  readonly researchIncludeWhatsApp = signal(false);
  readonly researchMinScore = signal(60);

  ngOnInit(): void {
    const templateId = this.route.snapshot.queryParamMap.get('templateId');
    this.activeTemplateId.set(templateId);
    this.loadPage();
  }

  selectTab(templateId: string | null): void {
    this.activeTemplateId.set(templateId);
    this.syncTemplateQuery(templateId);
    this.loadProjects();
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    this.loadProjects();
  }

  openCreate(): void {
    this.router.navigate(['/projects/new']);
  }

  openView(id: string): void {
    this.router.navigate(['/projects', id]);
  }

  openEdit(id: string): void {
    this.router.navigate(['/projects', id, 'edit']);
  }

  openResearch(id: string): void {
    const project = this.projects().find((item) => item.id === id);
    this.researchProjectId.set(id);
    this.researchRequestedCount.set(50);
    this.researchIncludeEmail.set(project?.enableEmail ?? true);
    this.researchIncludeWhatsApp.set(project?.enableWhatsApp ?? false);
    this.researchMinScore.set(60);
  }

  closeResearch(): void {
    this.researchProjectId.set(null);
  }

  onResearchCountChange(value: string): void {
    this.researchRequestedCount.set(Number(value) || 0);
  }

  onResearchEmailChange(checked: boolean): void {
    this.researchIncludeEmail.set(checked);
  }

  onResearchWhatsAppChange(checked: boolean): void {
    this.researchIncludeWhatsApp.set(checked);
  }

  onResearchMinScoreChange(value: string): void {
    this.researchMinScore.set(Number(value) || 0);
  }

  confirmResearch(): void {
    const id = this.researchProjectId();
    if (!id) {
      return;
    }
    const request: StartResearchRequest = {
      requestedProspectCount: this.researchRequestedCount(),
      includeEmail: this.researchIncludeEmail(),
      includeWhatsApp: this.researchIncludeWhatsApp(),
      minQualificationScore: this.researchMinScore()
    };
    this.saving.set(true);
    this.projectApi.startResearch(id, request).subscribe({
      next: (run) => {
        this.saving.set(false);
        this.researchProjectId.set(null);
        this.notification.success(this.translate.instant('projects.researchStarted'));
        this.router.navigate(['/research-runs', run.id]);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  openDeactivate(id: string): void {
    this.deactivateId.set(id);
  }

  closeDeactivate(): void {
    this.deactivateId.set(null);
  }

  confirmDeactivate(): void {
    const id = this.deactivateId();
    if (!id) {
      return;
    }
    this.saving.set(true);
    this.projectApi.deactivate(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deactivateId.set(null);
        this.notification.success(this.translate.instant('projects.deactivateSuccess'));
        this.loadProjects();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
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
    this.projectApi.delete(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleteId.set(null);
        this.notification.success(this.translate.instant('projects.deleteSuccess'));
        this.loadProjects();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadPage(): void {
    this.loading.set(true);
    const term = this.search().trim();
    forkJoin({
      templates: this.templateApi.list(undefined, 'active'),
      projects: this.projectApi.list(term || undefined, this.activeTemplateId() || undefined)
    }).subscribe({
      next: ({ templates, projects }) => {
        this.tabs.set(this.buildTabs(templates ?? []));
        this.projects.set(this.mapRows(projects ?? []));
        this.loading.set(false);
      },
      error: () => {
        this.tabs.set(this.buildTabs([]));
        this.projects.set([]);
        this.loading.set(false);
      }
    });
  }

  private loadProjects(): void {
    this.loading.set(true);
    const term = this.search().trim();
    this.projectApi.list(term || undefined, this.activeTemplateId() || undefined).subscribe({
      next: (items) => {
        this.projects.set(this.mapRows(items ?? []));
        this.loading.set(false);
      },
      error: () => {
        this.projects.set([]);
        this.loading.set(false);
      }
    });
  }

  private buildTabs(templates: EmailTemplateSummary[]): TemplateFilterTab[] {
    const tabs: TemplateFilterTab[] = [
      {
        id: null,
        label: null,
        labelKey: 'projects.tabAll'
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
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { templateId },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  private mapRows(items: OutreachProjectSummary[]): ProjectListRow[] {
    const rows: ProjectListRow[] = [];
    for (const item of items) {
      rows.push({
        ...item,
        countriesLabel: this.joinSnippet(item.countries),
        keywordsLabel: this.joinSnippet(item.keywords),
        contactMethodsLabel: this.buildContactMethods(item.enableEmail, item.enableWhatsApp),
        lastResearchLabel: item.lastResearchAtUtc
          ? formatDate(item.lastResearchAtUtc)
          : this.translate.instant('projects.neverResearched'),
        statusLabelKey: item.isActive ? 'projects.statusActive' : 'projects.statusInactive',
        descriptionLabel: this.snippet(item.description ?? '')
      });
    }
    return rows;
  }

  private buildContactMethods(enableEmail: boolean, enableWhatsApp: boolean): string {
    const parts: string[] = [];
    if (enableEmail) {
      parts.push(this.translate.instant('projects.channelEmail'));
    }
    if (enableWhatsApp) {
      parts.push(this.translate.instant('projects.channelWhatsApp'));
    }
    if (parts.length === 0) {
      return this.translate.instant('projects.noChannels');
    }
    return parts.join(', ');
  }

  private joinSnippet(values: string[]): string {
    if (!values || values.length === 0) {
      return '—';
    }
    return this.snippet(values.join(', '));
  }

  private snippet(value: string): string {
    const trimmed = value.trim();
    if (!trimmed) {
      return '—';
    }
    if (trimmed.length <= 80) {
      return trimmed;
    }
    return `${trimmed.slice(0, 77)}...`;
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

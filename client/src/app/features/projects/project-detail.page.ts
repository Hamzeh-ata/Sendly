import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { OutreachProjectDetail, StartResearchRequest } from '../../core/models/prospecting.model';
import { ProjectResearchRunRow } from '../../core/models/project-row.model';
import { ProjectApiService } from '../../core/services/project-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatDate } from '../../core/utils/format.utils';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-project-detail-page',
  imports: [RouterLink, TranslatePipe, LoadingComponent, StatusBadgeComponent, ConfirmDialogComponent],
  templateUrl: './project-detail.page.html',
  styleUrl: './project-detail.page.scss'
})
export class ProjectDetailPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly projectApi = inject(ProjectApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly project = signal<OutreachProjectDetail | null>(null);
  readonly researchRuns = signal<ProjectResearchRunRow[]>([]);
  readonly researchOpen = signal(false);
  readonly deleteOpen = signal(false);
  readonly researchRequestedCount = signal(50);
  readonly researchIncludeEmail = signal(true);
  readonly researchIncludeWhatsApp = signal(false);
  readonly researchMinScore = signal(60);

  readonly keywordsLabel = computed(() => this.joinOrDash(this.project()?.keywords));
  readonly countriesLabel = computed(() => this.joinOrDash(this.project()?.countries));
  readonly rolesLabel = computed(() => this.joinOrDash(this.project()?.targetRoles));
  readonly industriesLabel = computed(() => this.joinOrDash(this.project()?.industries));
  readonly contactMethodsLabel = computed(() => {
    const project = this.project();
    if (!project) {
      return '—';
    }
    const parts: string[] = [];
    if (project.enableEmail) {
      parts.push(this.translate.instant('projects.channelEmail'));
    }
    if (project.enableWhatsApp) {
      parts.push(this.translate.instant('projects.channelWhatsApp'));
    }
    return parts.length > 0 ? parts.join(', ') : this.translate.instant('projects.noChannels');
  });
  readonly duplicateHandlingLabel = computed(() => {
    const mode = this.project()?.duplicateHandling;
    if (mode === 'exclude') {
      return this.translate.instant('projects.duplicateExclude');
    }
    if (mode === 'allow') {
      return this.translate.instant('projects.duplicateAllow');
    }
    return this.translate.instant('projects.duplicateWarn');
  });
  readonly statusLabelKey = computed(() =>
    this.project()?.isActive ? 'projects.statusActive' : 'projects.statusInactive'
  );
  readonly researchInstructionsLabel = computed(() => {
    const value = this.project()?.researchInstructions?.trim();
    return value || this.translate.instant('projects.noResearchInstructions');
  });
  readonly templateNames = computed(() => {
    const templates = this.project()?.templates ?? [];
    const names: string[] = [];
    for (const item of templates) {
      names.push(item.templateName || item.templateId);
    }
    return names;
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id') ?? '';
    if (!id) {
      this.loading.set(false);
      return;
    }
    this.loadProject(id);
  }

  openEdit(): void {
    const id = this.project()?.id;
    if (!id) {
      return;
    }
    this.router.navigate(['/projects', id, 'edit']);
  }

  openProspects(): void {
    const id = this.project()?.id;
    if (!id) {
      return;
    }
    this.router.navigate(['/prospects'], { queryParams: { projectId: id } });
  }

  openSequences(): void {
    const id = this.project()?.id;
    if (!id) {
      return;
    }
    this.router.navigate(['/sequences'], { queryParams: { projectId: id } });
  }

  openResearchRun(id: string): void {
    this.router.navigate(['/research-runs', id]);
  }

  openResearch(): void {
    const project = this.project();
    if (!project) {
      return;
    }
    this.researchRequestedCount.set(project.defaultRequestedProspectCount || 50);
    this.researchIncludeEmail.set(project.enableEmail);
    this.researchIncludeWhatsApp.set(project.enableWhatsApp);
    this.researchMinScore.set(project.minQualificationScore || 60);
    this.researchOpen.set(true);
  }

  closeResearch(): void {
    this.researchOpen.set(false);
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
    const id = this.project()?.id;
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
        this.researchOpen.set(false);
        this.notification.success(this.translate.instant('projects.researchStarted'));
        this.router.navigate(['/research-runs', run.id]);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  openDelete(): void {
    this.deleteOpen.set(true);
  }

  closeDelete(): void {
    this.deleteOpen.set(false);
  }

  confirmDelete(): void {
    const id = this.project()?.id;
    if (!id) {
      return;
    }
    this.saving.set(true);
    this.projectApi.delete(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleteOpen.set(false);
        this.notification.success(this.translate.instant('projects.deleteSuccess'));
        this.router.navigate(['/projects']);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadProject(id: string): void {
    this.loading.set(true);
    this.projectApi.get(id).subscribe({
      next: (detail) => {
        this.project.set(detail);
        this.researchRuns.set(this.mapResearchRuns(detail.recentResearchRuns ?? []));
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/projects']);
      }
    });
  }

  private mapResearchRuns(
    items: OutreachProjectDetail['recentResearchRuns']
  ): ProjectResearchRunRow[] {
    const rows: ProjectResearchRunRow[] = [];
    for (const item of items) {
      rows.push({
        id: item.id,
        status: item.status,
        progressPercent: item.progressPercent,
        requestedProspectCount: item.requestedProspectCount,
        qualifiedCount: item.qualifiedCount,
        createdLabel: formatDate(item.createdAtUtc ?? null)
      });
    }
    return rows;
  }

  private joinOrDash(values?: string[] | null): string {
    if (!values || values.length === 0) {
      return '—';
    }
    return values.join(', ');
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

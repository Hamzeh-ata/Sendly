import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  DuplicateHandlingMode,
  ProjectTemplateLink,
  UpsertOutreachProject
} from '../../core/models/prospecting.model';
import { ProjectTemplateOption } from '../../core/models/project-row.model';
import { ProjectApiService } from '../../core/services/project-api.service';
import { TemplateApiService } from '../../core/services/template-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { LoadingComponent } from '../../shared/components/loading/loading.component';

@Component({
  selector: 'app-project-editor-page',
  imports: [TranslatePipe, LoadingComponent],
  templateUrl: './project-editor.page.html',
  styleUrl: './project-editor.page.scss'
})
export class ProjectEditorPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly projectApi = inject(ProjectApiService);
  private readonly templateApi = inject(TemplateApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly name = signal('');
  readonly description = signal('');
  readonly researchInstructions = signal('');
  readonly isActive = signal(true);
  readonly minQualificationScore = signal(60);
  readonly duplicateHandling = signal<DuplicateHandlingMode>('warn');
  readonly enableEmail = signal(true);
  readonly enableWhatsApp = signal(false);
  readonly defaultRequestedProspectCount = signal(50);
  readonly keywordsText = signal('');
  readonly excludedKeywordsText = signal('');
  readonly countriesText = signal('');
  readonly industriesText = signal('');
  readonly excludedIndustriesText = signal('');
  readonly targetRolesText = signal('');
  readonly templateOptions = signal<ProjectTemplateOption[]>([]);
  readonly selectedTemplateIds = signal<string[]>([]);

  readonly pageTitleKey = computed(() =>
    this.editingId() ? 'projects.editTitle' : 'projects.createTitle'
  );
  readonly canSave = computed(() => this.name().trim().length > 0 && !this.saving());
  readonly templateChecklist = computed(() => {
    const selected = this.selectedTemplateIds();
    const options: ProjectTemplateOption[] = [];
    for (const item of this.templateOptions()) {
      options.push({
        id: item.id,
        name: item.name,
        selected: selected.includes(item.id)
      });
    }
    return options;
  });

  readonly duplicateOptions: { value: DuplicateHandlingMode; labelKey: string }[] = [
    { value: 'warn', labelKey: 'projects.duplicateWarn' },
    { value: 'exclude', labelKey: 'projects.duplicateExclude' },
    { value: 'allow', labelKey: 'projects.duplicateAllow' }
  ];

  ngOnInit(): void {
    this.loadTemplates();
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.editingId.set(id);
      this.loadProject(id);
      return;
    }
    this.loading.set(false);
  }

  onNameChange(value: string): void {
    this.name.set(value);
  }

  onDescriptionChange(value: string): void {
    this.description.set(value);
  }

  onResearchInstructionsChange(value: string): void {
    this.researchInstructions.set(value);
  }

  onActiveChange(checked: boolean): void {
    this.isActive.set(checked);
  }

  onMinScoreChange(value: string): void {
    this.minQualificationScore.set(Number(value) || 0);
  }

  onDuplicateHandlingChange(value: string): void {
    this.duplicateHandling.set(value as DuplicateHandlingMode);
  }

  onEnableEmailChange(checked: boolean): void {
    this.enableEmail.set(checked);
  }

  onEnableWhatsAppChange(checked: boolean): void {
    this.enableWhatsApp.set(checked);
  }

  onDefaultCountChange(value: string): void {
    this.defaultRequestedProspectCount.set(Number(value) || 0);
  }

  onKeywordsChange(value: string): void {
    this.keywordsText.set(value);
  }

  onExcludedKeywordsChange(value: string): void {
    this.excludedKeywordsText.set(value);
  }

  onCountriesChange(value: string): void {
    this.countriesText.set(value);
  }

  onIndustriesChange(value: string): void {
    this.industriesText.set(value);
  }

  onExcludedIndustriesChange(value: string): void {
    this.excludedIndustriesText.set(value);
  }

  onTargetRolesChange(value: string): void {
    this.targetRolesText.set(value);
  }

  toggleTemplate(id: string): void {
    this.selectedTemplateIds.update((ids) => {
      if (ids.includes(id)) {
        return ids.filter((item) => item !== id);
      }
      return [...ids, id];
    });
  }

  cancel(): void {
    const id = this.editingId();
    if (id) {
      this.router.navigate(['/projects', id]);
      return;
    }
    this.router.navigate(['/projects']);
  }

  save(): void {
    if (!this.canSave()) {
      this.notification.error(this.translate.instant('projects.validationRequired'));
      return;
    }

    const payload: UpsertOutreachProject = {
      name: this.name().trim(),
      description: this.description().trim() || null,
      researchInstructions: this.researchInstructions().trim() || null,
      isActive: this.isActive(),
      minQualificationScore: this.minQualificationScore(),
      duplicateHandling: this.duplicateHandling(),
      enableEmail: this.enableEmail(),
      enableWhatsApp: this.enableWhatsApp(),
      defaultRequestedProspectCount: this.defaultRequestedProspectCount(),
      keywords: this.parseList(this.keywordsText()),
      excludedKeywords: this.parseList(this.excludedKeywordsText()),
      countries: this.parseList(this.countriesText()),
      industries: this.parseList(this.industriesText()),
      excludedIndustries: this.parseList(this.excludedIndustriesText()),
      targetRoles: this.parseList(this.targetRolesText()),
      templates: this.buildTemplateLinks()
    };

    this.saving.set(true);
    const editingId = this.editingId();
    const request$ = editingId
      ? this.projectApi.update(editingId, payload)
      : this.projectApi.create(payload);

    request$.subscribe({
      next: (project) => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('projects.saveSuccess'));
        this.router.navigate(['/projects', project.id]);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadProject(id: string): void {
    this.projectApi.get(id).subscribe({
      next: (project) => {
        this.name.set(project.name);
        this.description.set(project.description ?? '');
        this.researchInstructions.set(project.researchInstructions ?? '');
        this.isActive.set(project.isActive);
        this.minQualificationScore.set(project.minQualificationScore);
        this.duplicateHandling.set(project.duplicateHandling);
        this.enableEmail.set(project.enableEmail);
        this.enableWhatsApp.set(project.enableWhatsApp);
        this.defaultRequestedProspectCount.set(project.defaultRequestedProspectCount);
        this.keywordsText.set(this.joinList(project.keywords));
        this.excludedKeywordsText.set(this.joinList(project.excludedKeywords));
        this.countriesText.set(this.joinList(project.countries));
        this.industriesText.set(this.joinList(project.industries));
        this.excludedIndustriesText.set(this.joinList(project.excludedIndustries));
        this.targetRolesText.set(this.joinList(project.targetRoles));
        const selectedIds: string[] = [];
        for (const link of project.templates) {
          selectedIds.push(link.templateId);
        }
        this.selectedTemplateIds.set(selectedIds);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/projects']);
      }
    });
  }

  private loadTemplates(): void {
    this.templateApi.list(undefined, 'active').subscribe({
      next: (items) => {
        const options: ProjectTemplateOption[] = [];
        for (const item of items ?? []) {
          options.push({ id: item.id, name: item.name, selected: false });
        }
        this.templateOptions.set(options);
      },
      error: () => this.templateOptions.set([])
    });
  }

  private buildTemplateLinks(): ProjectTemplateLink[] {
    const links: ProjectTemplateLink[] = [];
    let sortOrder = 0;
    const selected = this.selectedTemplateIds();
    for (const item of this.templateOptions()) {
      if (!selected.includes(item.id)) {
        continue;
      }
      links.push({
        templateId: item.id,
        templateName: item.name,
        channel: 'email',
        sortOrder
      });
      sortOrder += 1;
    }
    return links;
  }

  private parseList(raw: string): string[] {
    const parts = raw.split(/[,\n\r]+/);
    const unique: string[] = [];
    for (const part of parts) {
      const trimmed = part.trim();
      if (trimmed && !unique.includes(trimmed)) {
        unique.push(trimmed);
      }
    }
    return unique;
  }

  private joinList(values: string[]): string {
    if (!values || values.length === 0) {
      return '';
    }
    return values.join('\n');
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

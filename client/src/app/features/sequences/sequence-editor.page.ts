import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { OutreachChannelType } from '../../core/models/prospecting.model';
import {
  OutreachSequenceStep,
  SequenceStepEditorRow,
  SequenceTemplateOption,
  UpsertOutreachSequence
} from '../../core/models/sequence.model';
import { ProjectFilterOption } from '../../core/models/prospect-row.model';
import { ProjectApiService } from '../../core/services/project-api.service';
import { SequenceApiService } from '../../core/services/sequence-api.service';
import { TemplateApiService } from '../../core/services/template-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { LoadingComponent } from '../../shared/components/loading/loading.component';

@Component({
  selector: 'app-sequence-editor-page',
  imports: [TranslatePipe, LoadingComponent],
  templateUrl: './sequence-editor.page.html',
  styleUrl: './sequence-editor.page.scss'
})
export class SequenceEditorPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly sequenceApi = inject(SequenceApiService);
  private readonly projectApi = inject(ProjectApiService);
  private readonly templateApi = inject(TemplateApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  private stepKeyCounter = 0;

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly projectId = signal('');
  readonly name = signal('');
  readonly description = signal('');
  readonly isActive = signal(true);
  readonly steps = signal<SequenceStepEditorRow[]>([]);
  readonly projects = signal<ProjectFilterOption[]>([]);
  readonly templates = signal<SequenceTemplateOption[]>([]);
  readonly showProjectSelect = signal(false);

  readonly pageTitleKey = computed(() =>
    this.editingId() ? 'sequences.editTitle' : 'sequences.createTitle'
  );
  readonly canSave = computed(
    () =>
      this.name().trim().length > 0 &&
      this.projectId().trim().length > 0 &&
      this.steps().length > 0 &&
      !this.saving()
  );

  readonly channelOptions: { value: OutreachChannelType; labelKey: string }[] = [
    { value: 'email', labelKey: 'projects.channelEmail' },
    { value: 'whatsApp', labelKey: 'projects.channelWhatsApp' }
  ];

  ngOnInit(): void {
    this.loadTemplates();
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.editingId.set(id);
      this.showProjectSelect.set(false);
      this.loadSequence(id);
      return;
    }
    const projectId = this.route.snapshot.queryParamMap.get('projectId') ?? '';
    this.projectId.set(projectId);
    this.showProjectSelect.set(!projectId);
    if (!projectId) {
      this.loadProjects();
    }
    this.steps.set([this.createEmptyStep(1)]);
    this.loading.set(false);
  }

  onNameChange(value: string): void {
    this.name.set(value);
  }

  onDescriptionChange(value: string): void {
    this.description.set(value);
  }

  onActiveChange(checked: boolean): void {
    this.isActive.set(checked);
  }

  onProjectChange(value: string): void {
    this.projectId.set(value);
  }

  addStep(): void {
    let maxOrder = 0;
    for (const item of this.steps()) {
      if (item.stepOrder > maxOrder) {
        maxOrder = item.stepOrder;
      }
    }
    this.steps.update((items) => [...items, this.createEmptyStep(maxOrder + 1)]);
  }

  removeStep(clientKey: string): void {
    this.steps.set(this.steps().filter((item) => item.clientKey !== clientKey));
  }

  onStepChannelChange(clientKey: string, value: string): void {
    this.steps.update((items) => {
      const rows: SequenceStepEditorRow[] = [];
      for (const item of items) {
        if (item.clientKey !== clientKey) {
          rows.push(item);
          continue;
        }
        const channel = value as OutreachChannelType;
        rows.push({
          ...item,
          channel,
          showWhatsAppFields: channel === 'whatsApp'
        });
      }
      return rows;
    });
  }

  onStepTemplateChange(clientKey: string, value: string): void {
    this.updateStepField(clientKey, (item) => ({ ...item, templateId: value }));
  }

  onStepDelayChange(clientKey: string, value: string): void {
    this.updateStepField(clientKey, (item) => ({
      ...item,
      delayHoursAfterPrevious: Number(value) || 0
    }));
  }

  onStepWhatsAppNameChange(clientKey: string, value: string): void {
    this.updateStepField(clientKey, (item) => ({ ...item, whatsAppTemplateName: value }));
  }

  onStepWhatsAppLanguageChange(clientKey: string, value: string): void {
    this.updateStepField(clientKey, (item) => ({ ...item, whatsAppTemplateLanguage: value }));
  }

  onStepOrderChange(clientKey: string, value: string): void {
    this.updateStepField(clientKey, (item) => ({
      ...item,
      stepOrder: Math.max(1, Number(value) || 1)
    }));
  }

  onStepVariantChange(clientKey: string, value: string): void {
    this.updateStepField(clientKey, (item) => ({ ...item, variantKey: value }));
  }

  onStepTrafficChange(clientKey: string, value: string): void {
    this.updateStepField(clientKey, (item) => ({
      ...item,
      trafficPercent: Math.min(100, Math.max(1, Number(value) || 100))
    }));
  }

  cancel(): void {
    const editingId = this.editingId();
    if (editingId) {
      this.router.navigate(['/sequences', editingId]);
      return;
    }
    const projectId = this.projectId();
    if (projectId) {
      this.router.navigate(['/sequences'], { queryParams: { projectId } });
      return;
    }
    this.router.navigate(['/sequences']);
  }

  save(): void {
    if (!this.canSave()) {
      this.notification.error(this.translate.instant('sequences.validationRequired'));
      return;
    }
    const payload = this.buildPayload();
    this.saving.set(true);
    const editingId = this.editingId();
    if (editingId) {
      this.sequenceApi.update(editingId, payload).subscribe({
        next: (detail) => {
          this.saving.set(false);
          this.notification.success(this.translate.instant('sequences.saveSuccess'));
          this.router.navigate(['/sequences', detail.id]);
        },
        error: (err: HttpErrorResponse) => {
          this.saving.set(false);
          this.notifyError(err);
        }
      });
      return;
    }
    this.sequenceApi.create(this.projectId(), payload).subscribe({
      next: (detail) => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('sequences.saveSuccess'));
        this.router.navigate(['/sequences', detail.id]);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadSequence(id: string): void {
    this.loading.set(true);
    this.sequenceApi.get(id).subscribe({
      next: (detail) => {
        this.projectId.set(detail.projectId);
        this.name.set(detail.name);
        this.description.set(detail.description ?? '');
        this.isActive.set(detail.isActive);
        this.steps.set(this.mapSteps(detail.steps ?? []));
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/sequences']);
      }
    });
  }

  private loadProjects(): void {
    this.projectApi.list().subscribe({
      next: (items) => {
        const options: ProjectFilterOption[] = [];
        for (const item of items ?? []) {
          options.push({ id: item.id, name: item.name });
        }
        this.projects.set(options);
      },
      error: () => this.projects.set([])
    });
  }

  private loadTemplates(): void {
    this.templateApi.list().subscribe({
      next: (items) => {
        const options: SequenceTemplateOption[] = [];
        for (const item of items ?? []) {
          options.push({ id: item.id, name: item.name });
        }
        this.templates.set(options);
      },
      error: () => this.templates.set([])
    });
  }

  private buildPayload(): UpsertOutreachSequence {
    const steps: OutreachSequenceStep[] = [];
    for (const item of this.steps()) {
      steps.push({
        id: item.id,
        stepOrder: item.stepOrder,
        channel: item.channel,
        templateId: item.templateId.trim() || null,
        delayHoursAfterPrevious: item.delayHoursAfterPrevious,
        whatsAppTemplateName: item.showWhatsAppFields
          ? item.whatsAppTemplateName.trim() || null
          : null,
        whatsAppTemplateLanguage: item.showWhatsAppFields
          ? item.whatsAppTemplateLanguage.trim() || null
          : null,
        variantKey: item.variantKey.trim() || null,
        trafficPercent: item.trafficPercent
      });
    }
    return {
      name: this.name().trim(),
      description: this.description().trim() || null,
      isActive: this.isActive(),
      steps
    };
  }

  private mapSteps(items: OutreachSequenceStep[]): SequenceStepEditorRow[] {
    const rows: SequenceStepEditorRow[] = [];
    for (const item of items) {
      rows.push({
        clientKey: this.nextStepKey(),
        id: item.id ?? null,
        stepOrder: item.stepOrder,
        channel: item.channel,
        templateId: item.templateId ?? '',
        delayHoursAfterPrevious: item.delayHoursAfterPrevious,
        whatsAppTemplateName: item.whatsAppTemplateName ?? '',
        whatsAppTemplateLanguage: item.whatsAppTemplateLanguage ?? '',
        variantKey: item.variantKey ?? '',
        trafficPercent: item.trafficPercent ?? 100,
        showWhatsAppFields: item.channel === 'whatsApp'
      });
    }
    return rows;
  }

  private createEmptyStep(stepOrder: number): SequenceStepEditorRow {
    return {
      clientKey: this.nextStepKey(),
      id: null,
      stepOrder,
      channel: 'email',
      templateId: '',
      delayHoursAfterPrevious: 0,
      whatsAppTemplateName: '',
      whatsAppTemplateLanguage: '',
      variantKey: '',
      trafficPercent: 100,
      showWhatsAppFields: false
    };
  }

  private updateStepField(
    clientKey: string,
    mapper: (item: SequenceStepEditorRow) => SequenceStepEditorRow
  ): void {
    this.steps.update((items) => {
      const rows: SequenceStepEditorRow[] = [];
      for (const item of items) {
        if (item.clientKey === clientKey) {
          rows.push(mapper(item));
          continue;
        }
        rows.push(item);
      }
      return rows;
    });
  }

  private nextStepKey(): string {
    this.stepKeyCounter += 1;
    return `step-${this.stepKeyCounter}`;
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

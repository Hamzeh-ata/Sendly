import { Component, computed, HostListener, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  OutreachChannelType,
  PrepareOutreachRequest,
  ProspectListFilter,
  ProspectSummary
} from '../../core/models/prospecting.model';
import {
  ProspectActiveFilterChip,
  ProspectFilterChoice,
  ProspectListRow,
  ProjectFilterOption
} from '../../core/models/prospect-row.model';
import { SequenceOption } from '../../core/models/sequence.model';
import { ProjectApiService } from '../../core/services/project-api.service';
import { ProspectApiService } from '../../core/services/prospect-api.service';
import { SequenceApiService } from '../../core/services/sequence-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatDate } from '../../core/utils/format.utils';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

type OpenFilterMenu = 'contactMethod' | 'qualification' | 'duplicate' | null;

@Component({
  selector: 'app-prospects-page',
  imports: [TranslatePipe, LoadingComponent, EmptyStateComponent, StatusBadgeComponent],
  templateUrl: './prospects.page.html',
  styleUrl: './prospects.page.scss'
})
export class ProspectsPage implements OnInit {
  private readonly prospectApi = inject(ProspectApiService);
  private readonly projectApi = inject(ProjectApiService);
  private readonly sequenceApi = inject(SequenceApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly prospects = signal<ProspectListRow[]>([]);
  readonly projects = signal<ProjectFilterOption[]>([]);
  readonly selectedIds = signal<string[]>([]);
  readonly search = signal('');
  readonly projectId = signal('');
  readonly researchRunId = signal('');
  readonly country = signal('');
  readonly industry = signal('');
  readonly statuses = signal<string[]>([]);
  readonly contactMethods = signal<string[]>([]);
  readonly qualifications = signal<string[]>([]);
  readonly duplicates = signal<string[]>([]);
  readonly openFilterMenu = signal<OpenFilterMenu>(null);
  readonly campaignOpen = signal(false);
  readonly campaignProspectIds = signal<string[]>([]);
  readonly campaignChannel = signal<OutreachChannelType>('email');
  readonly useAiRecommendedTemplate = signal(true);
  readonly enrollOpen = signal(false);
  readonly enrollSequences = signal<SequenceOption[]>([]);
  readonly selectedSequenceId = signal('');
  readonly enrollMixedProjects = signal(false);
  private queryParamsBound = false;

  private readonly statusValues = [
    'discovered',
    'researching',
    'qualified',
    'needsReview',
    'rejected',
    'readyForOutreach',
    'contacted',
    'doNotContact'
  ];

  private readonly contactMethodValues = [
    { value: 'email', labelKey: 'projects.channelEmail' },
    { value: 'whatsApp', labelKey: 'projects.channelWhatsApp' }
  ];

  private readonly qualificationValues = [
    { value: 'qualified', labelKey: 'prospects.qualifiedOnly' },
    { value: 'unqualified', labelKey: 'prospects.unqualifiedOnly' }
  ];

  private readonly duplicateValues = [
    { value: 'none', labelKey: 'duplicateKind.none' },
    { value: 'sameProject', labelKey: 'duplicateKind.sameProject' },
    { value: 'sameCompany', labelKey: 'duplicateKind.sameCompany' },
    { value: 'emailContacted', labelKey: 'duplicateKind.emailContacted' },
    { value: 'whatsAppContacted', labelKey: 'duplicateKind.whatsAppContacted' },
    { value: 'crossProject', labelKey: 'duplicateKind.crossProject' },
    { value: 'doNotContact', labelKey: 'duplicateKind.doNotContact' },
    { value: 'sameWebsite', labelKey: 'duplicateKind.sameWebsite' },
    { value: 'samePhone', labelKey: 'duplicateKind.samePhone' },
    { value: 'possibleDuplicate', labelKey: 'duplicateKind.possibleDuplicate' }
  ];

  readonly selectedCount = computed(() => this.selectedIds().length);
  readonly hasSelection = computed(() => this.selectedIds().length > 0);
  readonly resultCount = computed(() => this.prospects().length);
  readonly canEnrollInSequence = computed(
    () => this.hasSelection() && !this.enrollMixedProjects() && !this.saving()
  );
  readonly allSelected = computed(() => {
    const rows = this.prospects();
    if (rows.length === 0) {
      return false;
    }
    const selected = this.selectedIds();
    for (const row of rows) {
      if (!selected.includes(row.id)) {
        return false;
      }
    }
    return true;
  });
  readonly displayRows = computed(() => {
    const selected = this.selectedIds();
    const rows: ProspectListRow[] = [];
    for (const row of this.prospects()) {
      rows.push({ ...row, selected: selected.includes(row.id) });
    }
    return rows;
  });
  readonly canConfirmEnroll = computed(
    () => this.selectedSequenceId().trim().length > 0 && !this.saving()
  );
  readonly statusChoices = computed(() => this.buildChoices(this.statusValues, this.statuses(), 'prospectStatus'));
  readonly contactMethodChoices = computed(() =>
    this.buildLabeledChoices(this.contactMethodValues, this.contactMethods())
  );
  readonly qualificationChoices = computed(() =>
    this.buildLabeledChoices(this.qualificationValues, this.qualifications())
  );
  readonly duplicateChoices = computed(() =>
    this.buildLabeledChoices(this.duplicateValues, this.duplicates())
  );
  readonly contactMethodMenuOpen = computed(() => this.openFilterMenu() === 'contactMethod');
  readonly qualificationMenuOpen = computed(() => this.openFilterMenu() === 'qualification');
  readonly duplicateMenuOpen = computed(() => this.openFilterMenu() === 'duplicate');
  readonly contactMethodCount = computed(() => this.contactMethods().length);
  readonly qualificationCount = computed(() => this.qualifications().length);
  readonly duplicateCount = computed(() => this.duplicates().length);
  readonly hasContactMethodSelection = computed(() => this.contactMethodCount() > 0);
  readonly hasQualificationSelection = computed(() => this.qualificationCount() > 0);
  readonly hasDuplicateSelection = computed(() => this.duplicateCount() > 0);
  readonly activeFilterChips = computed(() => this.buildActiveChips());
  readonly hasActiveFilters = computed(() => this.activeFilterChips().length > 0);
  readonly hasMapRunFilter = computed(() => !!this.researchRunId().trim());
  readonly mapRunFilterLabel = computed(() => {
    const runId = this.researchRunId().trim();
    if (!runId) {
      return '';
    }
    return runId.slice(0, 8);
  });

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.openFilterMenu() === null) {
      return;
    }
    const target = event.target;
    if (!(target instanceof Element)) {
      this.openFilterMenu.set(null);
      return;
    }
    if (target.closest('.multi-filter')) {
      return;
    }
    this.openFilterMenu.set(null);
  }

  ngOnInit(): void {
    this.loadProjects();
    this.route.queryParamMap.subscribe((params) => {
      const projectId = params.get('projectId') ?? '';
      const researchRunId = params.get('researchRunId') ?? '';
      const unchanged =
        this.queryParamsBound &&
        projectId === this.projectId() &&
        researchRunId === this.researchRunId();
      this.queryParamsBound = true;
      this.projectId.set(projectId);
      this.researchRunId.set(researchRunId);
      if (unchanged) {
        return;
      }
      this.loadProspects();
    });
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    this.loadProspects();
  }

  onProjectChange(value: string): void {
    this.projectId.set(value);
    this.researchRunId.set('');
    this.syncListQuery(value, '');
    this.loadProspects();
  }

  clearMapRunFilter(): void {
    this.researchRunId.set('');
    this.syncListQuery(this.projectId(), '');
    this.loadProspects();
  }

  onCountryChange(value: string): void {
    this.country.set(value);
    this.loadProspects();
  }

  onIndustryChange(value: string): void {
    this.industry.set(value);
    this.loadProspects();
  }

  toggleStatus(value: string): void {
    this.statuses.update((items) => this.toggleValue(items, value));
    this.loadProspects();
  }

  toggleContactMethod(value: string, event: Event): void {
    event.stopPropagation();
    this.contactMethods.update((items) => this.toggleValue(items, value));
    this.loadProspects();
  }

  toggleQualification(value: string, event: Event): void {
    event.stopPropagation();
    this.qualifications.update((items) => this.toggleValue(items, value));
    this.loadProspects();
  }

  toggleDuplicate(value: string, event: Event): void {
    event.stopPropagation();
    this.duplicates.update((items) => this.toggleValue(items, value));
    this.loadProspects();
  }

  openContactMethodMenu(event: Event): void {
    event.stopPropagation();
    this.toggleFilterMenu('contactMethod');
  }

  openQualificationMenu(event: Event): void {
    event.stopPropagation();
    this.toggleFilterMenu('qualification');
  }

  openDuplicateMenu(event: Event): void {
    event.stopPropagation();
    this.toggleFilterMenu('duplicate');
  }

  stopMenuPropagation(event: Event): void {
    event.stopPropagation();
  }

  clearAllFilters(): void {
    this.search.set('');
    this.country.set('');
    this.industry.set('');
    this.statuses.set([]);
    this.contactMethods.set([]);
    this.qualifications.set([]);
    this.duplicates.set([]);
    this.researchRunId.set('');
    this.openFilterMenu.set(null);
    this.syncListQuery(this.projectId(), '');
    this.loadProspects();
  }

  removeActiveChip(chip: ProspectActiveFilterChip): void {
    if (chip.group === 'status') {
      this.statuses.update((items) => items.filter((item) => item !== chip.value));
    } else if (chip.group === 'contactMethod') {
      this.contactMethods.update((items) => items.filter((item) => item !== chip.value));
    } else if (chip.group === 'qualification') {
      this.qualifications.update((items) => items.filter((item) => item !== chip.value));
    } else if (chip.group === 'researchRun') {
      this.researchRunId.set('');
      this.syncListQuery(this.projectId(), '');
    } else {
      this.duplicates.update((items) => items.filter((item) => item !== chip.value));
    }
    this.loadProspects();
  }

  toggleSelect(id: string): void {
    this.selectedIds.update((ids) => {
      if (ids.includes(id)) {
        return ids.filter((item) => item !== id);
      }
      return [...ids, id];
    });
    this.refreshEnrollEligibility();
  }

  toggleSelectAll(): void {
    if (this.allSelected()) {
      this.selectedIds.set([]);
      this.refreshEnrollEligibility();
      return;
    }
    const ids: string[] = [];
    for (const row of this.prospects()) {
      ids.push(row.id);
    }
    this.selectedIds.set(ids);
    this.refreshEnrollEligibility();
  }

  clearSelection(): void {
    this.selectedIds.set([]);
    this.enrollMixedProjects.set(false);
  }

  openView(id: string): void {
    this.router.navigate(['/prospects', id]);
  }

  approve(id: string): void {
    this.saving.set(true);
    this.prospectApi.approve(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('prospects.approveSuccess'));
        this.loadProspects();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  reject(id: string): void {
    this.saving.set(true);
    this.prospectApi.reject(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('prospects.rejectSuccess'));
        this.loadProspects();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  doNotContact(id: string): void {
    this.saving.set(true);
    this.prospectApi.doNotContact(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('prospects.doNotContactSuccess'));
        this.loadProspects();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  openCampaignForSelected(): void {
    const ids = this.selectedIds();
    if (ids.length === 0) {
      this.notification.error(this.translate.instant('prospects.selectRequired'));
      return;
    }
    this.campaignProspectIds.set(ids);
    this.campaignChannel.set('email');
    this.useAiRecommendedTemplate.set(true);
    this.campaignOpen.set(true);
  }

  openCampaignForOne(id: string): void {
    this.campaignProspectIds.set([id]);
    this.campaignChannel.set('email');
    this.useAiRecommendedTemplate.set(true);
    this.campaignOpen.set(true);
  }

  closeCampaign(): void {
    this.campaignOpen.set(false);
  }

  onCampaignChannelChange(value: string): void {
    this.campaignChannel.set(value as OutreachChannelType);
  }

  onUseAiTemplateChange(checked: boolean): void {
    this.useAiRecommendedTemplate.set(checked);
  }

  confirmCampaign(): void {
    const prospectIds = this.campaignProspectIds();
    if (prospectIds.length === 0) {
      return;
    }
    const projectId = this.resolveProjectId(prospectIds);
    if (!projectId) {
      this.notification.error(this.translate.instant('prospects.projectRequired'));
      return;
    }
    const request: PrepareOutreachRequest = {
      prospectIds,
      channel: this.campaignChannel(),
      templateId: null,
      useAiRecommendedTemplate: this.useAiRecommendedTemplate(),
      emailAccountId: null,
      signatureId: null,
      allowAlreadyContacted: false
    };
    this.saving.set(true);
    this.projectApi.prepareOutreach(projectId, request).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.campaignOpen.set(false);
        this.notification.success(this.translate.instant('prospects.campaignSuccess'));
        if (result.campaignId) {
          this.router.navigate(['/history', result.campaignId]);
          return;
        }
        this.loadProspects();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  openEnrollSequence(): void {
    const ids = this.selectedIds();
    if (ids.length === 0) {
      this.notification.error(this.translate.instant('prospects.selectRequired'));
      return;
    }
    const projectId = this.resolveSelectionProjectId(ids);
    if (!projectId) {
      this.enrollMixedProjects.set(true);
      this.notification.error(this.translate.instant('prospects.enrollMixedProjects'));
      return;
    }
    this.enrollMixedProjects.set(false);
    this.selectedSequenceId.set('');
    this.enrollOpen.set(true);
    this.loadEnrollSequences(projectId);
  }

  closeEnrollSequence(): void {
    this.enrollOpen.set(false);
  }

  onEnrollSequenceChange(value: string): void {
    this.selectedSequenceId.set(value);
  }

  confirmEnrollSequence(): void {
    const sequenceId = this.selectedSequenceId().trim();
    const prospectIds = this.selectedIds();
    if (!sequenceId || prospectIds.length === 0) {
      return;
    }
    this.saving.set(true);
    this.sequenceApi.enroll(sequenceId, prospectIds).subscribe({
      next: () => {
        this.saving.set(false);
        this.enrollOpen.set(false);
        this.notification.success(this.translate.instant('prospects.enrollSuccess'));
        this.selectedIds.set([]);
        this.refreshEnrollEligibility();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private toggleFilterMenu(menu: OpenFilterMenu): void {
    this.openFilterMenu.update((current) => (current === menu ? null : menu));
  }

  private toggleValue(items: string[], value: string): string[] {
    if (items.includes(value)) {
      return items.filter((item) => item !== value);
    }
    return [...items, value];
  }

  private buildChoices(values: string[], selected: string[], prefix: string): ProspectFilterChoice[] {
    const choices: ProspectFilterChoice[] = [];
    for (const value of values) {
      choices.push({
        value,
        labelKey: `${prefix}.${value}`,
        selected: selected.includes(value)
      });
    }
    return choices;
  }

  private buildLabeledChoices(
    values: { value: string; labelKey: string }[],
    selected: string[]
  ): ProspectFilterChoice[] {
    const choices: ProspectFilterChoice[] = [];
    for (const item of values) {
      choices.push({
        value: item.value,
        labelKey: item.labelKey,
        selected: selected.includes(item.value)
      });
    }
    return choices;
  }

  private buildActiveChips(): ProspectActiveFilterChip[] {
    const chips: ProspectActiveFilterChip[] = [];
    const researchRunId = this.researchRunId().trim();
    if (researchRunId) {
      chips.push({
        group: 'researchRun',
        value: researchRunId,
        labelKey: 'prospects.mapRunFilter'
      });
    }
    for (const value of this.statuses()) {
      chips.push({ group: 'status', value, labelKey: `prospectStatus.${value}` });
    }
    for (const value of this.contactMethods()) {
      const match = this.contactMethodValues.find((item) => item.value === value);
      chips.push({
        group: 'contactMethod',
        value,
        labelKey: match?.labelKey ?? value
      });
    }
    for (const value of this.qualifications()) {
      const match = this.qualificationValues.find((item) => item.value === value);
      chips.push({
        group: 'qualification',
        value,
        labelKey: match?.labelKey ?? value
      });
    }
    for (const value of this.duplicates()) {
      chips.push({ group: 'duplicate', value, labelKey: `duplicateKind.${value}` });
    }
    return chips;
  }

  private syncListQuery(projectId: string, researchRunId: string): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        projectId: projectId || null,
        researchRunId: researchRunId || null
      },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  private resolveProjectId(prospectIds: string[]): string | null {
    const filterProject = this.projectId().trim();
    if (filterProject) {
      return filterProject;
    }
    const firstId = prospectIds[0];
    const row = this.prospects().find((item) => item.id === firstId);
    return row?.projectId ?? null;
  }

  private resolveSelectionProjectId(prospectIds: string[]): string | null {
    const filterProject = this.projectId().trim();
    if (filterProject) {
      return filterProject;
    }
    let projectId: string | null = null;
    for (const id of prospectIds) {
      const row = this.prospects().find((item) => item.id === id);
      if (!row?.projectId) {
        return null;
      }
      if (projectId === null) {
        projectId = row.projectId;
        continue;
      }
      if (projectId !== row.projectId) {
        return null;
      }
    }
    return projectId;
  }

  private refreshEnrollEligibility(): void {
    const ids = this.selectedIds();
    if (ids.length === 0) {
      this.enrollMixedProjects.set(false);
      return;
    }
    this.enrollMixedProjects.set(this.resolveSelectionProjectId(ids) === null);
  }

  private loadEnrollSequences(projectId: string): void {
    this.sequenceApi.list(projectId).subscribe({
      next: (items) => {
        const options: SequenceOption[] = [];
        for (const item of items ?? []) {
          if (item.isActive) {
            options.push({ id: item.id, name: item.name });
          }
        }
        this.enrollSequences.set(options);
      },
      error: () => this.enrollSequences.set([])
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

  exportCsv(): void {
    const filter = this.buildFilter();
    this.saving.set(true);
    this.prospectApi.exportCsv(filter).subscribe({
      next: (blob) => {
        this.saving.set(false);
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = 'prospects.csv';
        anchor.click();
        URL.revokeObjectURL(url);
        this.notification.success(this.translate.instant('prospects.exportSuccess'));
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private buildFilter(): ProspectListFilter {
    return {
      projectId: this.projectId().trim() || undefined,
      researchRunId: this.researchRunId().trim() || undefined,
      search: this.search().trim() || undefined,
      country: this.country().trim() || undefined,
      industry: this.industry().trim() || undefined,
      contactMethods: this.contactMethods().length > 0 ? this.contactMethods() : undefined,
      statuses: this.statuses().length > 0 ? this.statuses() : undefined,
      qualifications: this.qualifications().length > 0 ? this.qualifications() : undefined,
      duplicates: this.duplicates().length > 0 ? this.duplicates() : undefined
    };
  }

  private loadProspects(): void {
    this.loading.set(true);
    const filter = this.buildFilter();
    this.prospectApi.list(filter).subscribe({
      next: (items) => {
        this.prospects.set(this.mapRows(items ?? []));
        this.selectedIds.set([]);
        this.enrollMixedProjects.set(false);
        this.loading.set(false);
      },
      error: () => {
        this.prospects.set([]);
        this.loading.set(false);
      }
    });
  }

  private mapRows(items: ProspectSummary[]): ProspectListRow[] {
    const rows: ProspectListRow[] = [];
    for (const item of items) {
      const locationParts: string[] = [];
      if (item.city) {
        locationParts.push(item.city);
      }
      if (item.country) {
        locationParts.push(item.country);
      }
      rows.push({
        ...item,
        createdLabel: formatDate(item.createdAtUtc ?? null),
        statusLabelKey: `prospectStatus.${item.status}`,
        duplicateLabelKey: `duplicateKind.${item.duplicateKind}`,
        qualificationLabel:
          item.qualificationScore != null
            ? String(item.qualificationScore)
            : this.translate.instant('prospects.noScore'),
        sourceLabel: item.sourceName || item.sourceType || '—',
        templateLabel:
          item.recommendedTemplateName || this.translate.instant('prospects.noTemplate'),
        selected: false,
        contactLabel: item.contactName || '—',
        projectLabel: item.projectName || '—',
        locationLabel: locationParts.length > 0 ? locationParts.join(', ') : '—',
        emailLabel: item.email || '—',
        whatsAppLabel: item.whatsApp || '—',
        hasEmail: !!item.email,
        hasWhatsApp: !!item.whatsApp
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

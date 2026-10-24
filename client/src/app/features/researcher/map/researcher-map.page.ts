import {
  Component,
  Injector,
  OnDestroy,
  OnInit,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  viewChild,
  ElementRef
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Subscription, filter, switchMap, takeWhile, timer } from 'rxjs';
import * as L from 'leaflet';
import {
  MapCoverageRegionDto,
  MapFieldProvenanceView,
  MapMarkerDetailView,
  MapMarkerDto,
  MapMetricTile,
  MapProjectOption,
  MapRegionRow,
  MapResearchOverviewDto,
  MapResultRow,
  MapStatusFilterOption
} from '../../../core/models/map-research.model';
import { MapResearchApiService } from '../../../core/services/map-research-api.service';
import { ProjectApiService } from '../../../core/services/project-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { MetricTileComponent } from '../../../shared/components/researcher/metric-tile.component';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-researcher-map-page',
  imports: [RouterLink, TranslatePipe, EmptyStateComponent, MetricTileComponent, StatusBadgeComponent],
  templateUrl: './researcher-map.page.html',
  styleUrl: './researcher-map.page.scss'
})
export class ResearcherMapPage implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly mapResearchApi = inject(MapResearchApiService);
  private readonly projectApi = inject(ProjectApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private readonly injector = inject(Injector);

  private readonly mapHost = viewChild<ElementRef<HTMLDivElement>>('mapHost');
  private map: L.Map | null = null;
  private markerLayer: L.LayerGroup | null = null;
  private regionLayer: L.LayerGroup | null = null;
  private leafletMarkers = new Map<string, L.Marker>();
  private pollSub: Subscription | null = null;
  private defaultIcon: L.Icon | null = null;
  private lastBoundsKey = '';

  readonly loading = signal(true);
  readonly actionLoading = signal(false);
  readonly batchExtractLoading = signal(false);
  readonly extractingProspectIds = signal<string[]>([]);
  readonly projects = signal<MapProjectOption[]>([]);
  readonly projectId = signal('');
  readonly overview = signal<MapResearchOverviewDto | null>(null);
  readonly markers = signal<MapMarkerDto[]>([]);
  readonly selectedProspectId = signal('');
  readonly statusFilter = signal('');
  readonly qualifiedOnly = signal(false);
  readonly missingContactsOnly = signal(false);
  readonly requestedProspectCount = signal(50);

  readonly hasProject = computed(() => !!this.projectId());
  readonly hasOverview = computed(() => !!this.overview());
  readonly activeRunId = computed(() => this.overview()?.activeRunId ?? '');
  readonly activeRunStatus = computed(() => this.overview()?.activeRunStatus ?? null);
  readonly isRunActive = computed(() => {
    const status = this.activeRunStatus();
    return status === 'pending' || status === 'running';
  });
  readonly canCancel = computed(() => !!this.activeRunId() && this.isRunActive());
  readonly canResume = computed(() => {
    const status = this.activeRunStatus();
    return !!this.activeRunId() && (status === 'cancelled' || status === 'partial' || status === 'failed');
  });
  readonly canStart = computed(() => !!this.projectId() && !this.isRunActive());
  readonly progressLabel = computed(() => `${this.overview()?.progressPercent ?? 0}%`);
  readonly runStatusLabelKey = computed(() => {
    const status = this.activeRunStatus();
    if (!status) {
      return 'researcher.map.noActiveRun';
    }
    return `researchStatus.${status}`;
  });
  readonly hasActiveRunStatus = computed(() => !!this.activeRunStatus());
  readonly activeRunBadgeStatus = computed(() => this.activeRunStatus() || 'cancelled');
  readonly unsupportedCategoriesLabel = computed(() => {
    const items = this.overview()?.unsupportedCategories ?? [];
    if (items.length === 0) {
      return '—';
    }
    return items.join(', ');
  });
  readonly hasUnsupportedCategories = computed(
    () => (this.overview()?.unsupportedCategories?.length ?? 0) > 0
  );

  readonly statusFilterOptions = computed((): MapStatusFilterOption[] => {
    const selected = this.statusFilter();
    const values = [
      '',
      'discovered',
      'researching',
      'qualified',
      'needsReview',
      'rejected',
      'readyForOutreach',
      'contacted',
      'doNotContact'
    ];
    return values.map((value) => ({
      value,
      labelKey: value ? `prospectStatus.${value}` : 'researcher.map.filterAllStatuses',
      selected: selected === value
    }));
  });

  readonly filteredMarkers = computed(() => {
    const status = this.statusFilter();
    const qualifiedOnly = this.qualifiedOnly();
    const items = this.markers();
    const result: MapMarkerDto[] = [];
    for (const marker of items) {
      if (status && marker.status !== status) {
        continue;
      }
      if (qualifiedOnly && !marker.isQualified) {
        continue;
      }
      result.push(marker);
    }
    return result;
  });

  readonly filteredMarkersCountLabel = computed(() => String(this.filteredMarkers().length));

  readonly resultRows = computed((): MapResultRow[] => {
    const extracting = new Set(this.extractingProspectIds());
    const missingOnly = this.missingContactsOnly();
    const rows: MapResultRow[] = [];
    for (const marker of this.filteredMarkers()) {
      const row = this.mapResultRow(marker, extracting.has(marker.prospectId));
      if (missingOnly && !row.hasMissingContacts) {
        continue;
      }
      rows.push(row);
    }
    return rows;
  });

  readonly hasResultRows = computed(() => this.resultRows().length > 0);
  readonly missingContactsCountLabel = computed(() => {
    let count = 0;
    for (const marker of this.filteredMarkers()) {
      if (this.hasMissingContacts(marker)) {
        count += 1;
      }
    }
    return String(count);
  });
  readonly canExtractMissing = computed(
    () => !!this.projectId() && Number(this.missingContactsCountLabel()) > 0 && !this.batchExtractLoading()
  );
  readonly prospectsListQuery = computed(() => {
    const projectId = this.projectId();
    const researchRunId = this.activeRunId();
    if (!projectId) {
      return {};
    }
    if (!researchRunId) {
      return { projectId };
    }
    return { projectId, researchRunId };
  });
  readonly canOpenMapProspects = computed(
    () => !!this.projectId() && !!this.activeRunId() && this.markers().length > 0
  );
  readonly showResultsSection = computed(() => this.markers().length > 0);
  readonly selectedHasMissingContacts = computed(() => {
    const marker = this.selectedMarker();
    if (!marker) {
      return false;
    }
    return (
      marker.emailLabel === '—' ||
      marker.phoneLabel === '—' ||
      marker.websiteLabel === '—'
    );
  });
  readonly selectedIsExtracting = computed(() => {
    const id = this.selectedProspectId();
    return !!id && this.isExtracting(id);
  });

  readonly metricTiles = computed((): MapMetricTile[] => {
    const data = this.overview();
    if (!data) {
      return [];
    }
    return [
      {
        labelKey: 'researcher.map.markersCount',
        value: String(data.markersCount),
        tone: 'default'
      },
      {
        labelKey: 'researcher.map.placesDiscovered',
        value: String(data.placesDiscovered),
        tone: 'active'
      },
      {
        labelKey: 'researcher.map.prospectsSaved',
        value: String(data.prospectsSaved),
        tone: 'success'
      },
      {
        labelKey: 'researcher.map.regionsCompleted',
        value: `${data.regionsCompleted}/${data.regionsTotal}`,
        tone: 'default'
      },
      {
        labelKey: 'researcher.map.regionsPending',
        value: String(data.regionsPending),
        tone: 'warning'
      },
      {
        labelKey: 'researcher.map.regionsFailed',
        value: String(data.regionsFailed),
        tone: data.regionsFailed > 0 ? 'danger' : 'default'
      }
    ];
  });

  readonly regionRows = computed((): MapRegionRow[] => {
    const regions = this.overview()?.regions ?? [];
    const rows: MapRegionRow[] = [];
    for (const region of regions) {
      rows.push(this.mapRegionRow(region));
    }
    return rows;
  });

  readonly hasRegions = computed(() => this.regionRows().length > 0);

  readonly selectedMarker = computed((): MapMarkerDetailView | null => {
    const id = this.selectedProspectId();
    if (!id) {
      return null;
    }
    const markers = this.markers();
    for (const marker of markers) {
      if (marker.prospectId === id) {
        return this.mapMarkerDetail(marker);
      }
    }
    return null;
  });

  constructor() {
    afterNextRender(() => {
      this.scheduleMapSync();
    });

    effect(() => {
      this.loading();
      this.hasProject();
      this.filteredMarkers();
      this.overview()?.regions;
      this.selectedProspectId();
      this.scheduleMapSync();
    });
  }

  ngOnInit(): void {
    const projectId = this.route.snapshot.queryParamMap.get('projectId') ?? '';
    this.projectId.set(projectId);
    this.loadProjects(projectId);
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.destroyMap();
  }

  onProjectChange(value: string): void {
    this.projectId.set(value);
    this.selectedProspectId.set('');
    this.lastBoundsKey = '';
    this.syncProjectQuery(value);
    if (!value) {
      this.overview.set(null);
      this.markers.set([]);
      this.stopPolling();
      this.syncMapLayers();
      return;
    }
    this.loadOverview(value);
  }

  onStatusFilterChange(value: string): void {
    this.statusFilter.set(value);
  }

  onQualifiedOnlyChange(checked: boolean): void {
    this.qualifiedOnly.set(checked);
  }

  onMissingContactsOnlyChange(checked: boolean): void {
    this.missingContactsOnly.set(checked);
  }

  onRequestedCountChange(value: string): void {
    const parsed = Number(value);
    if (!Number.isFinite(parsed) || parsed < 1) {
      this.requestedProspectCount.set(50);
      return;
    }
    this.requestedProspectCount.set(Math.floor(parsed));
  }

  closePanel(): void {
    this.selectedProspectId.set('');
  }

  selectResultRow(prospectId: string): void {
    this.selectedProspectId.set(prospectId);
  }

  extractContactsWithOpenAi(prospectId: string): void {
    const projectId = this.projectId();
    if (!projectId || !prospectId || this.isExtracting(prospectId)) {
      return;
    }

    this.setExtracting(prospectId, true);
    this.mapResearchApi.extractContactsWithOpenAi(projectId, prospectId).subscribe({
      next: (result) => {
        this.setExtracting(prospectId, false);
        this.notification.success(
          result.message || this.translate.instant('researcher.map.extractSuccess')
        );
        this.loadOverview(projectId);
      },
      error: (err: HttpErrorResponse) => {
        this.setExtracting(prospectId, false);
        this.notifyError(err);
      }
    });
  }

  extractMissingWithOpenAi(): void {
    const projectId = this.projectId();
    if (!projectId || this.batchExtractLoading()) {
      return;
    }

    this.batchExtractLoading.set(true);
    this.mapResearchApi
      .extractMissingContactsWithOpenAi(projectId, this.activeRunId() || undefined)
      .subscribe({
        next: (result) => {
          this.batchExtractLoading.set(false);
          this.notification.success(
            this.translate.instant('researcher.map.extractBatchSuccess', {
              improved: result.improved,
              processed: result.processed
            })
          );
          this.loadOverview(projectId);
        },
        error: (err: HttpErrorResponse) => {
          this.batchExtractLoading.set(false);
          this.notifyError(err);
        }
      });
  }

  startRun(): void {
    const projectId = this.projectId();
    if (!projectId || this.actionLoading() || !this.canStart()) {
      return;
    }
    this.actionLoading.set(true);
    this.mapResearchApi
      .start(projectId, { requestedProspectCount: this.requestedProspectCount() })
      .subscribe({
        next: () => {
          this.actionLoading.set(false);
          this.notification.success(this.translate.instant('researcher.map.startSuccess'));
          this.loadOverview(projectId);
        },
        error: (err: HttpErrorResponse) => {
          this.actionLoading.set(false);
          this.notifyError(err);
        }
      });
  }

  cancelRun(): void {
    const projectId = this.projectId();
    const runId = this.activeRunId();
    if (!projectId || !runId || this.actionLoading() || !this.canCancel()) {
      return;
    }
    this.actionLoading.set(true);
    this.mapResearchApi.cancel(projectId, runId).subscribe({
      next: () => {
        this.actionLoading.set(false);
        this.notification.success(this.translate.instant('researcher.map.cancelSuccess'));
        this.loadOverview(projectId);
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notifyError(err);
      }
    });
  }

  resumeRun(): void {
    const projectId = this.projectId();
    const runId = this.activeRunId();
    if (!projectId || !runId || this.actionLoading() || !this.canResume()) {
      return;
    }
    this.actionLoading.set(true);
    this.mapResearchApi.resume(projectId, runId).subscribe({
      next: () => {
        this.actionLoading.set(false);
        this.notification.success(this.translate.instant('researcher.map.resumeSuccess'));
        this.loadOverview(projectId);
      },
      error: (err: HttpErrorResponse) => {
        this.actionLoading.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadProjects(preferredId: string): void {
    this.projectApi.list().subscribe({
      next: (items) => {
        const options: MapProjectOption[] = [];
        for (const item of items ?? []) {
          options.push({ id: item.id, name: item.name });
        }
        this.projects.set(options);
        const selected = preferredId || options[0]?.id || '';
        this.projectId.set(selected);
        this.syncProjectQuery(selected);
        if (selected) {
          this.loadOverview(selected);
          return;
        }
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.projects.set([]);
        this.loading.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadOverview(projectId: string): void {
    this.destroyMap();
    this.loading.set(true);
    this.mapResearchApi.getOverview(projectId).subscribe({
      next: (overview) => {
        this.loading.set(false);
        this.applyOverview(overview);
        this.scheduleMapSync();
        this.startPollingIfNeeded(projectId);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.overview.set(null);
        this.markers.set([]);
        this.stopPolling();
        this.destroyMap();
        this.notifyError(err);
      }
    });
  }

  private scheduleMapSync(): void {
    if (this.loading() || !this.hasProject()) {
      return;
    }

    afterNextRender(
      () => {
        this.syncMapLayers();
      },
      { injector: this.injector }
    );
  }

  private applyOverview(overview: MapResearchOverviewDto): void {
    this.overview.set(overview);
    this.markers.set(overview.markers ?? []);
    const selectedId = this.selectedProspectId();
    if (!selectedId) {
      return;
    }
    let found = false;
    for (const marker of overview.markers ?? []) {
      if (marker.prospectId === selectedId) {
        found = true;
        break;
      }
    }
    if (!found) {
      this.selectedProspectId.set('');
    }
  }

  private startPollingIfNeeded(projectId: string): void {
    this.stopPolling();
    if (!this.isRunActive()) {
      return;
    }
    this.pollSub = timer(4000, 4000)
      .pipe(
        filter(() => this.projectId() === projectId && this.isRunActive()),
        takeWhile(() => this.projectId() === projectId && this.isRunActive(), true),
        switchMap(() => this.mapResearchApi.getOverview(projectId))
      )
      .subscribe({
        next: (overview) => this.applyOverview(overview),
        error: () => this.stopPolling()
      });
  }

  private stopPolling(): void {
    this.pollSub?.unsubscribe();
    this.pollSub = null;
  }

  private syncProjectQuery(projectId: string): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { projectId: projectId || null },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  private ensureMap(): void {
    const host = this.mapHost()?.nativeElement;
    if (!host) {
      return;
    }

    if (this.map && this.map.getContainer() !== host) {
      this.destroyMap();
    }

    if (this.map) {
      return;
    }

    this.defaultIcon = L.icon({
      iconUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png',
      iconRetinaUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png',
      shadowUrl: 'https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png',
      iconSize: [25, 41],
      iconAnchor: [12, 41],
      popupAnchor: [1, -34],
      shadowSize: [41, 41]
    });
    this.map = L.map(host, {
      center: [24.4539, 54.3773],
      zoom: 6,
      scrollWheelZoom: true
    });
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(this.map);
    this.markerLayer = L.layerGroup().addTo(this.map);
    this.regionLayer = L.layerGroup().addTo(this.map);
    this.map.invalidateSize();
  }

  private syncMapLayers(): void {
    this.ensureMap();
    if (!this.map || !this.markerLayer || !this.regionLayer) {
      return;
    }
    this.syncRegionRectangles(this.overview()?.regions ?? []);
    this.syncMarkerPins(this.filteredMarkers(), this.selectedProspectId());
    this.map.invalidateSize();
  }

  private syncMarkerPins(markers: MapMarkerDto[], selectedId: string): void {
    if (!this.markerLayer || !this.defaultIcon) {
      return;
    }
    const nextIds = new Set<string>();
    for (const marker of markers) {
      nextIds.add(marker.prospectId);
      const existing = this.leafletMarkers.get(marker.prospectId);
      if (existing) {
        existing.setLatLng([marker.latitude, marker.longitude]);
        continue;
      }
      const pin = L.marker([marker.latitude, marker.longitude], { icon: this.defaultIcon });
      pin.on('click', () => this.selectMarker(marker.prospectId));
      pin.addTo(this.markerLayer);
      this.leafletMarkers.set(marker.prospectId, pin);
    }
    for (const [id, pin] of this.leafletMarkers) {
      if (nextIds.has(id)) {
        continue;
      }
      this.markerLayer.removeLayer(pin);
      this.leafletMarkers.delete(id);
    }
    this.fitMapToContent(markers, this.overview()?.regions ?? [], selectedId);
  }

  private syncRegionRectangles(regions: MapCoverageRegionDto[]): void {
    if (!this.regionLayer) {
      return;
    }
    this.regionLayer.clearLayers();
    for (const region of regions) {
      const bounds: L.LatLngBoundsExpression = [
        [region.south, region.west],
        [region.north, region.east]
      ];
      const color = this.regionColor(region.status);
      L.rectangle(bounds, {
        color,
        weight: 1,
        fillOpacity: 0.08,
        opacity: 0.65
      }).addTo(this.regionLayer);
    }
  }

  private fitMapToContent(
    markers: MapMarkerDto[],
    regions: MapCoverageRegionDto[],
    selectedId: string
  ): void {
    if (!this.map) {
      return;
    }
    if (selectedId) {
      const selected = this.leafletMarkers.get(selectedId);
      if (selected) {
        this.map.panTo(selected.getLatLng());
      }
      return;
    }
    const markerKeyParts: string[] = [];
    for (const marker of markers) {
      markerKeyParts.push(marker.prospectId);
    }
    const regionKeyParts: string[] = [];
    for (const region of regions) {
      regionKeyParts.push(region.id);
    }
    const boundsKey = `${markerKeyParts.join(',')}|${regionKeyParts.join(',')}`;
    if (boundsKey === this.lastBoundsKey) {
      return;
    }
    this.lastBoundsKey = boundsKey;
    const points: L.LatLngExpression[] = [];
    for (const marker of markers) {
      points.push([marker.latitude, marker.longitude]);
    }
    for (const region of regions) {
      points.push([region.south, region.west]);
      points.push([region.north, region.east]);
    }
    if (points.length === 0) {
      return;
    }
    if (points.length === 1) {
      this.map.setView(points[0], 12);
      return;
    }
    this.map.fitBounds(L.latLngBounds(points), { padding: [36, 36], maxZoom: 14 });
  }

  private selectMarker(prospectId: string): void {
    this.selectedProspectId.set(prospectId);
  }

  private destroyMap(): void {
    this.leafletMarkers.clear();
    this.markerLayer = null;
    this.regionLayer = null;
    if (this.map) {
      this.map.remove();
      this.map = null;
    }
  }

  private mapResultRow(marker: MapMarkerDto, extracting: boolean): MapResultRow {
    const missing = this.buildMissingContactLabels(marker);
    return {
      prospectId: marker.prospectId,
      companyName: marker.companyName || '—',
      locationLabel: this.buildLocationLabel(marker),
      emailLabel: marker.email || '—',
      phoneLabel: marker.phone || '—',
      websiteLabel: marker.website || '—',
      statusLabelKey: `prospectStatus.${marker.status}`,
      missingLabel: missing.length ? missing.join(', ') : '—',
      hasMissingContacts: missing.length > 0,
      prospectDetailUrl: `/prospects/${marker.prospectId}`,
      extracting
    };
  }

  private buildMissingContactLabels(marker: MapMarkerDto): string[] {
    const missing: string[] = [];
    if (!marker.email) {
      missing.push(this.translate.instant('researcher.map.detailEmail'));
    }
    if (!marker.phone) {
      missing.push(this.translate.instant('researcher.map.detailPhone'));
    }
    if (!marker.website) {
      missing.push(this.translate.instant('researcher.map.detailWebsite'));
    }
    return missing;
  }

  private hasMissingContacts(marker: MapMarkerDto): boolean {
    return !marker.email || !marker.phone || !marker.website;
  }

  private isExtracting(prospectId: string): boolean {
    return this.extractingProspectIds().includes(prospectId);
  }

  private setExtracting(prospectId: string, extracting: boolean): void {
    const current = this.extractingProspectIds();
    if (extracting) {
      if (current.includes(prospectId)) {
        return;
      }
      this.extractingProspectIds.set([...current, prospectId]);
      return;
    }

    const next: string[] = [];
    for (const id of current) {
      if (id !== prospectId) {
        next.push(id);
      }
    }
    this.extractingProspectIds.set(next);
  }

  private mapMarkerDetail(marker: MapMarkerDto): MapMarkerDetailView {
    const missingFields = marker.missingFields ?? [];
    const additionalPhones = marker.additionalPhones ?? [];
    const additionalEmails = marker.additionalEmails ?? [];
    const emailRows = this.mergeContactValues(marker.email, additionalEmails);
    const phoneRows = this.mergeContactValues(marker.phone, additionalPhones);
    const provenanceRows = this.buildProvenanceRows(marker);
    return {
      prospectId: marker.prospectId,
      companyName: marker.companyName || '—',
      statusLabelKey: `prospectStatus.${marker.status}`,
      qualifiedLabelKey: marker.isQualified
        ? 'researcher.map.qualifiedYes'
        : 'researcher.map.qualifiedNo',
      scoreLabel:
        marker.qualificationScore == null ? '—' : String(marker.qualificationScore),
      locationLabel: this.buildLocationLabel(marker),
      industryLabel: marker.industry || '—',
      websiteLabel: marker.website || '—',
      phoneLabel: marker.phone || '—',
      emailLabel: marker.email || '—',
      faxLabel: marker.fax || '—',
      streetAddressLabel: marker.streetAddress || '—',
      openingHoursLabel: marker.openingHours || '—',
      sourceNameLabel: marker.sourceName || '—',
      sourceUrl: marker.sourceUrl || '',
      hasSourceUrl: !!marker.sourceUrl,
      categoryTagsLabel: marker.categoryTags || '—',
      missingFields,
      hasMissingFields: missingFields.length > 0,
      additionalPhonesLabel: additionalPhones.length ? additionalPhones.join(', ') : '—',
      additionalEmailsLabel: additionalEmails.length ? additionalEmails.join(', ') : '—',
      hasAdditionalContacts: additionalPhones.length > 0 || additionalEmails.length > 0,
      emailRows,
      phoneRows,
      hasFetchedEmails: emailRows.length > 0,
      hasFetchedPhones: phoneRows.length > 0,
      hasFetchedContacts: emailRows.length > 0 || phoneRows.length > 0,
      prospectDetailUrl: `/prospects/${marker.prospectId}`,
      provenanceRows,
      hasProvenance: provenanceRows.length > 0
    };
  }

  private mergeContactValues(
    primary: string | null | undefined,
    additional: string[]
  ): string[] {
    const rows: string[] = [];
    const seen = new Set<string>();
    const primaryValue = (primary ?? '').trim();
    if (primaryValue) {
      seen.add(primaryValue.toLowerCase());
      rows.push(primaryValue);
    }

    for (const raw of additional) {
      const value = (raw ?? '').trim();
      if (!value) {
        continue;
      }
      const key = value.toLowerCase();
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      rows.push(value);
    }

    return rows;
  }

  private buildProvenanceRows(marker: MapMarkerDto): MapFieldProvenanceView[] {
    const rows: MapFieldProvenanceView[] = [];
    for (const item of marker.fieldProvenance ?? []) {
      rows.push({
        fieldLabelKey: `researcher.map.field.${item.fieldName}`,
        valueLabel: item.value || '—',
        methodLabelKey: `researcher.map.extractionMethod.${item.extractionMethod}`,
        statusLabelKey: `researcher.map.verificationStatus.${item.verificationStatus}`,
        sourceUrl: item.sourceUrl || '',
        hasSourceUrl: !!item.sourceUrl,
        excerptLabel: item.supportingExcerpt || '',
        hasExcerpt: !!item.supportingExcerpt
      });
    }
    return rows;
  }

  private mapRegionRow(region: MapCoverageRegionDto): MapRegionRow {
    return {
      id: region.id,
      regionLabel: region.regionLabel || '—',
      countryCodeLabel: region.countryCode || '—',
      keywordFamilyKey: region.keywordFamilyKey || '—',
      sourceName: region.sourceName || '—',
      statusLabelKey: `researcher.map.regionStatus.${region.status}`,
      recordsFound: region.recordsFound,
      uniqueRecordsFound: region.uniqueRecordsFound,
      lastErrorLabel: region.lastError || '—'
    };
  }

  private buildLocationLabel(marker: MapMarkerDto): string {
    const parts: string[] = [];
    if (marker.streetAddress) {
      parts.push(marker.streetAddress);
    }
    if (marker.city) {
      parts.push(marker.city);
    }
    if (marker.country) {
      parts.push(marker.country);
    }
    if (parts.length === 0) {
      return '—';
    }
    return parts.join(', ');
  }

  private regionColor(status: string): string {
    if (status === 'completed') {
      return '#34d399';
    }
    if (status === 'failed') {
      return '#f87171';
    }
    if (status === 'inProgress') {
      return '#38bdf8';
    }
    if (status === 'pending') {
      return '#fbbf24';
    }
    return '#94a3b8';
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

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  MapMarkerDto,
  MapOpenAiContactBatchResult,
  MapOpenAiContactExtractResult,
  MapResearchOverviewDto,
  MapResearchPlanPreviewDto,
  StartMapResearchRequest
} from '../models/map-research.model';
import { ResearchRunDetail } from '../models/prospecting.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class MapResearchApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  getOverview(projectId: string, researchRunId?: string): Observable<MapResearchOverviewDto> {
    return this.http.get<MapResearchOverviewDto>(this.buildUrl(projectId), {
      params: this.buildRunParams(researchRunId)
    });
  }

  listMarkers(projectId: string, researchRunId?: string): Observable<MapMarkerDto[]> {
    return this.http.get<MapMarkerDto[]>(`${this.buildUrl(projectId)}/markers`, {
      params: this.buildRunParams(researchRunId)
    });
  }

  previewPlan(projectId: string): Observable<MapResearchPlanPreviewDto> {
    return this.http.get<MapResearchPlanPreviewDto>(`${this.buildUrl(projectId)}/plan-preview`);
  }

  start(projectId: string, request: StartMapResearchRequest): Observable<ResearchRunDetail> {
    return this.http.post<ResearchRunDetail>(`${this.buildUrl(projectId)}/start`, request);
  }

  cancel(projectId: string, researchRunId: string): Observable<void> {
    return this.http.post<void>(
      `${this.buildUrl(projectId)}/runs/${researchRunId}/cancel`,
      {}
    );
  }

  resume(projectId: string, researchRunId: string): Observable<void> {
    return this.http.post<void>(
      `${this.buildUrl(projectId)}/runs/${researchRunId}/resume`,
      {}
    );
  }

  extractContactsWithOpenAi(
    projectId: string,
    prospectId: string
  ): Observable<MapOpenAiContactExtractResult> {
    return this.http.post<MapOpenAiContactExtractResult>(
      `${this.buildUrl(projectId)}/prospects/${prospectId}/extract-contacts-openai`,
      {}
    );
  }

  extractMissingContactsWithOpenAi(
    projectId: string,
    researchRunId?: string
  ): Observable<MapOpenAiContactBatchResult> {
    return this.http.post<MapOpenAiContactBatchResult>(
      `${this.buildUrl(projectId)}/extract-contacts-openai`,
      {},
      { params: this.buildRunParams(researchRunId) }
    );
  }

  private buildUrl(projectId: string): string {
    return `${this.api.baseUrl}/projects/${projectId}/map-research`;
  }

  private buildRunParams(researchRunId?: string): HttpParams {
    let params = new HttpParams();
    if (researchRunId) {
      params = params.set('researchRunId', researchRunId);
    }
    return params;
  }
}

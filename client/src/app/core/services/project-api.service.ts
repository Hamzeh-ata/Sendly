import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  OutreachProjectDetail,
  OutreachProjectSummary,
  PrepareOutreachRequest,
  PrepareOutreachResponse,
  ResearchRunDetail,
  StartResearchRequest,
  UpsertOutreachProject
} from '../models/prospecting.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class ProjectApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(search?: string, templateId?: string): Observable<OutreachProjectSummary[]> {
    let params = new HttpParams();
    if (search) {
      params = params.set('search', search);
    }
    if (templateId) {
      params = params.set('templateId', templateId);
    }
    return this.http.get<OutreachProjectSummary[]>(`${this.api.baseUrl}/projects`, { params });
  }

  get(id: string): Observable<OutreachProjectDetail> {
    return this.http.get<OutreachProjectDetail>(`${this.api.baseUrl}/projects/${id}`);
  }

  create(request: UpsertOutreachProject): Observable<OutreachProjectDetail> {
    return this.http.post<OutreachProjectDetail>(`${this.api.baseUrl}/projects`, request);
  }

  update(id: string, request: UpsertOutreachProject): Observable<OutreachProjectDetail> {
    return this.http.put<OutreachProjectDetail>(`${this.api.baseUrl}/projects/${id}`, request);
  }

  deactivate(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/projects/${id}/deactivate`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/projects/${id}`);
  }

  startResearch(id: string, request: StartResearchRequest): Observable<ResearchRunDetail> {
    return this.http.post<ResearchRunDetail>(`${this.api.baseUrl}/projects/${id}/research`, request);
  }

  prepareOutreach(id: string, request: PrepareOutreachRequest): Observable<PrepareOutreachResponse> {
    return this.http.post<PrepareOutreachResponse>(
      `${this.api.baseUrl}/projects/${id}/outreach/prepare`,
      request
    );
  }
}

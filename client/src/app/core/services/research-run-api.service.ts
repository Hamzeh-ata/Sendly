import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ResearchRunDetail, ResearchRunSummary } from '../models/prospecting.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class ResearchRunApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(projectId?: string, templateId?: string): Observable<ResearchRunSummary[]> {
    let params = new HttpParams();
    if (projectId) {
      params = params.set('projectId', projectId);
    }
    if (templateId) {
      params = params.set('templateId', templateId);
    }
    return this.http.get<ResearchRunSummary[]>(`${this.api.baseUrl}/research-runs`, { params });
  }

  get(id: string): Observable<ResearchRunDetail> {
    return this.http.get<ResearchRunDetail>(`${this.api.baseUrl}/research-runs/${id}`);
  }

  cancel(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/research-runs/${id}/cancel`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/research-runs/${id}`);
  }
}

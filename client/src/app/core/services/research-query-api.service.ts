import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ResearchQueryDetail,
  ResearchQuerySummary,
  UpsertResearchQuery
} from '../models/research-query.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class ResearchQueryApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(projectId: string): Observable<ResearchQuerySummary[]> {
    return this.http.get<ResearchQuerySummary[]>(
      `${this.api.baseUrl}/projects/${projectId}/research-queries`
    );
  }

  get(projectId: string, id: string): Observable<ResearchQueryDetail> {
    return this.http.get<ResearchQueryDetail>(
      `${this.api.baseUrl}/projects/${projectId}/research-queries/${id}`
    );
  }

  create(projectId: string, request: UpsertResearchQuery): Observable<ResearchQueryDetail> {
    return this.http.post<ResearchQueryDetail>(
      `${this.api.baseUrl}/projects/${projectId}/research-queries`,
      request
    );
  }

  update(
    projectId: string,
    id: string,
    request: UpsertResearchQuery
  ): Observable<ResearchQueryDetail> {
    return this.http.put<ResearchQueryDetail>(
      `${this.api.baseUrl}/projects/${projectId}/research-queries/${id}`,
      request
    );
  }

  duplicate(
    projectId: string,
    id: string,
    name: string
  ): Observable<ResearchQueryDetail> {
    return this.http.post<ResearchQueryDetail>(
      `${this.api.baseUrl}/projects/${projectId}/research-queries/${id}/duplicate`,
      { name }
    );
  }

  archive(projectId: string, id: string): Observable<void> {
    return this.http.post<void>(
      `${this.api.baseUrl}/projects/${projectId}/research-queries/${id}/archive`,
      {}
    );
  }

  delete(projectId: string, id: string): Observable<void> {
    return this.http.delete<void>(
      `${this.api.baseUrl}/projects/${projectId}/research-queries/${id}`
    );
  }
}

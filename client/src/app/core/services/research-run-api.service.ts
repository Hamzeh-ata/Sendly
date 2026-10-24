import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ResearchRunDetail, ResearchRunSummary } from '../models/prospecting.model';
import {
  CrawledPageItem,
  DiscoveredUrlItem,
  ResearchRunEventItem
} from '../models/research-query.model';
import {
  ResearchErrorGroup,
  ResearchEventListParams,
  ResearchOverview
} from '../models/researcher.model';
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

  getOverview(): Observable<ResearchOverview> {
    return this.http.get<ResearchOverview>(`${this.api.baseUrl}/research-runs/overview`);
  }

  listErrors(researchRunId?: string): Observable<ResearchErrorGroup[]> {
    let params = new HttpParams();
    if (researchRunId) {
      params = params.set('researchRunId', researchRunId);
    }
    return this.http.get<ResearchErrorGroup[]>(`${this.api.baseUrl}/research-runs/errors`, {
      params
    });
  }

  get(id: string): Observable<ResearchRunDetail> {
    return this.http.get<ResearchRunDetail>(`${this.api.baseUrl}/research-runs/${id}`);
  }

  listDiscoveredUrls(id: string): Observable<DiscoveredUrlItem[]> {
    return this.http.get<DiscoveredUrlItem[]>(
      `${this.api.baseUrl}/research-runs/${id}/discovered-urls`
    );
  }

  listCrawledPages(id: string): Observable<CrawledPageItem[]> {
    return this.http.get<CrawledPageItem[]>(
      `${this.api.baseUrl}/research-runs/${id}/crawled-pages`
    );
  }

  getCrawledPage(runId: string, pageId: string): Observable<CrawledPageItem> {
    return this.http.get<CrawledPageItem>(
      `${this.api.baseUrl}/research-runs/${runId}/crawled-pages/${pageId}`
    );
  }

  listEvents(id: string, filters?: ResearchEventListParams): Observable<ResearchRunEventItem[]> {
    let params = new HttpParams();
    if (filters?.severity) {
      params = params.set('severity', filters.severity);
    }
    if (filters?.stage) {
      params = params.set('stage', filters.stage);
    }
    if (filters?.eventType) {
      params = params.set('eventType', filters.eventType);
    }
    if (filters?.search) {
      params = params.set('search', filters.search);
    }
    if (filters?.afterId) {
      params = params.set('afterId', filters.afterId);
    }
    if (filters?.take != null) {
      params = params.set('take', String(filters.take));
    }
    return this.http.get<ResearchRunEventItem[]>(
      `${this.api.baseUrl}/research-runs/${id}/events`,
      { params }
    );
  }

  cancel(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/research-runs/${id}/cancel`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/research-runs/${id}`);
  }
}

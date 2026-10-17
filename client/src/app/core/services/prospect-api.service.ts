import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateProspectRequest,
  ProspectDetail,
  ProspectListFilter,
  ProspectSummary,
  RecommendTemplateResponse
} from '../models/prospecting.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class ProspectApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(filter: ProspectListFilter = {}): Observable<ProspectSummary[]> {
    let params = new HttpParams();
    params = this.appendParam(params, 'projectId', filter.projectId);
    params = this.appendParam(params, 'search', filter.search);
    params = this.appendParam(params, 'country', filter.country);
    params = this.appendParam(params, 'industry', filter.industry);
    params = this.appendArray(params, 'contactMethod', filter.contactMethods);
    params = this.appendArray(params, 'status', filter.statuses);
    params = this.appendArray(params, 'qualification', filter.qualifications);
    params = this.appendArray(params, 'duplicate', filter.duplicates);
    return this.http.get<ProspectSummary[]>(`${this.api.baseUrl}/prospects`, { params });
  }

  get(id: string): Observable<ProspectDetail> {
    return this.http.get<ProspectDetail>(`${this.api.baseUrl}/prospects/${id}`);
  }

  create(request: CreateProspectRequest): Observable<ProspectDetail> {
    return this.http.post<ProspectDetail>(`${this.api.baseUrl}/prospects`, request);
  }

  approve(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/prospects/${id}/approve`, {});
  }

  reject(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/prospects/${id}/reject`, {});
  }

  doNotContact(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/prospects/${id}/do-not-contact`, {});
  }

  recommendTemplate(id: string): Observable<RecommendTemplateResponse> {
    return this.http.post<RecommendTemplateResponse>(
      `${this.api.baseUrl}/prospects/${id}/recommend-template`,
      {}
    );
  }

  private appendParam(params: HttpParams, key: string, value?: string): HttpParams {
    if (!value) {
      return params;
    }
    return params.set(key, value);
  }

  private appendArray(params: HttpParams, key: string, values?: string[]): HttpParams {
    if (!values || values.length === 0) {
      return params;
    }
    let next = params;
    for (const value of values) {
      if (value) {
        next = next.append(key, value);
      }
    }
    return next;
  }
}

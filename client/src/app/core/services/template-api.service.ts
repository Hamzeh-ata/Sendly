import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  EmailTemplateDetail,
  EmailTemplatePreview,
  EmailTemplateSummary,
  TemplateStatusFilter,
  UpsertEmailTemplate
} from '../models/template.model';
import {
  CheckTemplateRecipientsResponse,
  TemplateRecipientHistoryItem,
  TemplateStats
} from '../models/template-recipient.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class TemplateApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(search?: string, status?: TemplateStatusFilter): Observable<EmailTemplateSummary[]> {
    let params = new HttpParams();
    if (search) {
      params = params.set('search', search);
    }
    if (status && status !== 'all') {
      params = params.set('status', status);
    }
    return this.http.get<EmailTemplateSummary[]>(`${this.api.baseUrl}/templates`, { params });
  }

  get(id: string): Observable<EmailTemplateDetail> {
    return this.http.get<EmailTemplateDetail>(`${this.api.baseUrl}/templates/${id}`);
  }

  preview(id: string): Observable<EmailTemplatePreview> {
    return this.http.get<EmailTemplatePreview>(`${this.api.baseUrl}/templates/${id}/preview`);
  }

  create(request: UpsertEmailTemplate): Observable<EmailTemplateDetail> {
    return this.http.post<EmailTemplateDetail>(`${this.api.baseUrl}/templates`, request);
  }

  update(id: string, request: UpsertEmailTemplate): Observable<EmailTemplateDetail> {
    return this.http.put<EmailTemplateDetail>(`${this.api.baseUrl}/templates/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/templates/${id}`);
  }

  duplicate(id: string): Observable<EmailTemplateDetail> {
    return this.http.post<EmailTemplateDetail>(`${this.api.baseUrl}/templates/${id}/duplicate`, {});
  }

  checkRecipients(id: string, rawRecipients: string): Observable<CheckTemplateRecipientsResponse> {
    return this.http.post<CheckTemplateRecipientsResponse>(
      `${this.api.baseUrl}/templates/${id}/check-recipients`,
      { rawRecipients }
    );
  }

  recipientHistory(id: string, search?: string): Observable<TemplateRecipientHistoryItem[]> {
    let params = new HttpParams();
    if (search) {
      params = params.set('search', search);
    }
    return this.http.get<TemplateRecipientHistoryItem[]>(
      `${this.api.baseUrl}/templates/${id}/recipient-history`,
      { params }
    );
  }

  stats(id: string): Observable<TemplateStats> {
    return this.http.get<TemplateStats>(`${this.api.baseUrl}/templates/${id}/stats`);
  }
}

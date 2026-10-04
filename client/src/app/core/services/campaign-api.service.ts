import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CampaignDetail,
  CampaignRecipient,
  CampaignSummary,
  CreateCampaignRequest,
  RecipientFilter,
  ValidateRecipientsRequest,
  ValidateRecipientsResponse
} from '../models/campaign.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class CampaignApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  validateRecipients(request: ValidateRecipientsRequest): Observable<ValidateRecipientsResponse> {
    return this.http.post<ValidateRecipientsResponse>(
      `${this.api.baseUrl}/campaigns/validate`,
      request
    );
  }

  createCampaign(request: CreateCampaignRequest): Observable<CampaignDetail> {
    return this.http.post<CampaignDetail>(`${this.api.baseUrl}/campaigns`, request);
  }

  getCampaigns(search?: string): Observable<CampaignSummary[]> {
    let params = new HttpParams();
    if (search) {
      params = params.set('search', search);
    }
    return this.http.get<CampaignSummary[]>(`${this.api.baseUrl}/campaigns`, { params });
  }

  getCampaign(id: string): Observable<CampaignDetail> {
    return this.http.get<CampaignDetail>(`${this.api.baseUrl}/campaigns/${id}`);
  }

  getRecipients(id: string, status?: RecipientFilter): Observable<CampaignRecipient[]> {
    let params = new HttpParams();
    if (status && status !== 'all') {
      params = params.set('status', status);
    }
    return this.http.get<CampaignRecipient[]>(
      `${this.api.baseUrl}/campaigns/${id}/recipients`,
      { params }
    );
  }

  cancelCampaign(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/campaigns/${id}/cancel`, null);
  }

  retryFailed(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/campaigns/${id}/retry-failed`, null);
  }

  exportCsv(id: string): Observable<Blob> {
    return this.http.get(`${this.api.baseUrl}/campaigns/${id}/export`, {
      responseType: 'blob'
    });
  }
}

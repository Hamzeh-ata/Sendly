import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  EnrollProspectsRequest,
  OutreachSequenceDetail,
  OutreachSequenceSummary,
  SequenceAnalytics,
  SequenceEnrollment,
  UpsertOutreachSequence
} from '../models/sequence.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class SequenceApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(projectId?: string): Observable<OutreachSequenceSummary[]> {
    let params = new HttpParams();
    if (projectId) {
      params = params.set('projectId', projectId);
    }
    return this.http.get<OutreachSequenceSummary[]>(`${this.api.baseUrl}/sequences`, { params });
  }

  get(id: string): Observable<OutreachSequenceDetail> {
    return this.http.get<OutreachSequenceDetail>(`${this.api.baseUrl}/sequences/${id}`);
  }

  create(projectId: string, request: UpsertOutreachSequence): Observable<OutreachSequenceDetail> {
    return this.http.post<OutreachSequenceDetail>(
      `${this.api.baseUrl}/sequences/projects/${projectId}`,
      request
    );
  }

  update(id: string, request: UpsertOutreachSequence): Observable<OutreachSequenceDetail> {
    return this.http.put<OutreachSequenceDetail>(`${this.api.baseUrl}/sequences/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/sequences/${id}`);
  }

  enroll(id: string, prospectIds: string[]): Observable<SequenceEnrollment[]> {
    const request: EnrollProspectsRequest = { prospectIds };
    return this.http.post<SequenceEnrollment[]>(
      `${this.api.baseUrl}/sequences/${id}/enroll`,
      request
    );
  }

  listEnrollments(id: string): Observable<SequenceEnrollment[]> {
    return this.http.get<SequenceEnrollment[]>(`${this.api.baseUrl}/sequences/${id}/enrollments`);
  }

  cancelEnrollment(enrollmentId: string): Observable<void> {
    return this.http.post<void>(
      `${this.api.baseUrl}/sequences/enrollments/${enrollmentId}/cancel`,
      {}
    );
  }

  getAnalytics(id: string): Observable<SequenceAnalytics> {
    return this.http.get<SequenceAnalytics>(`${this.api.baseUrl}/sequences/${id}/analytics`);
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { DiscoverySourceSummary } from '../models/researcher.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class DiscoverySourceApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(): Observable<DiscoverySourceSummary[]> {
    return this.http.get<DiscoverySourceSummary[]>(`${this.api.baseUrl}/discovery-sources`);
  }

  get(id: string): Observable<DiscoverySourceSummary> {
    return this.http.get<DiscoverySourceSummary>(`${this.api.baseUrl}/discovery-sources/${id}`);
  }
}

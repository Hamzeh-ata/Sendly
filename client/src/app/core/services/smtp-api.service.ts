import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { SmtpSettings, SmtpTestResult, UpsertSmtpSettings } from '../models/smtp.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class SmtpApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  getSettings(): Observable<SmtpSettings> {
    return this.http.get<SmtpSettings>(`${this.api.baseUrl}/smtp/settings`);
  }

  saveSettings(dto: UpsertSmtpSettings): Observable<SmtpSettings> {
    return this.http.put<SmtpSettings>(`${this.api.baseUrl}/smtp/settings`, dto);
  }

  deleteSettings(): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/smtp/settings`);
  }

  testConnection(dto: UpsertSmtpSettings | null): Observable<SmtpTestResult> {
    return this.http.post<SmtpTestResult>(`${this.api.baseUrl}/smtp/test-connection`, dto);
  }
}

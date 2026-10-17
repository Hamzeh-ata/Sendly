import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  EmailAccount,
  EmailAccountStatus,
  EmailTestResult,
  MicrosoftConnectResult,
  SmtpAccountTestRequest,
  UpsertSmtpAccount
} from '../models/email-account.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class EmailAccountApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(): Observable<EmailAccount[]> {
    return this.http.get<EmailAccount[]>(`${this.api.baseUrl}/email-accounts`);
  }

  get(id: string): Observable<EmailAccount> {
    return this.http.get<EmailAccount>(`${this.api.baseUrl}/email-accounts/${id}`);
  }

  getStatus(): Observable<EmailAccountStatus> {
    return this.http.get<EmailAccountStatus>(`${this.api.baseUrl}/email-accounts/status`);
  }

  createSmtp(dto: UpsertSmtpAccount): Observable<EmailAccount> {
    return this.http.post<EmailAccount>(`${this.api.baseUrl}/email-accounts/smtp`, dto);
  }

  updateSmtp(id: string, dto: UpsertSmtpAccount): Observable<EmailAccount> {
    return this.http.put<EmailAccount>(`${this.api.baseUrl}/email-accounts/smtp/${id}`, dto);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/email-accounts/${id}`);
  }

  setDefault(id: string): Observable<EmailAccount> {
    return this.http.post<EmailAccount>(`${this.api.baseUrl}/email-accounts/${id}/set-default`, {});
  }

  testAccount(id: string, dto: SmtpAccountTestRequest | null = null): Observable<EmailTestResult> {
    return this.http.post<EmailTestResult>(`${this.api.baseUrl}/email-accounts/${id}/test`, dto);
  }

  testSmtpDraft(dto: SmtpAccountTestRequest | null): Observable<EmailTestResult> {
    return this.http.post<EmailTestResult>(`${this.api.baseUrl}/email-accounts/smtp/test`, dto);
  }

  connectMicrosoft(): Observable<MicrosoftConnectResult> {
    return this.http.post<MicrosoftConnectResult>(
      `${this.api.baseUrl}/email-accounts/microsoft/connect`,
      {}
    );
  }

  disconnect(id: string): Observable<void> {
    return this.http.post<void>(`${this.api.baseUrl}/email-accounts/${id}/disconnect`, {});
  }
}

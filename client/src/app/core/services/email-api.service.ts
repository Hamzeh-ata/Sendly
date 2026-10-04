import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { TestEmailRequest } from '../models/campaign.model';
import { SmtpTestResult } from '../models/smtp.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class EmailApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  sendTestEmail(request: TestEmailRequest): Observable<SmtpTestResult> {
    return this.http.post<SmtpTestResult>(`${this.api.baseUrl}/email/test`, request);
  }
}

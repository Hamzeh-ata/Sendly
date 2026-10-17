import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { TestEmailRequest } from '../models/campaign.model';
import { EmailTestResult } from '../models/email-account.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class EmailApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  sendTestEmail(request: TestEmailRequest): Observable<EmailTestResult> {
    return this.http.post<EmailTestResult>(`${this.api.baseUrl}/email/test`, request);
  }
}

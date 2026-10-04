import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiBaseService } from './api-base.service';
import {
  EmailSignature,
  SignatureSettings,
  UpdateSignatureSettings,
  UpsertEmailSignature
} from '../models/signature.model';

@Injectable({ providedIn: 'root' })
export class SignatureApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  list(): Observable<EmailSignature[]> {
    return this.http.get<EmailSignature[]>(`${this.api.baseUrl}/signatures`);
  }

  getSettings(): Observable<SignatureSettings> {
    return this.http.get<SignatureSettings>(`${this.api.baseUrl}/signatures/settings`);
  }

  updateSettings(request: UpdateSignatureSettings): Observable<SignatureSettings> {
    return this.http.put<SignatureSettings>(`${this.api.baseUrl}/signatures/settings`, request);
  }

  getDefault(): Observable<EmailSignature> {
    return this.http.get<EmailSignature>(`${this.api.baseUrl}/signatures/default`);
  }

  create(request: UpsertEmailSignature): Observable<EmailSignature> {
    return this.http.post<EmailSignature>(`${this.api.baseUrl}/signatures`, request);
  }

  update(id: string, request: UpsertEmailSignature): Observable<EmailSignature> {
    return this.http.put<EmailSignature>(`${this.api.baseUrl}/signatures/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/signatures/${id}`);
  }

  duplicate(id: string): Observable<EmailSignature> {
    return this.http.post<EmailSignature>(`${this.api.baseUrl}/signatures/${id}/duplicate`, {});
  }

  setDefault(id: string): Observable<EmailSignature> {
    return this.http.post<EmailSignature>(`${this.api.baseUrl}/signatures/${id}/set-default`, {});
  }
}

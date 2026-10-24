import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AppSettings,
  OllamaHealthResult,
  ProviderConnectionTestResult
} from '../models/app-settings.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class AppSettingsApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  get(): Observable<AppSettings> {
    return this.http.get<AppSettings>(`${this.api.baseUrl}/app-settings`);
  }

  update(request: AppSettings): Observable<AppSettings> {
    return this.http.put<AppSettings>(`${this.api.baseUrl}/app-settings`, request);
  }

  testBraveSearch(): Observable<ProviderConnectionTestResult> {
    return this.http.post<ProviderConnectionTestResult>(
      `${this.api.baseUrl}/app-settings/brave-search/test`,
      {}
    );
  }

  testSearXng(): Observable<ProviderConnectionTestResult> {
    return this.http.post<ProviderConnectionTestResult>(
      `${this.api.baseUrl}/app-settings/searxng/test`,
      {}
    );
  }

  testOllama(): Observable<OllamaHealthResult> {
    return this.http.post<OllamaHealthResult>(
      `${this.api.baseUrl}/app-settings/ollama/test`,
      {}
    );
  }
}

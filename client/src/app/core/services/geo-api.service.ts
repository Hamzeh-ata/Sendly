import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { GeoCountry } from '../models/geo-country.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class GeoApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  listCountries(): Observable<GeoCountry[]> {
    return this.http.get<GeoCountry[]>(`${this.api.baseUrl}/geo/countries`);
  }
}

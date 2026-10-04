import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ApiBaseService {
  readonly baseUrl = 'http://localhost:5088/api';
  readonly hubUrl = 'http://localhost:5088/hubs/campaigns';
}

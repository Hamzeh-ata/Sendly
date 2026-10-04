import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AttachmentUploadResult } from '../models/attachment.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class AttachmentApiService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(ApiBaseService);

  upload(file: File): Observable<AttachmentUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<AttachmentUploadResult>(`${this.api.baseUrl}/attachments`, formData);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api.baseUrl}/attachments/${id}`);
  }
}

import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { ResearchRunDetail } from '../models/prospecting.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class ResearchHubService {
  private readonly api = inject(ApiBaseService);
  private connection: signalR.HubConnection | null = null;
  private joinedRunId: string | null = null;
  private operationQueue: Promise<void> = Promise.resolve();

  readonly connected = signal(false);
  readonly latestProgress = signal<ResearchRunDetail | null>(null);

  joinResearchRun(researchRunId: string): void {
    const normalizedId = this.normalizeId(researchRunId);
    if (!normalizedId) {
      return;
    }

    this.enqueue(async () => {
      await this.ensureConnected();
      if (!this.connection) {
        return;
      }

      if (this.joinedRunId && this.joinedRunId !== normalizedId) {
        await this.safeInvoke('LeaveResearchRun', this.joinedRunId);
        this.joinedRunId = null;
      }

      if (this.joinedRunId === normalizedId) {
        return;
      }

      await this.safeInvoke('JoinResearchRun', normalizedId);
      this.joinedRunId = normalizedId;
    });
  }

  leaveResearchRun(): void {
    this.enqueue(async () => {
      if (!this.connection || !this.joinedRunId) {
        return;
      }

      const runId = this.joinedRunId;
      this.joinedRunId = null;
      await this.safeInvoke('LeaveResearchRun', runId);
    });
  }

  clearProgress(): void {
    this.latestProgress.set(null);
  }

  private enqueue(operation: () => Promise<void>): void {
    this.operationQueue = this.operationQueue.then(operation).catch(() => undefined);
  }

  private async ensureConnected(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      this.connected.set(true);
      return;
    }

    if (this.connection?.state === signalR.HubConnectionState.Connecting) {
      return;
    }

    if (this.connection) {
      try {
        await this.connection.stop();
      } catch {
      }
      this.connection = null;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(this.api.researchHubUrl)
      .withAutomaticReconnect()
      .build();

    connection.on('researchProgress', (progress: ResearchRunDetail) => {
      this.latestProgress.set(this.normalizeProgress(progress));
    });

    connection.onreconnected(() => {
      this.connected.set(true);
      const runId = this.joinedRunId;
      if (runId) {
        void this.safeInvoke('JoinResearchRun', runId);
      }
    });

    connection.onclose(() => {
      this.connected.set(false);
    });

    this.connection = connection;

    try {
      await connection.start();
      this.connected.set(true);
    } catch {
      this.connected.set(false);
      this.connection = null;
    }
  }

  private async safeInvoke(methodName: string, researchRunId: string): Promise<void> {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke(methodName, researchRunId);
    } catch {
    }
  }

  private normalizeProgress(progress: ResearchRunDetail): ResearchRunDetail {
    return {
      ...progress,
      id: this.normalizeId(progress.id),
      projectId: this.normalizeId(progress.projectId)
    };
  }

  private normalizeId(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    return String(value).trim().toLowerCase();
  }
}

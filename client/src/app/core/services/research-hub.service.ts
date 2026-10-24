import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { ResearchRunDetail } from '../models/prospecting.model';
import { ResearchRunEventItem } from '../models/research-query.model';
import { ResearchHubConnectionState } from '../models/researcher.model';
import { ApiBaseService } from './api-base.service';

const MAX_EVENT_BUFFER = 500;

@Injectable({ providedIn: 'root' })
export class ResearchHubService {
  private readonly api = inject(ApiBaseService);
  private connection: signalR.HubConnection | null = null;
  private joinedRunId: string | null = null;
  private operationQueue: Promise<void> = Promise.resolve();

  readonly connected = signal(false);
  readonly connectionState = signal<ResearchHubConnectionState>('disconnected');
  readonly latestProgress = signal<ResearchRunDetail | null>(null);
  readonly latestEvent = signal<ResearchRunEventItem | null>(null);
  readonly eventsBuffer = signal<ResearchRunEventItem[]>([]);

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
        this.clearEvents();
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

  clearEvents(): void {
    this.latestEvent.set(null);
    this.eventsBuffer.set([]);
  }

  seedEvents(events: ResearchRunEventItem[]): void {
    const normalized = events.map((item) => this.normalizeEvent(item));
    const existing = this.eventsBuffer();
    const byId = new Map<string, ResearchRunEventItem>();
    for (const item of [...normalized, ...existing]) {
      const key = item.id || `${item.createdAtUtc}|${item.message}|${item.url ?? ''}`;
      if (!byId.has(key)) {
        byId.set(key, item);
      }
    }
    const merged = Array.from(byId.values()).sort((a, b) => {
      const left = new Date(a.createdAtUtc).getTime();
      const right = new Date(b.createdAtUtc).getTime();
      return right - left;
    });
    this.eventsBuffer.set(merged.slice(0, MAX_EVENT_BUFFER));
    if (merged.length > 0) {
      this.latestEvent.set(merged[0]);
    }
  }

  private enqueue(operation: () => Promise<void>): void {
    this.operationQueue = this.operationQueue.then(operation).catch(() => undefined);
  }

  private async ensureConnected(): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      this.connected.set(true);
      this.connectionState.set('connected');
      return;
    }

    if (this.connection?.state === signalR.HubConnectionState.Connecting) {
      this.connectionState.set('reconnecting');
      return;
    }

    if (this.connection?.state === signalR.HubConnectionState.Reconnecting) {
      this.connectionState.set('reconnecting');
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

    connection.on('researchEvent', (researchEvent: ResearchRunEventItem) => {
      this.appendEvent(this.normalizeEvent(researchEvent));
    });

    connection.onreconnecting(() => {
      this.connected.set(false);
      this.connectionState.set('reconnecting');
    });

    connection.onreconnected(() => {
      this.connected.set(true);
      this.connectionState.set('connected');
      const runId = this.joinedRunId;
      if (runId) {
        void this.safeInvoke('JoinResearchRun', runId);
      }
    });

    connection.onclose(() => {
      this.connected.set(false);
      this.connectionState.set('disconnected');
    });

    this.connection = connection;
    this.connectionState.set('reconnecting');

    try {
      await connection.start();
      this.connected.set(true);
      this.connectionState.set('connected');
    } catch {
      this.connected.set(false);
      this.connectionState.set('disconnected');
      this.connection = null;
    }
  }

  private appendEvent(event: ResearchRunEventItem): void {
    const runId = this.joinedRunId;
    if (runId && event.researchRunId && this.normalizeId(event.researchRunId) !== runId) {
      return;
    }

    this.latestEvent.set(event);
    const current = this.eventsBuffer();
    const withoutDup = current.filter((item) => item.id !== event.id);
    this.eventsBuffer.set([event, ...withoutDup].slice(0, MAX_EVENT_BUFFER));
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

  private normalizeEvent(event: ResearchRunEventItem): ResearchRunEventItem {
    return {
      ...event,
      id: this.normalizeId(event.id),
      researchRunId: event.researchRunId ? this.normalizeId(event.researchRunId) : event.researchRunId
    };
  }

  private normalizeId(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    return String(value).trim().toLowerCase();
  }
}

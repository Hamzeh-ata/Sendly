import { Injectable, inject, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { CampaignProgress } from '../models/campaign.model';
import { ApiBaseService } from './api-base.service';

@Injectable({ providedIn: 'root' })
export class CampaignHubService {
  private readonly api = inject(ApiBaseService);
  private connection: signalR.HubConnection | null = null;
  private joinedCampaignId: string | null = null;
  private operationQueue: Promise<void> = Promise.resolve();

  readonly connected = signal(false);
  readonly latestProgress = signal<CampaignProgress | null>(null);

  joinCampaign(campaignId: string): void {
    const normalizedId = this.normalizeId(campaignId);
    if (!normalizedId) {
      return;
    }

    this.enqueue(async () => {
      await this.ensureConnected();
      if (!this.connection) {
        return;
      }

      if (this.joinedCampaignId && this.joinedCampaignId !== normalizedId) {
        await this.safeInvoke('LeaveCampaign', this.joinedCampaignId);
        this.joinedCampaignId = null;
      }

      if (this.joinedCampaignId === normalizedId) {
        return;
      }

      await this.safeInvoke('JoinCampaign', normalizedId);
      this.joinedCampaignId = normalizedId;
    });
  }

  leaveCampaign(): void {
    this.enqueue(async () => {
      if (!this.connection || !this.joinedCampaignId) {
        return;
      }

      const campaignId = this.joinedCampaignId;
      this.joinedCampaignId = null;
      await this.safeInvoke('LeaveCampaign', campaignId);
    });
  }

  clearProgress(): void {
    this.latestProgress.set(null);
  }

  private enqueue(operation: () => Promise<void>): void {
    this.operationQueue = this.operationQueue
      .then(operation)
      .catch(() => undefined);
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
      .withUrl(this.api.hubUrl)
      .withAutomaticReconnect()
      .build();

    connection.on('campaignProgress', (progress: CampaignProgress) => {
      this.latestProgress.set(this.normalizeProgress(progress));
    });

    connection.onreconnected(() => {
      this.connected.set(true);
      const campaignId = this.joinedCampaignId;
      if (campaignId) {
        void this.safeInvoke('JoinCampaign', campaignId);
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

  private async safeInvoke(methodName: string, campaignId: string): Promise<void> {
    if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke(methodName, campaignId);
    } catch {
    }
  }

  private normalizeProgress(progress: CampaignProgress): CampaignProgress {
    return {
      ...progress,
      campaignId: this.normalizeId(progress.campaignId)
    };
  }

  private normalizeId(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    return String(value).trim().toLowerCase();
  }
}

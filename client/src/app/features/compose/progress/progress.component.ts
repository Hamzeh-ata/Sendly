import { Component, effect, inject, input, OnDestroy, output, signal, untracked } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { CampaignProgress } from '../../../core/models/campaign.model';
import { CampaignHubService } from '../../../core/services/campaign-hub.service';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-compose-progress',
  imports: [TranslatePipe, StatusBadgeComponent],
  templateUrl: './progress.component.html',
  styleUrl: './progress.component.scss'
})
export class ComposeProgressComponent implements OnDestroy {
  private readonly hub = inject(CampaignHubService);

  readonly campaignId = input.required<string>();
  readonly viewDetail = output<void>();
  readonly dismiss = output<void>();

  readonly progress = signal<CampaignProgress | null>(null);

  constructor() {
    effect(() => {
      const id = this.campaignId();
      if (!id) {
        return;
      }
      untracked(() => this.hub.joinCampaign(id));
    });

    effect(() => {
      const latest = this.hub.latestProgress();
      const id = this.campaignId();
      if (!latest || !id) {
        return;
      }
      if (this.sameCampaign(latest.campaignId, id)) {
        untracked(() => this.progress.set(latest));
      }
    });
  }

  ngOnDestroy(): void {
    this.hub.leaveCampaign();
  }

  onViewDetail(): void {
    this.viewDetail.emit();
  }

  onDismiss(): void {
    this.dismiss.emit();
  }

  private sameCampaign(left: string, right: string): boolean {
    return left.trim().toLowerCase() === right.trim().toLowerCase();
  }
}

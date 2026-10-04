import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { CampaignSummary } from '../../core/models/campaign.model';
import { CampaignHistoryRow } from '../../core/models/history-row.model';
import { CampaignApiService } from '../../core/services/campaign-api.service';
import { formatDate } from '../../core/utils/format.utils';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge/status-badge.component';

@Component({
  selector: 'app-history-page',
  imports: [TranslatePipe, LoadingComponent, EmptyStateComponent, StatusBadgeComponent],
  templateUrl: './history.page.html',
  styleUrl: './history.page.scss'
})
export class HistoryPage implements OnInit {
  private readonly campaignApi = inject(CampaignApiService);
  private readonly router = inject(Router);

  readonly campaigns = signal<CampaignHistoryRow[]>([]);
  readonly loading = signal(true);
  readonly search = signal('');

  ngOnInit(): void {
    this.loadCampaigns();
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    this.loadCampaigns();
  }

  openDetail(id: string): void {
    if (!id) {
      return;
    }
    this.router.navigate(['/history', id]);
  }

  private loadCampaigns(): void {
    this.loading.set(true);
    const term = this.search().trim();
    this.campaignApi.getCampaigns(term || undefined).subscribe({
      next: (items) => {
        this.campaigns.set(this.mapRows(items ?? []));
        this.loading.set(false);
      },
      error: () => {
        this.campaigns.set([]);
        this.loading.set(false);
      }
    });
  }

  private mapRows(items: CampaignSummary[]): CampaignHistoryRow[] {
    const rows: CampaignHistoryRow[] = [];
    for (const item of items) {
      rows.push({
        ...item,
        id: String(item.id),
        status: item.status,
        createdLabel: formatDate(item.createdAtUtc ?? null)
      });
    }
    return rows;
  }
}

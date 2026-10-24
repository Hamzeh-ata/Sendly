import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ResearchRunApiService } from '../../../core/services/research-run-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-researcher-live-entry-page',
  imports: [RouterLink, TranslatePipe, EmptyStateComponent],
  template: `
    <div class="researcher-page">
      @if (loading()) {
        <div class="rc-skeleton">
          <div class="bone"></div>
        </div>
      } @else {
        <app-empty-state
          titleKey="researcher.live.emptyTitle"
          descriptionKey="researcher.live.emptyDescription"
        />
        <div class="actions">
          <a class="btn btn-primary" routerLink="/researcher/runs">
            {{ 'researcher.live.backToRuns' | translate }}
          </a>
        </div>
      }
    </div>
  `,
  styles: `
    .actions {
      margin-top: 1rem;
    }
  `
})
export class ResearcherLiveEntryPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);

  ngOnInit(): void {
    const queryRunId = this.route.snapshot.queryParamMap.get('runId');
    if (queryRunId) {
      void this.router.navigate(['/researcher/live', queryRunId]);
      return;
    }

    this.researchRunApi.getOverview().subscribe({
      next: (overview) => {
        const active = overview.activeRunDetails?.[0];
        if (active?.id) {
          void this.router.navigate(['/researcher/live', active.id]);
          return;
        }
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        if (err.status === 0) {
          this.notification.error(this.translate.instant('common.apiOffline'));
          return;
        }
        this.notification.error(this.translate.instant('common.errorGeneric'));
      }
    });
  }
}

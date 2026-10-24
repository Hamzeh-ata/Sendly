import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ResearchErrorGroupRow } from '../../../core/models/researcher.model';
import { ResearchRunApiService } from '../../../core/services/research-run-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { mapErrorGroupToRow } from '../researcher.mappers';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';

@Component({
  selector: 'app-researcher-errors-page',
  imports: [RouterLink, TranslatePipe, EmptyStateComponent],
  templateUrl: './researcher-errors.page.html',
  styleUrl: './researcher-errors.page.scss'
})
export class ResearcherErrorsPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly researchRunApi = inject(ResearchRunApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly runId = signal('');
  readonly rows = signal<ResearchErrorGroupRow[]>([]);

  readonly hasRows = computed(() => this.rows().length > 0);

  ngOnInit(): void {
    const runId = this.route.snapshot.queryParamMap.get('researchRunId') ?? '';
    this.runId.set(runId);
    this.loadErrors(runId || undefined);
  }

  private loadErrors(researchRunId?: string): void {
    this.loading.set(true);
    this.researchRunApi.listErrors(researchRunId).subscribe({
      next: (items) => {
        this.rows.set((items ?? []).map((item) => mapErrorGroupToRow(item)));
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.rows.set([]);
        this.notifyError(err);
      }
    });
  }

  private notifyError(err: HttpErrorResponse): void {
    if (err.status === 0) {
      this.notification.error(this.translate.instant('common.apiOffline'));
      return;
    }
    if (err.error?.message) {
      this.notification.error(err.error.message);
      return;
    }
    this.notification.error(this.translate.instant('common.errorGeneric'));
  }
}

import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { OutreachSequenceSummary, SequenceListRow } from '../../core/models/sequence.model';
import { SequenceApiService } from '../../core/services/sequence-api.service';
import { formatDate } from '../../core/utils/format.utils';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';

@Component({
  selector: 'app-sequences-page',
  imports: [TranslatePipe, LoadingComponent, EmptyStateComponent],
  templateUrl: './sequences.page.html',
  styleUrl: './sequences.page.scss'
})
export class SequencesPage implements OnInit {
  private readonly sequenceApi = inject(SequenceApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly sequences = signal<SequenceListRow[]>([]);
  readonly projectId = signal<string | null>(null);

  ngOnInit(): void {
    const projectId = this.route.snapshot.queryParamMap.get('projectId');
    this.projectId.set(projectId);
    this.loadSequences(projectId || undefined);
  }

  openCreate(): void {
    const projectId = this.projectId();
    if (projectId) {
      this.router.navigate(['/sequences/new'], { queryParams: { projectId } });
      return;
    }
    this.router.navigate(['/sequences/new']);
  }

  openDetail(id: string): void {
    this.router.navigate(['/sequences', id]);
  }

  private loadSequences(projectId?: string): void {
    this.loading.set(true);
    this.sequenceApi.list(projectId).subscribe({
      next: (items) => {
        this.sequences.set(this.mapRows(items ?? []));
        this.loading.set(false);
      },
      error: () => {
        this.sequences.set([]);
        this.loading.set(false);
      }
    });
  }

  private mapRows(items: OutreachSequenceSummary[]): SequenceListRow[] {
    const rows: SequenceListRow[] = [];
    for (const item of items) {
      rows.push({
        ...item,
        createdLabel: formatDate(item.createdAtUtc ?? null),
        statusLabelKey: item.isActive ? 'sequences.statusActive' : 'sequences.statusInactive',
        descriptionLabel: this.snippet(item.description ?? ''),
        projectLabel: item.projectName || '—'
      });
    }
    return rows;
  }

  private snippet(value: string): string {
    const trimmed = value.trim();
    if (!trimmed) {
      return '—';
    }
    if (trimmed.length <= 80) {
      return trimmed;
    }
    return `${trimmed.slice(0, 77)}...`;
  }
}

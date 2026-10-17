import { Component, computed, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-status-badge',
  imports: [TranslatePipe],
  templateUrl: './status-badge.component.html',
  styleUrl: './status-badge.component.scss'
})
export class StatusBadgeComponent {
  readonly status = input.required<string>();
  readonly prefix = input('status');

  readonly statusClass = computed(() => {
    const value = this.status();
    if (
      value === 'completed' ||
      value === 'sent' ||
      value === 'qualified' ||
      value === 'readyForOutreach' ||
      value === 'contacted'
    ) {
      return 'success';
    }
    if (
      value === 'failed' ||
      value === 'completedWithFailures' ||
      value === 'rejected' ||
      value === 'doNotContact'
    ) {
      return 'danger';
    }
    if (
      value === 'running' ||
      value === 'sending' ||
      value === 'queued' ||
      value === 'pending' ||
      value === 'waiting' ||
      value === 'researching' ||
      value === 'discovered'
    ) {
      return 'info';
    }
    if (value === 'cancelled') {
      return 'muted';
    }
    return 'warning';
  });

  readonly translationKey = computed(() => `${this.prefix()}.${this.status()}`);
}

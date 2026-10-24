import { Component, computed, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { ResearchHubConnectionState } from '../../../core/models/researcher.model';

@Component({
  selector: 'app-connection-status',
  imports: [TranslatePipe],
  template: `
    <span class="connection-status" [class]="state()">
      <span class="dot"></span>
      {{ labelKey() | translate }}
    </span>
  `,
  styles: `
    .connection-status {
      display: inline-flex;
      align-items: center;
      gap: 0.4rem;
      font-size: 0.8125rem;
      color: var(--text-secondary);
    }
    .dot {
      width: 0.5rem;
      height: 0.5rem;
      border-radius: 50%;
      background: var(--text-muted);
    }
    .connected .dot {
      background: #34d399;
      box-shadow: 0 0 0 3px rgba(52, 211, 153, 0.2);
    }
    .reconnecting .dot {
      background: #fbbf24;
    }
    .disconnected .dot {
      background: #f87171;
    }
  `
})
export class ConnectionStatusComponent {
  readonly state = input.required<ResearchHubConnectionState>();

  readonly labelKey = computed(() => `researcher.connection.${this.state()}`);
}

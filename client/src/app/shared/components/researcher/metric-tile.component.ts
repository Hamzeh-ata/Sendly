import { Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-metric-tile',
  imports: [TranslatePipe],
  template: `
    <div class="metric-tile" [class]="tone()">
      <span class="label">{{ labelKey() | translate }}</span>
      <span class="value">{{ value() }}</span>
    </div>
  `,
  styles: `
    .metric-tile {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
      padding: 0.875rem 1rem;
      background: var(--rc-surface);
      border: 1px solid var(--rc-border);
      border-radius: var(--radius-md);
      min-width: 0;
    }
    .label {
      font-size: 0.75rem;
      color: var(--text-secondary);
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }
    .value {
      font-family: var(--font-mono, ui-monospace, monospace);
      font-size: 1.35rem;
      font-weight: 650;
      color: var(--text-primary);
    }
    .metric-tile.active {
      border-color: rgba(56, 189, 248, 0.45);
      box-shadow: inset 0 0 0 1px rgba(56, 189, 248, 0.12);
    }
    .metric-tile.active .value {
      color: #7dd3fc;
    }
    .metric-tile.success .value {
      color: #34d399;
    }
    .metric-tile.warning .value {
      color: #fbbf24;
    }
    .metric-tile.danger .value {
      color: #f87171;
    }
  `
})
export class MetricTileComponent {
  readonly labelKey = input.required<string>();
  readonly value = input.required<string>();
  readonly tone = input<'default' | 'active' | 'success' | 'warning' | 'danger'>('default');
}

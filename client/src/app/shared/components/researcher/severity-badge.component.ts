import { Component, computed, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { mapSeverityClass } from '../../../features/researcher/researcher.mappers';

@Component({
  selector: 'app-severity-badge',
  imports: [TranslatePipe],
  template: `
    <span class="severity-badge" [class]="toneClass()">
      {{ translationKey() | translate }}
    </span>
  `,
  styles: `
    .severity-badge {
      display: inline-flex;
      align-items: center;
      padding: 0.125rem 0.5rem;
      border-radius: 4px;
      font-size: 0.75rem;
      font-weight: 600;
      letter-spacing: 0.02em;
      text-transform: uppercase;
      border: 1px solid transparent;
    }
    .severity-badge.info {
      color: #7dd3fc;
      background: rgba(56, 189, 248, 0.12);
      border-color: rgba(56, 189, 248, 0.28);
    }
    .severity-badge.warning {
      color: #fbbf24;
      background: rgba(251, 191, 36, 0.12);
      border-color: rgba(251, 191, 36, 0.28);
    }
    .severity-badge.danger {
      color: #f87171;
      background: rgba(248, 113, 113, 0.12);
      border-color: rgba(248, 113, 113, 0.28);
    }
  `
})
export class SeverityBadgeComponent {
  readonly severity = input.required<string>();

  readonly toneClass = computed(() => mapSeverityClass(this.severity()));
  readonly translationKey = computed(() => {
    const value = this.severity() || 'Info';
    return `researcher.severity.${value}`;
  });
}

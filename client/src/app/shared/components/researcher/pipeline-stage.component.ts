import { Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-pipeline-stage',
  imports: [TranslatePipe],
  template: `
    <div class="pipeline-stage" [class.active]="active()" [class.idle]="!active()">
      <span class="name">{{ labelKey() | translate }}</span>
      <span class="count">{{ count() }}</span>
    </div>
  `,
  styles: `
    .pipeline-stage {
      display: flex;
      flex-direction: column;
      gap: 0.25rem;
      min-width: 5.5rem;
      padding: 0.65rem 0.75rem;
      border-radius: var(--radius-sm);
      border: 1px solid var(--rc-border);
      background: rgba(15, 23, 42, 0.55);
    }
    .name {
      font-size: 0.7rem;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.03em;
    }
    .count {
      font-family: var(--font-mono, ui-monospace, monospace);
      font-size: 1rem;
      color: var(--text-secondary);
    }
    .pipeline-stage.active {
      border-color: rgba(56, 189, 248, 0.5);
      background: rgba(14, 116, 144, 0.18);
    }
    .pipeline-stage.active .name,
    .pipeline-stage.active .count {
      color: #7dd3fc;
    }
  `
})
export class PipelineStageComponent {
  readonly labelKey = input.required<string>();
  readonly count = input(0);
  readonly active = input(false);
}

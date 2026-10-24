import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { ResearchEventRow } from '../../../core/models/researcher.model';
import { SeverityBadgeComponent } from './severity-badge.component';

@Component({
  selector: 'app-event-row',
  imports: [TranslatePipe, RouterLink, SeverityBadgeComponent],
  template: `
    <article class="event-row" [class.expanded]="expanded()">
      <div class="event-main" (click)="toggleExpand.emit(row().id)">
        <div class="event-meta">
          <app-severity-badge [severity]="row().severity" />
          <span class="type">{{ row().eventType }}</span>
          <span class="stage">{{ row().stage }}</span>
          <span class="time">{{ row().createdLabel }}</span>
        </div>
        <p class="message">{{ row().message || ('researcher.events.noMessage' | translate) }}</p>
        <div class="chips">
          @if (row().domain) {
            <span>{{ row().domain }}</span>
          }
          @if (row().httpStatusLabel !== '—') {
            <span>HTTP {{ row().httpStatusLabel }}</span>
          }
          @if (row().durationLabel !== '—') {
            <span>{{ row().durationLabel }}</span>
          }
          @if (row().provider) {
            <span>{{ row().provider }}</span>
          }
        </div>
      </div>
      <div class="event-actions">
        @if (row().hasFetchLink) {
          <a
            class="btn btn-ghost btn-sm"
            [routerLink]="['/researcher/fetch', row().researchRunId, row().relatedEntityId]"
            (click)="$event.stopPropagation()"
          >
            {{ 'researcher.events.openFetch' | translate }}
          </a>
        }
        <button
          type="button"
          class="btn btn-ghost btn-sm"
          (click)="copyDetails.emit(row().id); $event.stopPropagation()"
        >
          {{ 'researcher.events.copy' | translate }}
        </button>
      </div>
      @if (expanded()) {
        <div class="event-details">
          @if (row().url) {
            <div class="detail-line">
              <span>{{ 'researcher.events.url' | translate }}</span>
              <a [href]="row().url" target="_blank" rel="noopener noreferrer">{{ row().url }}</a>
            </div>
          }
          @if (row().hasDataJson) {
            <pre class="data-json">{{ row().dataJson }}</pre>
          } @else {
            <p class="muted">{{ 'researcher.events.noData' | translate }}</p>
          }
        </div>
      }
    </article>
  `,
  styles: `
    .event-row {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.5rem 0.75rem;
      padding: 0.75rem 0.85rem;
      border-bottom: 1px solid var(--rc-border);
      background: rgba(15, 23, 42, 0.35);
    }
    .event-main {
      cursor: pointer;
      min-width: 0;
    }
    .event-meta {
      display: flex;
      flex-wrap: wrap;
      gap: 0.45rem 0.65rem;
      align-items: center;
      font-family: var(--font-mono, ui-monospace, monospace);
      font-size: 0.75rem;
      color: var(--text-secondary);
      margin-bottom: 0.35rem;
    }
    .message {
      margin: 0;
      color: var(--text-primary);
      font-size: 0.875rem;
      word-break: break-word;
    }
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
      margin-top: 0.4rem;
    }
    .chips span {
      font-size: 0.7rem;
      color: var(--text-muted);
      border: 1px solid var(--rc-border);
      border-radius: 4px;
      padding: 0.1rem 0.4rem;
      font-family: var(--font-mono, ui-monospace, monospace);
    }
    .event-actions {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
      align-items: stretch;
    }
    .btn-sm {
      padding: 0.3rem 0.55rem;
      font-size: 0.75rem;
    }
    .event-details {
      grid-column: 1 / -1;
      padding-top: 0.35rem;
    }
    .detail-line {
      display: flex;
      flex-direction: column;
      gap: 0.2rem;
      margin-bottom: 0.5rem;
      font-size: 0.8rem;
    }
    .detail-line a {
      color: #7dd3fc;
      word-break: break-all;
    }
    .data-json {
      margin: 0;
      padding: 0.75rem;
      border-radius: var(--radius-sm);
      background: #0a1220;
      border: 1px solid var(--rc-border);
      color: #cbd5e1;
      font-size: 0.75rem;
      overflow: auto;
      max-height: 16rem;
      white-space: pre-wrap;
      word-break: break-word;
    }
    .muted {
      margin: 0;
      color: var(--text-muted);
      font-size: 0.8rem;
    }
  `
})
export class EventRowComponent {
  readonly row = input.required<ResearchEventRow>();
  readonly expanded = input(false);
  readonly toggleExpand = output<string>();
  readonly copyDetails = output<string>();
}

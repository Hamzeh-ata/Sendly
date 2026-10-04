import { Injectable, signal } from '@angular/core';
import { timer } from 'rxjs';
import { NotificationMessage } from '../models/api.model';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly autoDismissMs = 4000;

  readonly messages = signal<NotificationMessage[]>([]);

  show(type: NotificationMessage['type'], message: string): void {
    const id = crypto.randomUUID();
    this.messages.update((items) => [...items, { id, type, message }]);
    this.scheduleDismiss(id);
  }

  success(message: string): void {
    this.show('success', message);
  }

  error(message: string): void {
    this.show('error', message);
  }

  info(message: string): void {
    this.show('info', message);
  }

  warning(message: string): void {
    this.show('warning', message);
  }

  dismiss(id: string): void {
    this.messages.update((items) => items.filter((item) => item.id !== id));
  }

  private scheduleDismiss(id: string): void {
    timer(this.autoDismissMs).subscribe(() => {
      this.dismiss(id);
    });
  }
}

import { Component, inject } from '@angular/core';
import { NotificationService } from '../../../core/services/notification.service';

@Component({
  selector: 'app-notification-host',
  templateUrl: './notification-host.component.html',
  styleUrl: './notification-host.component.scss'
})
export class NotificationHostComponent {
  private readonly notificationService = inject(NotificationService);

  readonly messages = this.notificationService.messages;

  dismiss(id: string): void {
    this.notificationService.dismiss(id);
  }
}

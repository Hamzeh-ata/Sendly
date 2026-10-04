import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { SmtpApiService } from '../../core/services/smtp-api.service';
import { NotificationHostComponent } from '../../shared/components/notification-host/notification-host.component';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, NotificationHostComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent implements OnInit {
  private readonly smtpApi = inject(SmtpApiService);

  readonly sidebarOpen = signal(false);
  readonly smtpConfigured = signal(false);
  readonly smtpLoading = signal(true);

  ngOnInit(): void {
    this.loadSmtpStatus();
  }

  toggleSidebar(): void {
    this.sidebarOpen.update((open) => !open);
  }

  closeSidebar(): void {
    this.sidebarOpen.set(false);
  }

  private loadSmtpStatus(): void {
    this.smtpLoading.set(true);
    this.smtpApi.getSettings().subscribe({
      next: (settings) => {
        this.smtpConfigured.set(settings.isConfigured && settings.hasPassword);
        this.smtpLoading.set(false);
      },
      error: () => {
        this.smtpConfigured.set(false);
        this.smtpLoading.set(false);
      }
    });
  }
}

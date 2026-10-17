import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { EmailAccountApiService } from '../../core/services/email-account-api.service';
import { NotificationHostComponent } from '../../shared/components/notification-host/notification-host.component';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, NotificationHostComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent implements OnInit {
  private readonly emailAccountApi = inject(EmailAccountApiService);

  readonly sidebarOpen = signal(false);
  readonly accountsConfigured = signal(false);
  readonly accountsLoading = signal(true);

  ngOnInit(): void {
    this.loadAccountStatus();
  }

  toggleSidebar(): void {
    this.sidebarOpen.update((open) => !open);
  }

  closeSidebar(): void {
    this.sidebarOpen.set(false);
  }

  private loadAccountStatus(): void {
    this.accountsLoading.set(true);
    this.emailAccountApi.getStatus().subscribe({
      next: (status) => {
        this.accountsConfigured.set(status.isConfigured);
        this.accountsLoading.set(false);
      },
      error: () => {
        this.accountsConfigured.set(false);
        this.accountsLoading.set(false);
      }
    });
  }
}

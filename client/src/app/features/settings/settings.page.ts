import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { SmtpSecurityMode, UpsertSmtpSettings } from '../../core/models/smtp.model';
import { SmtpApiService } from '../../core/services/smtp-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { SignaturesManagerComponent } from './signatures/signatures-manager.component';

@Component({
  selector: 'app-settings-page',
  imports: [TranslatePipe, LoadingComponent, ConfirmDialogComponent, SignaturesManagerComponent],
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss'
})
export class SettingsPage implements OnInit {
  private readonly smtpApi = inject(SmtpApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly testing = signal(false);
  readonly showPassword = signal(false);
  readonly showDeleteConfirm = signal(false);
  readonly hasPassword = signal(false);
  readonly isInsecure = signal(false);

  readonly host = signal('');
  readonly port = signal(587);
  readonly securityMode = signal<SmtpSecurityMode>('startTls');
  readonly username = signal('');
  readonly password = signal('');
  readonly senderEmail = signal('');
  readonly senderDisplayName = signal('');
  readonly replyToEmail = signal('');

  readonly securityModeOptions = [
    { value: 'none' as SmtpSecurityMode, labelKey: 'securityMode.none' },
    { value: 'startTls' as SmtpSecurityMode, labelKey: 'securityMode.startTls' },
    { value: 'sslTls' as SmtpSecurityMode, labelKey: 'securityMode.sslTls' }
  ];
  readonly passwordPlaceholder = computed(() =>
    this.hasPassword()
      ? this.translate.instant('settings.passwordKeepExisting')
      : this.translate.instant('settings.passwordPlaceholder')
  );

  ngOnInit(): void {
    this.loadSettings();
  }

  onHostChange(value: string): void {
    this.host.set(value);
  }

  onPortChange(value: string): void {
    this.port.set(this.parsePort(value));
  }

  onSecurityModeChange(value: string): void {
    this.securityMode.set(value as SmtpSecurityMode);
  }

  onUsernameChange(value: string): void {
    this.username.set(value);
  }

  onPasswordChange(value: string): void {
    this.password.set(value);
  }

  onSenderEmailChange(value: string): void {
    this.senderEmail.set(value);
  }

  onSenderDisplayNameChange(value: string): void {
    this.senderDisplayName.set(value);
  }

  onReplyToChange(value: string): void {
    this.replyToEmail.set(value);
  }

  togglePassword(): void {
    this.showPassword.update((visible) => !visible);
  }

  save(): void {
    this.saving.set(true);
    const dto = this.buildDto();
    this.smtpApi.saveSettings(dto).subscribe({
      next: (settings) => {
        this.saving.set(false);
        this.hasPassword.set(settings.hasPassword);
        this.isInsecure.set(settings.isInsecure);
        this.password.set('');
        this.notification.success(this.translate.instant('settings.saveSuccess'));
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  testConnection(): void {
    this.testing.set(true);
    const dto = this.buildDto();
    this.smtpApi.testConnection(dto).subscribe({
      next: (result) => {
        this.testing.set(false);
        if (result.success) {
          this.notification.success(result.message);
        } else {
          this.notification.error(result.message);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.testing.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  openDeleteConfirm(): void {
    this.showDeleteConfirm.set(true);
  }

  closeDeleteConfirm(): void {
    this.showDeleteConfirm.set(false);
  }

  deleteSettings(): void {
    this.saving.set(true);
    this.smtpApi.deleteSettings().subscribe({
      next: () => {
        this.saving.set(false);
        this.showDeleteConfirm.set(false);
        this.resetForm();
        this.notification.success(this.translate.instant('settings.deleteSuccess'));
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  private loadSettings(): void {
    this.loading.set(true);
    this.smtpApi.getSettings().subscribe({
      next: (settings) => {
        this.host.set(settings.host);
        this.port.set(settings.port);
        this.securityMode.set(settings.securityMode);
        this.username.set(settings.username);
        this.senderEmail.set(settings.senderEmail);
        this.senderDisplayName.set(settings.senderDisplayName);
        this.replyToEmail.set(settings.replyToEmail ?? '');
        this.hasPassword.set(settings.hasPassword);
        this.isInsecure.set(settings.isInsecure);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  private buildDto(): UpsertSmtpSettings {
    return {
      host: this.host().trim(),
      port: this.port(),
      securityMode: this.securityMode(),
      username: this.username().trim(),
      password: this.password().trim() || null,
      senderEmail: this.senderEmail().trim(),
      senderDisplayName: this.senderDisplayName().trim(),
      replyToEmail: this.replyToEmail().trim() || null
    };
  }

  private resetForm(): void {
    this.host.set('');
    this.port.set(587);
    this.securityMode.set('startTls');
    this.username.set('');
    this.password.set('');
    this.senderEmail.set('');
    this.senderDisplayName.set('');
    this.replyToEmail.set('');
    this.hasPassword.set(false);
    this.isInsecure.set(false);
  }

  private parsePort(value: string): number {
    const parsed = Number(value);
    if (Number.isNaN(parsed) || parsed <= 0) {
      return 587;
    }
    return parsed;
  }

  private extractError(err: HttpErrorResponse): string {
    if (err.error?.message) {
      return err.error.message;
    }
    return this.translate.instant('common.errorGeneric');
  }
}

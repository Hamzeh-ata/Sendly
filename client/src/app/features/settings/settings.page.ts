import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import {
  EmailAccount,
  SmtpSecurityMode,
  UpsertSmtpAccount
} from '../../core/models/email-account.model';
import {
  AppSettings,
  BraveConnectionStatus,
  ProviderConnectionStatus,
  SettingsTabId,
  SettingsTabItem
} from '../../core/models/app-settings.model';
import { AppSettingsApiService } from '../../core/services/app-settings-api.service';
import { EmailAccountApiService } from '../../core/services/email-account-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { EmptyStateComponent } from '../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../shared/components/loading/loading.component';
import { SignaturesManagerComponent } from './signatures/signatures-manager.component';
import { ConfigSettingsPanelComponent } from './config-settings-panel/config-settings-panel.component';

@Component({
  selector: 'app-settings-page',
  imports: [
    TranslatePipe,
    LoadingComponent,
    EmptyStateComponent,
    ConfirmDialogComponent,
    SignaturesManagerComponent,
    ConfigSettingsPanelComponent
  ],
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.scss'
})
export class SettingsPage implements OnInit {
  private readonly emailAccountApi = inject(EmailAccountApiService);
  private readonly appSettingsApi = inject(AppSettingsApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly tabs: SettingsTabItem[] = [
    { id: 'accounts', labelKey: 'appSettings.tabAccounts' },
    { id: 'signatures', labelKey: 'appSettings.tabSignatures' },
    { id: 'openai', labelKey: 'appSettings.tabOpenAi' },
    { id: 'research', labelKey: 'appSettings.tabResearch' },
    { id: 'whatsapp', labelKey: 'appSettings.tabWhatsApp' },
    { id: 'microsoft', labelKey: 'appSettings.tabMicrosoft' },
    { id: 'emailSending', labelKey: 'appSettings.tabEmailSending' },
    { id: 'attachments', labelKey: 'appSettings.tabAttachments' },
    { id: 'queues', labelKey: 'appSettings.tabQueues' }
  ];

  readonly activeTab = signal<SettingsTabId>('accounts');
  readonly appSettings = signal<AppSettings | null>(null);
  readonly configLoading = signal(false);
  readonly configSaving = signal(false);
  readonly testingBrave = signal(false);
  readonly braveConnectionStatus = signal<BraveConnectionStatus>('NotConfigured');
  readonly braveConnectionMessage = signal<string | null>(null);
  readonly testingSearXng = signal(false);
  readonly searXngConnectionStatus = signal<ProviderConnectionStatus>('NotConfigured');
  readonly searXngConnectionMessage = signal<string | null>(null);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly testing = signal(false);
  readonly connecting = signal(false);
  readonly showPassword = signal(false);
  readonly showProviderPicker = signal(false);
  readonly showEditor = signal(false);
  readonly showDeleteConfirm = signal(false);
  readonly accounts = signal<EmailAccount[]>([]);
  readonly editingId = signal<string | null>(null);
  readonly deleteId = signal<string | null>(null);
  readonly testingId = signal<string | null>(null);
  readonly hasPassword = signal(false);
  readonly isInsecure = signal(false);
  readonly isDefault = signal(false);
  readonly microsoftOAuthConfigured = signal(false);

  readonly displayName = signal('');
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

  readonly editorTitleKey = computed(() =>
    this.editingId() ? 'settings.editSmtpTitle' : 'settings.createSmtpTitle'
  );

  readonly passwordPlaceholder = computed(() =>
    this.hasPassword()
      ? this.translate.instant('settings.passwordKeepExisting')
      : this.translate.instant('settings.passwordPlaceholder')
  );

  readonly showAccountsHeader = computed(() => this.activeTab() === 'accounts');
  readonly isConfigTab = computed(() => {
    const tab = this.activeTab();
    return (
      tab === 'openai' ||
      tab === 'research' ||
      tab === 'whatsapp' ||
      tab === 'microsoft' ||
      tab === 'emailSending' ||
      tab === 'attachments' ||
      tab === 'queues'
    );
  });

  ngOnInit(): void {
    this.resolveInitialTab();
    this.handleMicrosoftQuery();
    this.loadStatus();
    this.loadAccounts();
    this.loadAppSettings();
  }

  selectTab(tabId: SettingsTabId): void {
    this.activeTab.set(tabId);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab: tabId },
      queryParamsHandling: 'merge',
      replaceUrl: true
    });
  }

  onConfigSave(settings: AppSettings): void {
    this.configSaving.set(true);
    this.appSettingsApi.update(settings).subscribe({
      next: (updated) => {
        this.appSettings.set(updated);
        this.braveConnectionStatus.set(updated.braveSearch?.connectionStatus ?? 'NotConfigured');
        this.braveConnectionMessage.set(null);
        this.searXngConnectionStatus.set(updated.searXng?.connectionStatus ?? 'NotConfigured');
        this.searXngConnectionMessage.set(null);
        this.configSaving.set(false);
        this.notification.success(this.translate.instant('appSettings.saveSuccess'));
      },
      error: (err: HttpErrorResponse) => {
        this.configSaving.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  onTestBraveConnection(): void {
    this.testingBrave.set(true);
    this.appSettingsApi.testBraveSearch().subscribe({
      next: (result) => {
        this.testingBrave.set(false);
        this.braveConnectionStatus.set(result.status || 'ConnectionSuccessful');
        this.braveConnectionMessage.set(result.message);
        this.notification.success(
          result.message || this.translate.instant('appSettings.braveTestSuccess')
        );
      },
      error: (err: HttpErrorResponse) => {
        this.testingBrave.set(false);
        const body = err.error as { status?: BraveConnectionStatus; message?: string } | null;
        this.braveConnectionStatus.set(body?.status || 'ConnectionFailed');
        const message = body?.message || this.extractError(err);
        this.braveConnectionMessage.set(message);
        this.notification.error(message);
      }
    });
  }

  onTestSearXngConnection(): void {
    this.testingSearXng.set(true);
    this.appSettingsApi.testSearXng().subscribe({
      next: (result) => {
        this.testingSearXng.set(false);
        this.searXngConnectionStatus.set(result.status || 'ConnectionSuccessful');
        this.searXngConnectionMessage.set(result.message);
        this.notification.success(
          result.message || this.translate.instant('appSettings.searxngTestSuccess')
        );
      },
      error: (err: HttpErrorResponse) => {
        this.testingSearXng.set(false);
        const body = err.error as { status?: ProviderConnectionStatus; message?: string } | null;
        this.searXngConnectionStatus.set(body?.status || 'ConnectionFailed');
        const message = body?.message || this.extractError(err);
        this.searXngConnectionMessage.set(message);
        this.notification.error(message);
      }
    });
  }

  openAdd(): void {
    this.showProviderPicker.set(true);
  }

  closeProviderPicker(): void {
    this.showProviderPicker.set(false);
  }

  chooseSmtp(): void {
    this.showProviderPicker.set(false);
    this.editingId.set(null);
    this.resetForm();
    this.isDefault.set(this.accounts().length === 0);
    this.showEditor.set(true);
  }

  chooseMicrosoft(): void {
    this.showProviderPicker.set(false);
    this.connectMicrosoft();
  }

  openEdit(account: EmailAccount): void {
    if (account.provider !== 'smtp') {
      return;
    }
    this.editingId.set(account.id);
    this.displayName.set(account.displayName);
    this.host.set(account.host ?? '');
    this.port.set(account.port ?? 587);
    this.securityMode.set(account.securityMode ?? 'startTls');
    this.username.set(account.username ?? '');
    this.password.set('');
    this.senderEmail.set(account.senderEmail ?? '');
    this.senderDisplayName.set(account.senderDisplayName ?? '');
    this.replyToEmail.set(account.replyToEmail ?? '');
    this.hasPassword.set(account.hasPassword);
    this.isInsecure.set(account.isInsecure);
    this.isDefault.set(account.isDefault);
    this.showPassword.set(false);
    this.showEditor.set(true);
  }

  closeEditor(): void {
    this.showEditor.set(false);
  }

  onDisplayNameChange(value: string): void {
    this.displayName.set(value);
  }

  onHostChange(value: string): void {
    this.host.set(value);
  }

  onPortChange(value: string): void {
    this.port.set(this.parsePort(value));
  }

  onSecurityModeChange(value: string): void {
    this.securityMode.set(value as SmtpSecurityMode);
    this.isInsecure.set(value === 'none');
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

  onDefaultChange(checked: boolean): void {
    this.isDefault.set(checked);
  }

  togglePassword(): void {
    this.showPassword.update((visible) => !visible);
  }

  save(): void {
    this.saving.set(true);
    const dto = this.buildDto();
    const id = this.editingId();
    const request$ = id
      ? this.emailAccountApi.updateSmtp(id, dto)
      : this.emailAccountApi.createSmtp(dto);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.showEditor.set(false);
        this.notification.success(this.translate.instant('settings.saveSuccess'));
        this.loadAccounts();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  testDraftConnection(): void {
    this.testing.set(true);
    this.emailAccountApi
      .testSmtpDraft({
        ...this.buildDto(),
        id: this.editingId()
      })
      .subscribe({
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

  testAccount(id: string): void {
    this.testingId.set(id);
    this.emailAccountApi.testAccount(id).subscribe({
      next: (result) => {
        this.testingId.set(null);
        if (result.success) {
          this.notification.success(result.message);
        } else {
          this.notification.error(result.message);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.testingId.set(null);
        this.notification.error(this.extractError(err));
      }
    });
  }

  setDefault(id: string): void {
    this.emailAccountApi.setDefault(id).subscribe({
      next: () => {
        this.notification.success(this.translate.instant('settings.defaultSuccess'));
        this.loadAccounts();
      },
      error: (err: HttpErrorResponse) => this.notification.error(this.extractError(err))
    });
  }

  disconnect(id: string): void {
    this.saving.set(true);
    this.emailAccountApi.disconnect(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('settings.disconnectSuccess'));
        this.loadAccounts();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  openDeleteConfirm(id: string): void {
    this.deleteId.set(id);
    this.showDeleteConfirm.set(true);
  }

  closeDeleteConfirm(): void {
    this.showDeleteConfirm.set(false);
    this.deleteId.set(null);
  }

  deleteAccount(): void {
    const id = this.deleteId();
    if (!id) {
      return;
    }

    this.saving.set(true);
    this.emailAccountApi.delete(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.showDeleteConfirm.set(false);
        this.deleteId.set(null);
        this.notification.success(this.translate.instant('settings.deleteSuccess'));
        this.loadAccounts();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  private resolveInitialTab(): void {
    const tab = this.route.snapshot.queryParamMap.get('tab') as SettingsTabId | null;
    if (tab && this.tabs.some((item) => item.id === tab)) {
      this.activeTab.set(tab);
      return;
    }
    if (this.route.snapshot.queryParamMap.get('microsoft')) {
      this.activeTab.set('accounts');
    }
  }

  private loadAppSettings(): void {
    this.configLoading.set(true);
    this.appSettingsApi.get().subscribe({
      next: (settings) => {
        this.appSettings.set(settings);
        this.braveConnectionStatus.set(settings.braveSearch?.connectionStatus ?? 'NotConfigured');
        this.braveConnectionMessage.set(null);
        this.searXngConnectionStatus.set(settings.searXng?.connectionStatus ?? 'NotConfigured');
        this.searXngConnectionMessage.set(null);
        this.configLoading.set(false);
      },
      error: () => {
        this.appSettings.set(null);
        this.configLoading.set(false);
      }
    });
  }

  private connectMicrosoft(): void {
    if (!this.microsoftOAuthConfigured()) {
      this.notification.error(this.translate.instant('settings.microsoftNotConfigured'));
      return;
    }

    this.connecting.set(true);
    this.emailAccountApi.connectMicrosoft().subscribe({
      next: (result) => {
        this.connecting.set(false);
        window.location.href = result.authorizeUrl;
      },
      error: (err: HttpErrorResponse) => {
        this.connecting.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  private handleMicrosoftQuery(): void {
    const microsoft = this.route.snapshot.queryParamMap.get('microsoft');
    if (microsoft === 'connected') {
      this.notification.success(this.translate.instant('settings.microsoftConnected'));
      return;
    }
    if (microsoft === 'error') {
      this.notification.error(this.translate.instant('settings.microsoftError'));
    }
  }

  private loadStatus(): void {
    this.emailAccountApi.getStatus().subscribe({
      next: (status) => this.microsoftOAuthConfigured.set(status.microsoftOAuthConfigured),
      error: () => this.microsoftOAuthConfigured.set(false)
    });
  }

  private loadAccounts(): void {
    this.loading.set(true);
    this.emailAccountApi.list().subscribe({
      next: (accounts) => {
        this.accounts.set(accounts);
        this.loading.set(false);
      },
      error: () => {
        this.accounts.set([]);
        this.loading.set(false);
      }
    });
  }

  private buildDto(): UpsertSmtpAccount {
    return {
      displayName: this.displayName().trim(),
      isDefault: this.isDefault(),
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
    this.displayName.set('');
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
    this.isDefault.set(false);
    this.showPassword.set(false);
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

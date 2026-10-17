import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AttachmentItem } from '../../core/models/attachment.model';
import { EmailSignature } from '../../core/models/signature.model';
import { CampaignApiService } from '../../core/services/campaign-api.service';
import { AttachmentApiService } from '../../core/services/attachment-api.service';
import { EmailApiService } from '../../core/services/email-api.service';
import { SignatureApiService } from '../../core/services/signature-api.service';
import { TemplateApiService } from '../../core/services/template-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { CampaignHubService } from '../../core/services/campaign-hub.service';
import { formatBytes, formatDate, isValidEmail, parseRecipients } from '../../core/utils/format.utils';
import { TemplateRecipientCheckRow } from '../../core/models/template-recipient.model';
import { ConfirmDialogComponent } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { ComposeRecipientsComponent } from './recipients/recipients.component';
import { ComposeEditorComponent } from './editor/editor.component';
import { ComposeAttachmentsComponent } from './attachments/attachments.component';
import { ComposeProgressComponent } from './progress/progress.component';
import { SignatureSelectorComponent } from './signature-selector/signature-selector.component';
import { EmailAccountSelectorComponent } from './email-account-selector/email-account-selector.component';
import { EmailAccountApiService } from '../../core/services/email-account-api.service';
import { EmailAccount } from '../../core/models/email-account.model';

@Component({
  selector: 'app-compose-page',
  imports: [
    TranslatePipe,
    ConfirmDialogComponent,
    ComposeRecipientsComponent,
    ComposeEditorComponent,
    ComposeAttachmentsComponent,
    ComposeProgressComponent,
    SignatureSelectorComponent,
    EmailAccountSelectorComponent
  ],
  templateUrl: './compose.page.html',
  styleUrl: './compose.page.scss'
})
export class ComposePage implements OnInit {
  private readonly campaignApi = inject(CampaignApiService);
  private readonly attachmentApi = inject(AttachmentApiService);
  private readonly emailApi = inject(EmailApiService);
  private readonly signatureApi = inject(SignatureApiService);
  private readonly emailAccountApi = inject(EmailAccountApiService);
  private readonly templateApi = inject(TemplateApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly hub = inject(CampaignHubService);

  readonly rawRecipients = signal('');
  readonly validEmails = signal<string[]>([]);
  readonly invalidEmails = signal<string[]>([]);
  readonly subject = signal('');
  readonly htmlBody = signal('');
  readonly attachments = signal<AttachmentItem[]>([]);
  readonly delayMs = signal(1000);
  readonly batchSize = signal(50);
  readonly maxRetries = signal(2);
  readonly previewMode = signal(false);
  readonly validating = signal(false);
  readonly sending = signal(false);
  readonly uploading = signal(false);
  readonly showConfirm = signal(false);
  readonly showTestDialog = signal(false);
  readonly testEmail = signal('');
  readonly testSending = signal(false);
  readonly activeCampaignId = signal<string | null>(null);
  readonly idempotencyKey = signal('');
  readonly signatures = signal<EmailSignature[]>([]);
  readonly signaturesEnabled = signal(true);
  readonly selectedSignatureId = signal<string | null>(null);
  readonly emailAccounts = signal<EmailAccount[]>([]);
  readonly selectedEmailAccountId = signal<string | null>(null);
  readonly templateId = signal<string | null>(null);
  readonly templateName = signal<string | null>(null);
  readonly allowAlreadyContacted = signal(false);
  readonly checkingTemplateRecipients = signal(false);
  readonly templateRecipientRows = signal<TemplateRecipientCheckRow[]>([]);
  readonly templateNewCount = signal(0);
  readonly templateContactedCount = signal(0);
  readonly showContactedOnly = signal(false);

  readonly validCount = computed(() => this.validEmails().length);
  readonly invalidCount = computed(() => this.invalidEmails().length);
  readonly totalAttachmentSize = computed(() =>
    this.attachments().reduce((sum, item) => sum + item.sizeBytes, 0)
  );
  readonly totalAttachmentSizeLabel = computed(() => formatBytes(this.totalAttachmentSize()));
  readonly sendableCount = computed(() => {
    if (!this.templateId() || this.allowAlreadyContacted()) {
      return this.validCount();
    }
    return this.templateNewCount();
  });
  readonly canSend = computed(
    () =>
      this.sendableCount() > 0 &&
      this.invalidCount() === 0 &&
      this.subject().trim().length > 0 &&
      this.htmlBody().trim().length > 0 &&
      !!this.selectedEmailAccountId() &&
      !this.sending()
  );
  readonly confirmMessageParams = computed(() => ({
    count: this.sendableCount(),
    subject: this.subject()
  }));
  readonly selectedSignatureHtml = computed(() => {
    const id = this.selectedSignatureId();
    if (!id || !this.signaturesEnabled()) {
      return '';
    }
    return this.signatures().find((item) => item.id === id)?.htmlContent ?? '';
  });
  readonly hasTemplateDuplicates = computed(
    () => !!this.templateId() && this.templateContactedCount() > 0
  );
  readonly visibleTemplateRecipientRows = computed(() => {
    const rows = this.templateRecipientRows();
    if (!this.showContactedOnly()) {
      return rows;
    }
    return rows.filter((row) => row.alreadyContacted);
  });
  readonly duplicateWarningParams = computed(() => ({
    count: this.templateContactedCount(),
    total: this.validCount()
  }));

  ngOnInit(): void {
    this.idempotencyKey.set(crypto.randomUUID());
    this.loadSignatures();
    this.loadEmailAccounts();
    this.loadTemplateFromQuery();
  }

  onRecipientsChange(value: string): void {
    this.rawRecipients.set(value);
    this.validateRecipients();
  }

  onRemoveValidEmail(email: string): void {
    const remaining = this.validEmails().filter((item) => item !== email);
    this.validEmails.set(remaining);
    this.rawRecipients.set(remaining.join('\n'));
    this.checkTemplateRecipients();
  }

  removeAlreadyContacted(): void {
    const remaining = this.templateRecipientRows()
      .filter((row) => !row.alreadyContacted)
      .map((row) => row.email);
    this.validEmails.set(remaining);
    this.rawRecipients.set(remaining.join('\n'));
    this.allowAlreadyContacted.set(false);
    this.showContactedOnly.set(false);
    this.checkTemplateRecipients();
  }

  keepDuplicates(): void {
    this.allowAlreadyContacted.set(true);
  }

  useRecommendedDuplicateHandling(): void {
    this.allowAlreadyContacted.set(false);
    this.removeAlreadyContacted();
  }

  toggleContactedOnly(): void {
    this.showContactedOnly.update((value) => !value);
  }

  onSubjectChange(value: string): void {
    this.subject.set(value);
  }

  onBodyChange(value: string): void {
    this.htmlBody.set(value);
  }

  onSignatureChange(signatureId: string | null): void {
    this.selectedSignatureId.set(signatureId);
  }

  onEmailAccountChange(emailAccountId: string | null): void {
    this.selectedEmailAccountId.set(emailAccountId);
  }

  onPreviewToggle(): void {
    this.previewMode.update((mode) => !mode);
  }

  onDelayChange(value: string): void {
    this.delayMs.set(this.parseNumber(value, 1000));
  }

  onBatchSizeChange(value: string): void {
    this.batchSize.set(this.parseNumber(value, 50));
  }

  onMaxRetriesChange(value: string): void {
    this.maxRetries.set(this.parseNumber(value, 2));
  }

  onFileSelected(files: FileList | null): void {
    if (!files || files.length === 0) {
      return;
    }
    for (let i = 0; i < files.length; i++) {
      this.uploadFile(files[i]);
    }
  }

  onRemoveAttachment(id: string): void {
    const item = this.attachments().find((entry) => entry.id === id);
    this.attachments.update((items) => items.filter((entry) => entry.id !== id));
    if (!item || item.canDelete === false) {
      return;
    }
    this.attachmentApi.delete(id).subscribe({
      error: (err: HttpErrorResponse) => {
        this.notification.error(this.extractError(err));
      }
    });
  }

  clearTemplate(): void {
    this.templateId.set(null);
    this.templateName.set(null);
    this.allowAlreadyContacted.set(false);
    this.templateRecipientRows.set([]);
    this.templateNewCount.set(0);
    this.templateContactedCount.set(0);
    this.showContactedOnly.set(false);
    this.router.navigate(['/compose']);
  }

  openConfirm(): void {
    if (!this.canSend()) {
      return;
    }
    this.showConfirm.set(true);
  }

  closeConfirm(): void {
    this.showConfirm.set(false);
  }

  sendCampaign(): void {
    this.sending.set(true);
    this.campaignApi
      .createCampaign({
        subject: this.subject().trim(),
        htmlBody: this.htmlBody(),
        rawRecipients: this.rawRecipients(),
        attachmentIds: this.attachments().map((item) => item.id),
        signatureId: this.resolveSignatureId(),
        delayMilliseconds: this.delayMs(),
        batchSize: this.batchSize(),
        maxRetryCount: this.maxRetries(),
        idempotencyKey: this.idempotencyKey(),
        templateId: this.templateId(),
        allowAlreadyContacted: this.allowAlreadyContacted(),
        emailAccountId: this.selectedEmailAccountId()
      })
      .subscribe({
        next: (campaign) => {
          this.sending.set(false);
          this.showConfirm.set(false);
          this.notification.success(this.translate.instant('compose.sendSuccess'));
          this.activeCampaignId.set(campaign.id);
          this.hub.joinCampaign(campaign.id);
        },
        error: (err: HttpErrorResponse) => {
          this.sending.set(false);
          this.notification.error(this.extractError(err));
        }
      });
  }

  openTestDialog(): void {
    this.testEmail.set('');
    this.showTestDialog.set(true);
  }

  closeTestDialog(): void {
    this.showTestDialog.set(false);
  }

  onTestEmailChange(value: string): void {
    this.testEmail.set(value);
  }

  sendTestEmail(): void {
    const email = this.testEmail().trim();
    if (!isValidEmail(email)) {
      this.notification.error(this.translate.instant('compose.invalidTestEmail'));
      return;
    }
    this.testSending.set(true);
    this.emailApi
      .sendTestEmail({
        toEmail: email,
        subject: this.resolveTestSubject(),
        htmlBody: this.resolveTestBody(),
        attachmentIds: this.attachments().map((item) => item.id),
        signatureId: this.resolveSignatureId(),
        emailAccountId: this.selectedEmailAccountId()
      })
      .subscribe({
        next: (result) => {
          this.testSending.set(false);
          if (result.success) {
            this.notification.success(result.message);
            this.showTestDialog.set(false);
          } else {
            this.notification.error(result.message);
          }
        },
        error: (err: HttpErrorResponse) => {
          this.testSending.set(false);
          this.notification.error(this.extractError(err));
        }
      });
  }

  goToDetail(): void {
    const id = this.activeCampaignId();
    if (!id) {
      return;
    }
    this.activeCampaignId.set(null);
    this.hub.leaveCampaign();
    this.router.navigate(['/history', id]);
  }

  dismissProgress(): void {
    this.activeCampaignId.set(null);
    this.hub.leaveCampaign();
    this.idempotencyKey.set(crypto.randomUUID());
  }

  private validateRecipients(): void {
    const parsed = parseRecipients(this.rawRecipients());
    const valid: string[] = [];
    const invalid: string[] = [];
    for (const email of parsed) {
      if (isValidEmail(email)) {
        valid.push(email);
      } else {
        invalid.push(email);
      }
    }
    this.validEmails.set(valid);
    this.invalidEmails.set(invalid);
    if (this.rawRecipients().trim().length === 0) {
      return;
    }
    this.validating.set(true);
    this.campaignApi.validateRecipients({ rawRecipients: this.rawRecipients() }).subscribe({
      next: (response) => {
        this.validEmails.set(response.validEmails);
        this.invalidEmails.set(response.invalidEmails);
        this.validating.set(false);
        this.checkTemplateRecipients();
      },
      error: () => {
        this.validating.set(false);
      }
    });
  }

  private checkTemplateRecipients(): void {
    const templateId = this.templateId();
    if (!templateId || this.validEmails().length === 0) {
      this.templateRecipientRows.set([]);
      this.templateNewCount.set(0);
      this.templateContactedCount.set(0);
      return;
    }

    this.checkingTemplateRecipients.set(true);
    this.templateApi.checkRecipients(templateId, this.rawRecipients()).subscribe({
      next: (response) => {
        const rows: TemplateRecipientCheckRow[] = [];
        for (const item of response.recipients) {
          rows.push({
            email: item.email,
            alreadyContacted: item.alreadyContacted,
            statusLabelKey: item.alreadyContacted
              ? 'compose.recipientAlreadyContacted'
              : 'compose.recipientNew',
            previousSendLabel: item.alreadyContacted
              ? formatDate(item.previousSentAtUtc)
              : this.translate.instant('compose.noPreviousSend')
          });
        }
        this.templateRecipientRows.set(rows);
        this.templateNewCount.set(response.newCount);
        this.templateContactedCount.set(response.alreadyContactedCount);
        this.checkingTemplateRecipients.set(false);
      },
      error: () => {
        this.checkingTemplateRecipients.set(false);
      }
    });
  }

  private uploadFile(file: File): void {
    this.uploading.set(true);
    this.attachmentApi.upload(file).subscribe({
      next: (result) => {
        this.attachments.update((items) => [
          ...items,
          { ...result, file, canDelete: true }
        ]);
        this.uploading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.uploading.set(false);
        this.notification.error(this.extractError(err));
      }
    });
  }

  private loadTemplateFromQuery(): void {
    const templateId = this.route.snapshot.queryParamMap.get('templateId');
    if (!templateId) {
      return;
    }
    this.templateId.set(templateId);
    this.templateApi.get(templateId).subscribe({
      next: (template) => {
        this.templateId.set(template.id);
        this.templateName.set(template.name);
        this.subject.set(template.subject);
        this.htmlBody.set(template.htmlBody);
        this.selectedSignatureId.set(template.signatureId);
        if (template.emailAccountId) {
          this.selectedEmailAccountId.set(template.emailAccountId);
        }
        const items: AttachmentItem[] = [];
        for (const file of template.attachments) {
          items.push({
            id: file.id,
            fileName: file.fileName,
            contentType: file.contentType,
            sizeBytes: file.sizeBytes,
            canDelete: false
          });
        }
        this.attachments.set(items);
      },
      error: (err: HttpErrorResponse) => {
        this.notification.error(this.extractError(err));
      }
    });
  }

  private parseNumber(value: string, fallback: number): number {
    const parsed = Number(value);
    if (Number.isNaN(parsed) || parsed < 0) {
      return fallback;
    }
    return parsed;
  }

  private resolveTestSubject(): string {
    const subject = this.subject().trim();
    if (subject.length > 0) {
      return subject;
    }
    return this.translate.instant('compose.testSubjectDefault');
  }

  private resolveTestBody(): string {
    const body = this.htmlBody().trim();
    if (body.length > 0) {
      return body;
    }
    return `<p>${this.translate.instant('compose.testBodyDefault')}</p>`;
  }

  private resolveSignatureId(): string | null {
    if (!this.signaturesEnabled()) {
      return null;
    }
    return this.selectedSignatureId();
  }

  private loadSignatures(): void {
    this.signatureApi.getSettings().subscribe({
      next: (settings) => {
        this.signaturesEnabled.set(settings.signaturesEnabled);
        this.signatureApi.list().subscribe({
          next: (items) => {
            this.signatures.set(items);
            this.applyDefaultSignature(items, settings.signaturesEnabled);
          },
          error: () => this.signatures.set([])
        });
      },
      error: () => {
        this.signaturesEnabled.set(true);
        this.signatures.set([]);
      }
    });
  }

  private loadEmailAccounts(): void {
    this.emailAccountApi.list().subscribe({
      next: (accounts) => {
        this.emailAccounts.set(accounts);
        this.applyDefaultEmailAccount(accounts);
      },
      error: () => this.emailAccounts.set([])
    });
  }

  private applyDefaultEmailAccount(accounts: EmailAccount[]): void {
    if (this.selectedEmailAccountId()) {
      return;
    }
    const connected = accounts.filter((item) => item.isConnected);
    const preferred = connected.find((item) => item.isDefault) ?? connected[0] ?? null;
    this.selectedEmailAccountId.set(preferred?.id ?? null);
  }

  private applyDefaultSignature(items: EmailSignature[], enabled: boolean): void {
    if (this.templateId()) {
      return;
    }
    if (!enabled) {
      this.selectedSignatureId.set(null);
      return;
    }
    const defaultSignature = items.find((item) => item.isDefault && item.isActive);
    this.selectedSignatureId.set(defaultSignature?.id ?? null);
  }

  private extractError(err: HttpErrorResponse): string {
    if (err.error?.message) {
      return err.error.message;
    }
    return this.translate.instant('common.errorGeneric');
  }
}

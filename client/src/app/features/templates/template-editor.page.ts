import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AttachmentItem } from '../../core/models/attachment.model';
import { EmailSignature } from '../../core/models/signature.model';
import { AttachmentApiService } from '../../core/services/attachment-api.service';
import { SignatureApiService } from '../../core/services/signature-api.service';
import { TemplateApiService } from '../../core/services/template-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { formatBytes } from '../../core/utils/format.utils';
import { ComposeEditorComponent } from '../compose/editor/editor.component';
import { ComposeAttachmentsComponent } from '../compose/attachments/attachments.component';
import { SignatureSelectorComponent } from '../compose/signature-selector/signature-selector.component';
import { EmailAccountSelectorComponent } from '../compose/email-account-selector/email-account-selector.component';
import { EmailAccountApiService } from '../../core/services/email-account-api.service';
import { EmailAccount } from '../../core/models/email-account.model';
import { LoadingComponent } from '../../shared/components/loading/loading.component';

@Component({
  selector: 'app-template-editor-page',
  imports: [
    TranslatePipe,
    LoadingComponent,
    ComposeEditorComponent,
    ComposeAttachmentsComponent,
    SignatureSelectorComponent,
    EmailAccountSelectorComponent
  ],
  templateUrl: './template-editor.page.html',
  styleUrl: './template-editor.page.scss'
})
export class TemplateEditorPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly templateApi = inject(TemplateApiService);
  private readonly attachmentApi = inject(AttachmentApiService);
  private readonly signatureApi = inject(SignatureApiService);
  private readonly emailAccountApi = inject(EmailAccountApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly uploading = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly name = signal('');
  readonly description = signal('');
  readonly subject = signal('');
  readonly htmlBody = signal('');
  readonly isActive = signal(true);
  readonly previewMode = signal(false);
  readonly attachments = signal<AttachmentItem[]>([]);
  readonly signatures = signal<EmailSignature[]>([]);
  readonly signaturesEnabled = signal(true);
  readonly selectedSignatureId = signal<string | null>(null);
  readonly emailAccounts = signal<EmailAccount[]>([]);
  readonly selectedEmailAccountId = signal<string | null>(null);
  readonly whatsAppTemplateName = signal('');
  readonly whatsAppTemplateLanguage = signal('');
  readonly whatsAppHeaderParameter = signal('');
  readonly whatsAppButtonSubType = signal('');
  readonly whatsAppButtonIndex = signal('');
  readonly whatsAppButtonParameter = signal('');

  readonly pageTitleKey = computed(() =>
    this.editingId() ? 'templates.editTitle' : 'templates.createTitle'
  );
  readonly totalAttachmentSize = computed(() =>
    this.attachments().reduce((sum, item) => sum + item.sizeBytes, 0)
  );
  readonly totalAttachmentSizeLabel = computed(() => formatBytes(this.totalAttachmentSize()));
  readonly selectedSignatureHtml = computed(() => {
    const id = this.selectedSignatureId();
    if (!id) {
      return '';
    }
    return this.signatures().find((item) => item.id === id)?.htmlContent ?? '';
  });
  readonly canSave = computed(
    () =>
      this.name().trim().length > 0 &&
      this.subject().trim().length > 0 &&
      this.htmlBody().trim().length > 0 &&
      !this.saving()
  );

  ngOnInit(): void {
    this.loadSignatures();
    this.loadEmailAccounts();
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.editingId.set(id);
      this.loadTemplate(id);
      return;
    }
    this.loading.set(false);
  }

  onNameChange(value: string): void {
    this.name.set(value);
  }

  onDescriptionChange(value: string): void {
    this.description.set(value);
  }

  onSubjectChange(value: string): void {
    this.subject.set(value);
  }

  onBodyChange(value: string): void {
    this.htmlBody.set(value);
  }

  onPreviewToggle(): void {
    this.previewMode.update((mode) => !mode);
  }

  onSignatureChange(signatureId: string | null): void {
    this.selectedSignatureId.set(signatureId);
  }

  onEmailAccountChange(emailAccountId: string | null): void {
    this.selectedEmailAccountId.set(emailAccountId);
  }

  onActiveChange(checked: boolean): void {
    this.isActive.set(checked);
  }

  onWhatsAppTemplateNameChange(value: string): void {
    this.whatsAppTemplateName.set(value);
  }

  onWhatsAppTemplateLanguageChange(value: string): void {
    this.whatsAppTemplateLanguage.set(value);
  }

  onWhatsAppHeaderParameterChange(value: string): void {
    this.whatsAppHeaderParameter.set(value);
  }

  onWhatsAppButtonSubTypeChange(value: string): void {
    this.whatsAppButtonSubType.set(value);
  }

  onWhatsAppButtonIndexChange(value: string): void {
    this.whatsAppButtonIndex.set(value);
  }

  onWhatsAppButtonParameterChange(value: string): void {
    this.whatsAppButtonParameter.set(value);
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
    this.attachmentApi.delete(id).subscribe({
      next: () => {
        this.attachments.update((items) => items.filter((item) => item.id !== id));
      },
      error: (err: HttpErrorResponse) => this.notifyError(err)
    });
  }

  cancel(): void {
    this.router.navigate(['/templates']);
  }

  save(): void {
    if (!this.canSave()) {
      this.notification.error(this.translate.instant('templates.validationRequired'));
      return;
    }

    const payload = {
      name: this.name().trim(),
      description: this.description().trim() || null,
      subject: this.subject().trim(),
      htmlBody: this.htmlBody(),
      signatureId: this.selectedSignatureId(),
      emailAccountId: this.selectedEmailAccountId(),
      category: null,
      isActive: this.isActive(),
      attachmentIds: this.attachments().map((item) => item.id),
      whatsAppTemplateName: this.whatsAppTemplateName().trim() || null,
      whatsAppTemplateLanguage: this.whatsAppTemplateLanguage().trim() || null,
      whatsAppHeaderParameter: this.whatsAppHeaderParameter().trim() || null,
      whatsAppButtonSubType: this.whatsAppButtonSubType().trim() || null,
      whatsAppButtonIndex: this.parseOptionalInt(this.whatsAppButtonIndex()),
      whatsAppButtonParameter: this.whatsAppButtonParameter().trim() || null
    };

    this.saving.set(true);
    const editingId = this.editingId();
    const request$ = editingId
      ? this.templateApi.update(editingId, payload)
      : this.templateApi.create(payload);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.notification.success(this.translate.instant('templates.saveSuccess'));
        this.router.navigate(['/templates']);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadTemplate(id: string): void {
    this.templateApi.get(id).subscribe({
      next: (template) => {
        this.name.set(template.name);
        this.description.set(template.description ?? '');
        this.subject.set(template.subject);
        this.htmlBody.set(template.htmlBody);
        this.isActive.set(template.isActive);
        this.selectedSignatureId.set(template.signatureId);
        this.selectedEmailAccountId.set(template.emailAccountId);
        this.whatsAppTemplateName.set(template.whatsAppTemplateName ?? '');
        this.whatsAppTemplateLanguage.set(template.whatsAppTemplateLanguage ?? '');
        this.whatsAppHeaderParameter.set(template.whatsAppHeaderParameter ?? '');
        this.whatsAppButtonSubType.set(template.whatsAppButtonSubType ?? '');
        this.whatsAppButtonIndex.set(
          template.whatsAppButtonIndex === null || template.whatsAppButtonIndex === undefined
            ? ''
            : String(template.whatsAppButtonIndex)
        );
        this.whatsAppButtonParameter.set(template.whatsAppButtonParameter ?? '');
        const items: AttachmentItem[] = [];
        for (const file of template.attachments) {
          items.push({
            id: file.id,
            fileName: file.fileName,
            contentType: file.contentType,
            sizeBytes: file.sizeBytes,
            canDelete: true
          });
        }
        this.attachments.set(items);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notifyError(err);
        this.router.navigate(['/templates']);
      }
    });
  }

  private loadSignatures(): void {
    this.signatureApi.getSettings().subscribe({
      next: (settings) => {
        this.signaturesEnabled.set(settings.signaturesEnabled);
        this.signatureApi.list().subscribe({
          next: (items) => this.signatures.set(items),
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
      next: (accounts) => this.emailAccounts.set(accounts),
      error: () => this.emailAccounts.set([])
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
        this.notifyError(err);
      }
    });
  }

  private parseOptionalInt(value: string): number | null {
    const trimmed = value.trim();
    if (!trimmed) {
      return null;
    }
    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed)) {
      return null;
    }
    return Math.max(0, Math.floor(parsed));
  }

  private notifyError(err: HttpErrorResponse): void {
    if (err.status === 0) {
      this.notification.error(this.translate.instant('common.apiOffline'));
      return;
    }
    if (err.error?.message) {
      this.notification.error(err.error.message);
      return;
    }
    this.notification.error(this.translate.instant('common.errorGeneric'));
  }
}

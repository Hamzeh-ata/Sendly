import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { EmailSignature } from '../../../core/models/signature.model';
import { SignatureApiService } from '../../../core/services/signature-api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { LoadingComponent } from '../../../shared/components/loading/loading.component';
import { SignatureEditorComponent } from './signature-editor.component';

@Component({
  selector: 'app-signatures-manager',
  imports: [
    TranslatePipe,
    LoadingComponent,
    EmptyStateComponent,
    ConfirmDialogComponent,
    SignatureEditorComponent
  ],
  templateUrl: './signatures-manager.component.html',
  styleUrl: './signatures-manager.component.scss'
})
export class SignaturesManagerComponent implements OnInit {
  private readonly signatureApi = inject(SignatureApiService);
  private readonly notification = inject(NotificationService);
  private readonly translate = inject(TranslateService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly signaturesEnabled = signal(true);
  readonly signatures = signal<EmailSignature[]>([]);
  readonly editingId = signal<string | null>(null);
  readonly showEditor = signal(false);
  readonly previewMode = signal(false);
  readonly name = signal('');
  readonly htmlContent = signal('');
  readonly isDefault = signal(false);
  readonly isActive = signal(true);
  readonly deleteId = signal<string | null>(null);

  readonly editorTitleKey = computed(() =>
    this.editingId() ? 'signatures.editTitle' : 'signatures.createTitle'
  );

  ngOnInit(): void {
    this.loadAll();
  }

  onToggleEnabled(checked: boolean): void {
    this.signaturesEnabled.set(checked);
    this.signatureApi.updateSettings({ signaturesEnabled: checked }).subscribe({
      next: (settings) => {
        this.signaturesEnabled.set(settings.signaturesEnabled);
        this.notification.success(this.translate.instant('signatures.settingsSaved'));
      },
      error: (err: HttpErrorResponse) => this.notifyError(err)
    });
  }

  openCreate(): void {
    this.editingId.set(null);
    this.name.set('');
    this.htmlContent.set('<p></p>');
    this.isDefault.set(false);
    this.isActive.set(true);
    this.previewMode.set(false);
    this.showEditor.set(true);
  }

  openEdit(signature: EmailSignature): void {
    this.editingId.set(signature.id);
    this.name.set(signature.name);
    this.htmlContent.set(signature.htmlContent);
    this.isDefault.set(signature.isDefault);
    this.isActive.set(signature.isActive);
    this.previewMode.set(false);
    this.showEditor.set(true);
  }

  closeEditor(): void {
    this.showEditor.set(false);
  }

  onNameChange(value: string): void {
    this.name.set(value);
  }

  onContentChange(value: string): void {
    this.htmlContent.set(value);
  }

  onPreviewToggle(): void {
    this.previewMode.update((value) => !value);
  }

  onDefaultChange(checked: boolean): void {
    this.isDefault.set(checked);
  }

  onActiveChange(checked: boolean): void {
    this.isActive.set(checked);
  }

  saveSignature(): void {
    const name = this.name().trim();
    const htmlContent = this.htmlContent().trim();
    if (!name || !htmlContent) {
      this.notification.error(this.translate.instant('signatures.validationRequired'));
      return;
    }

    const payload = {
      name,
      htmlContent,
      isDefault: this.isDefault(),
      isActive: this.isActive()
    };

    this.saving.set(true);
    const editingId = this.editingId();
    const request$ = editingId
      ? this.signatureApi.update(editingId, payload)
      : this.signatureApi.create(payload);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.showEditor.set(false);
        this.notification.success(this.translate.instant('signatures.saveSuccess'));
        this.loadSignatures();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  duplicate(id: string): void {
    this.signatureApi.duplicate(id).subscribe({
      next: () => {
        this.notification.success(this.translate.instant('signatures.duplicateSuccess'));
        this.loadSignatures();
      },
      error: (err: HttpErrorResponse) => this.notifyError(err)
    });
  }

  setDefault(id: string): void {
    this.signatureApi.setDefault(id).subscribe({
      next: () => {
        this.notification.success(this.translate.instant('signatures.defaultSuccess'));
        this.loadSignatures();
      },
      error: (err: HttpErrorResponse) => this.notifyError(err)
    });
  }

  openDelete(id: string): void {
    this.deleteId.set(id);
  }

  closeDelete(): void {
    this.deleteId.set(null);
  }

  confirmDelete(): void {
    const id = this.deleteId();
    if (!id) {
      return;
    }
    this.saving.set(true);
    this.signatureApi.delete(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleteId.set(null);
        this.notification.success(this.translate.instant('signatures.deleteSuccess'));
        this.loadSignatures();
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.notifyError(err);
      }
    });
  }

  private loadAll(): void {
    this.loading.set(true);
    this.signatureApi.getSettings().subscribe({
      next: (settings) => {
        this.signaturesEnabled.set(settings.signaturesEnabled);
        this.loadSignatures();
      },
      error: () => {
        this.loading.set(false);
      }
    });
  }

  private loadSignatures(): void {
    this.signatureApi.list().subscribe({
      next: (items) => {
        this.signatures.set(items);
        this.loading.set(false);
      },
      error: () => {
        this.signatures.set([]);
        this.loading.set(false);
      }
    });
  }

  private notifyError(err: HttpErrorResponse): void {
    if (err.error?.message) {
      this.notification.error(err.error.message);
      return;
    }
    this.notification.error(this.translate.instant('common.errorGeneric'));
  }
}

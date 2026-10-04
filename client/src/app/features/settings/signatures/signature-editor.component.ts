import { Component, ElementRef, inject, input, output, viewChild } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';

@Component({
  selector: 'app-signature-editor',
  imports: [TranslatePipe],
  templateUrl: './signature-editor.component.html',
  styleUrl: './signature-editor.component.scss'
})
export class SignatureEditorComponent {
  private readonly translate = inject(TranslateService);

  readonly htmlContent = input('');
  readonly previewMode = input(false);

  readonly contentChange = output<string>();
  readonly previewToggle = output<void>();

  readonly editorRef = viewChild<ElementRef<HTMLDivElement>>('editor');

  execCommand(command: string, value?: string): void {
    document.execCommand(command, false, value);
    this.syncContent();
  }

  insertWebsiteLink(): void {
    this.insertLink(this.translate.instant('signatures.promptWebsite'), 'https://');
  }

  insertEmailLink(): void {
    const email = window.prompt(this.translate.instant('signatures.promptEmail'), '');
    if (!email) {
      return;
    }
    document.execCommand('createLink', false, `mailto:${email.trim()}`);
    this.syncContent();
  }

  insertPhoneLink(): void {
    const phone = window.prompt(this.translate.instant('signatures.promptPhone'), '');
    if (!phone) {
      return;
    }
    document.execCommand('createLink', false, `tel:${phone.trim()}`);
    this.syncContent();
  }

  insertWhatsAppLink(): void {
    const phone = window.prompt(this.translate.instant('signatures.promptWhatsApp'), '');
    if (!phone) {
      return;
    }
    const digits = phone.replace(/\D/g, '');
    document.execCommand('createLink', false, `https://wa.me/${digits}`);
    this.syncContent();
  }

  insertImage(): void {
    const url = window.prompt(this.translate.instant('signatures.promptImage'), 'https://');
    if (!url) {
      return;
    }
    document.execCommand('insertImage', false, url.trim());
    this.syncContent();
  }

  onInput(): void {
    this.syncContent();
  }

  onTogglePreview(): void {
    this.previewToggle.emit();
  }

  private insertLink(promptText: string, defaultValue: string): void {
    const url = window.prompt(promptText, defaultValue);
    if (!url) {
      return;
    }
    document.execCommand('createLink', false, url.trim());
    this.syncContent();
  }

  private syncContent(): void {
    const el = this.editorRef()?.nativeElement;
    if (el) {
      this.contentChange.emit(el.innerHTML);
    }
  }
}

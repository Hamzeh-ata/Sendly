import { Component, ElementRef, input, output, viewChild } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-compose-editor',
  imports: [TranslatePipe],
  templateUrl: './editor.component.html',
  styleUrl: './editor.component.scss'
})
export class ComposeEditorComponent {
  readonly htmlBody = input('');
  readonly previewMode = input(false);

  readonly bodyChange = output<string>();
  readonly previewToggle = output<void>();

  readonly editorRef = viewChild<ElementRef<HTMLDivElement>>('editor');

  execCommand(command: string, value?: string): void {
    document.execCommand(command, false, value);
    this.syncBody();
  }

  insertLink(): void {
    const url = prompt('URL');
    if (url) {
      document.execCommand('createLink', false, url);
      this.syncBody();
    }
  }

  onInput(): void {
    this.syncBody();
  }

  onTogglePreview(): void {
    this.previewToggle.emit();
  }

  private syncBody(): void {
    const el = this.editorRef()?.nativeElement;
    if (el) {
      this.bodyChange.emit(el.innerHTML);
    }
  }
}

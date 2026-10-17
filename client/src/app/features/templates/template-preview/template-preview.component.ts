import { Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { TemplatePreviewView } from '../../../core/models/template-preview-view.model';

@Component({
  selector: 'app-template-preview',
  imports: [TranslatePipe],
  templateUrl: './template-preview.component.html',
  styleUrl: './template-preview.component.scss'
})
export class TemplatePreviewComponent {
  readonly visible = input(false);
  readonly preview = input<TemplatePreviewView | null>(null);
  readonly loading = input(false);

  readonly closed = output<void>();

  onClose(): void {
    this.closed.emit();
  }
}

import { Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-confirm-dialog',
  imports: [TranslatePipe],
  templateUrl: './confirm-dialog.component.html',
  styleUrl: './confirm-dialog.component.scss'
})
export class ConfirmDialogComponent {
  readonly visible = input(false);
  readonly titleKey = input.required<string>();
  readonly messageKey = input.required<string>();
  readonly messageParams = input<Record<string, unknown>>({});
  readonly confirmKey = input('common.confirm');
  readonly cancelKey = input('common.cancel');
  readonly confirmVariant = input<'primary' | 'danger'>('primary');
  readonly loading = input(false);

  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  onConfirm(): void {
    this.confirmed.emit();
  }

  onCancel(): void {
    this.cancelled.emit();
  }
}

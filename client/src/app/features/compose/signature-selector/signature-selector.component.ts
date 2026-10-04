import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { EmailSignature } from '../../../core/models/signature.model';

@Component({
  selector: 'app-signature-selector',
  imports: [TranslatePipe, RouterLink],
  templateUrl: './signature-selector.component.html',
  styleUrl: './signature-selector.component.scss'
})
export class SignatureSelectorComponent {
  readonly signatures = input<EmailSignature[]>([]);
  readonly selectedId = input<string | null>(null);
  readonly signaturesEnabled = input(true);

  readonly selectionChange = output<string | null>();

  readonly selectedSignature = computed(() => {
    const id = this.selectedId();
    if (!id) {
      return null;
    }
    return this.signatures().find((item) => item.id === id) ?? null;
  });

  readonly includeHintVisible = computed(
    () => this.signaturesEnabled() && !!this.selectedSignature()
  );

  readonly optionList = computed(() => {
    const items = this.signatures().filter((item) => item.isActive);
    return items;
  });

  onSelect(value: string): void {
    if (!value) {
      this.selectionChange.emit(null);
      return;
    }
    this.selectionChange.emit(value);
  }
}

import { Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-compose-recipients',
  imports: [TranslatePipe],
  templateUrl: './recipients.component.html',
  styleUrl: './recipients.component.scss'
})
export class ComposeRecipientsComponent {
  readonly rawRecipients = input('');
  readonly validEmails = input<string[]>([]);
  readonly invalidEmails = input<string[]>([]);
  readonly validCount = input(0);
  readonly invalidCount = input(0);
  readonly validating = input(false);

  readonly recipientsChange = output<string>();
  readonly removeValid = output<string>();

  onInput(event: Event): void {
    const target = event.target as HTMLTextAreaElement;
    this.recipientsChange.emit(target.value);
  }

  onRemove(email: string): void {
    this.removeValid.emit(email);
  }
}

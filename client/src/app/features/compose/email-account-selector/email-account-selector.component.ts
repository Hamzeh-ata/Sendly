import { Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { EmailAccount } from '../../../core/models/email-account.model';
import { EmailAccountOption } from '../../../core/models/email-account-option.model';

@Component({
  selector: 'app-email-account-selector',
  imports: [TranslatePipe, RouterLink],
  templateUrl: './email-account-selector.component.html',
  styleUrl: './email-account-selector.component.scss'
})
export class EmailAccountSelectorComponent {
  private readonly translate = inject(TranslateService);

  readonly accounts = input<EmailAccount[]>([]);
  readonly selectedId = input<string | null>(null);
  readonly labelKey = input('compose.sendFrom');
  readonly allowNone = input(false);
  readonly noneLabelKey = input('templates.noEmailAccount');
  readonly requireConnected = input(true);

  readonly selectionChange = output<string | null>();

  readonly selectedAccount = computed(() => {
    const id = this.selectedId();
    if (!id) {
      return null;
    }
    return this.accounts().find((item) => item.id === id) ?? null;
  });

  readonly optionList = computed(() => {
    const defaultLabel = this.translate.instant('compose.accountDefault');
    const smtpLabel = this.translate.instant('provider.smtp');
    const microsoftLabel = this.translate.instant('provider.microsoftGraph');
    const options: EmailAccountOption[] = [];
    for (const account of this.accounts()) {
      if (this.requireConnected() && !account.isConnected) {
        continue;
      }
      const providerLabel =
        account.provider === 'microsoftGraph' ? microsoftLabel : smtpLabel;
      const email = account.emailAddress || account.displayName;
      const baseLabel = `${email} — ${providerLabel}`;
      options.push({
        id: account.id,
        label: account.isDefault ? `${baseLabel} (${defaultLabel})` : baseLabel
      });
    }
    return options;
  });

  readonly hasAccounts = computed(() => this.optionList().length > 0);

  readonly selectedSummary = computed(() => {
    const account = this.selectedAccount();
    if (!account) {
      return '';
    }
    return account.displayName;
  });

  onSelect(value: string): void {
    if (!value) {
      this.selectionChange.emit(null);
      return;
    }
    this.selectionChange.emit(value);
  }
}

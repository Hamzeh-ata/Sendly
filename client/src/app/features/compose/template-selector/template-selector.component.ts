import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { TemplateOption } from '../../../core/models/template-option.model';
import { EmailTemplateSummary } from '../../../core/models/template.model';

@Component({
  selector: 'app-template-selector',
  imports: [TranslatePipe, RouterLink],
  templateUrl: './template-selector.component.html',
  styleUrl: './template-selector.component.scss'
})
export class TemplateSelectorComponent {
  readonly templates = input<EmailTemplateSummary[]>([]);
  readonly selectedId = input<string | null>(null);
  readonly loading = input(false);

  readonly selectionChange = output<string | null>();

  readonly optionList = computed(() => {
    const options: TemplateOption[] = [];
    for (const template of this.templates()) {
      if (!template.isActive) {
        continue;
      }
      options.push({ id: template.id, label: template.name });
    }
    return options;
  });

  readonly hasTemplates = computed(() => this.optionList().length > 0);

  onSelect(value: string): void {
    if (!value) {
      this.selectionChange.emit(null);
      return;
    }
    this.selectionChange.emit(value);
  }
}

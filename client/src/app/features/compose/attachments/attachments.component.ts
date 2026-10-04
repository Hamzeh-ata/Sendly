import { Component, computed, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AttachmentItem } from '../../../core/models/attachment.model';
import { formatBytes } from '../../../core/utils/format.utils';

@Component({
  selector: 'app-compose-attachments',
  imports: [TranslatePipe],
  templateUrl: './attachments.component.html',
  styleUrl: './attachments.component.scss'
})
export class ComposeAttachmentsComponent {
  readonly attachments = input<AttachmentItem[]>([]);
  readonly totalSizeLabel = input('');
  readonly uploading = input(false);

  readonly fileSelected = output<FileList | null>();
  readonly remove = output<string>();

  readonly displayItems = computed(() =>
    this.attachments().map((item) => ({
      ...item,
      sizeLabel: formatBytes(item.sizeBytes)
    }))
  );

  onFileChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.fileSelected.emit(input.files);
    input.value = '';
  }

  onRemove(id: string): void {
    this.remove.emit(id);
  }
}

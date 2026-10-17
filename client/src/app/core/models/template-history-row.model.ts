import { TemplateRecipientHistoryItem } from './template-recipient.model';

export interface TemplateHistoryRow extends TemplateRecipientHistoryItem {
  statusLabelKey: string;
  sentLabel: string;
  campaignLabel: string;
}

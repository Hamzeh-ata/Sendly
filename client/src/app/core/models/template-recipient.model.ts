export type TemplateRecipientContactStatus = 'claiming' | 'sent' | 'failed';

export interface TemplateRecipientCheckItem {
  email: string;
  alreadyContacted: boolean;
  previousSentAtUtc: string | null;
  previousCampaignId: string | null;
}

export interface CheckTemplateRecipientsResponse {
  totalCount: number;
  newCount: number;
  alreadyContactedCount: number;
  recipients: TemplateRecipientCheckItem[];
}

export interface TemplateRecipientHistoryItem {
  id: string;
  email: string;
  status: TemplateRecipientContactStatus;
  sentAtUtc: string | null;
  updatedAtUtc: string;
  campaignId: string;
  campaignSubject: string | null;
  failureReason: string | null;
  attemptCount: number;
}

export interface TemplateStats {
  totalCampaigns: number;
  uniqueRecipients: number;
  successfullyContacted: number;
  failed: number;
  lastUsedAtUtc: string | null;
}

export interface TemplateRecipientCheckRow {
  email: string;
  alreadyContacted: boolean;
  statusLabelKey: string;
  previousSendLabel: string;
}

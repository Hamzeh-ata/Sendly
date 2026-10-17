import { CampaignAttachment } from './attachment.model';
import { EmailProvider } from './email-account.model';

export type CampaignStatus =
  | 'draft'
  | 'queued'
  | 'running'
  | 'completed'
  | 'completedWithFailures'
  | 'cancelled'
  | 'failed';

export type RecipientDeliveryStatus =
  | 'pending'
  | 'sending'
  | 'sent'
  | 'failed'
  | 'cancelled';

export type RecipientFilter = 'all' | RecipientDeliveryStatus;

export interface ValidateRecipientsRequest {
  rawRecipients: string;
}

export interface ValidateRecipientsResponse {
  validEmails: string[];
  invalidEmails: string[];
  validCount: number;
  invalidCount: number;
}

export interface CreateCampaignRequest {
  subject: string;
  htmlBody: string;
  rawRecipients: string;
  attachmentIds: string[];
  signatureId: string | null;
  delayMilliseconds: number | null;
  batchSize: number | null;
  maxRetryCount: number | null;
  idempotencyKey: string | null;
  templateId: string | null;
  allowAlreadyContacted: boolean;
  emailAccountId: string | null;
}

export interface TestEmailRequest {
  toEmail: string;
  subject: string;
  htmlBody: string;
  attachmentIds: string[];
  signatureId: string | null;
  emailAccountId: string | null;
}

export interface CampaignSummary {
  id: string;
  subject: string;
  status: CampaignStatus;
  totalRecipients: number;
  successCount: number;
  failedCount: number;
  cancelledCount: number;
  pendingCount: number;
  progressPercent: number;
  currentRecipientEmail: string | null;
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  templateId: string | null;
  templateName: string | null;
  emailAccountId: string | null;
  provider: EmailProvider | null;
  sendingAccountName: string | null;
  sendingAccountEmail: string | null;
}

export interface CampaignDetail extends CampaignSummary {
  htmlBody: string;
  attachments: CampaignAttachment[];
}

export interface CampaignRecipient {
  id: string;
  email: string;
  status: RecipientDeliveryStatus;
  attemptCount: number;
  lastAttemptAtUtc: string | null;
  failureReason: string | null;
}

export interface CampaignProgress {
  campaignId: string;
  status: CampaignStatus;
  totalRecipients: number;
  successCount: number;
  failedCount: number;
  cancelledCount: number;
  pendingCount: number;
  progressPercent: number;
  currentRecipientEmail: string | null;
  updatedRecipient: CampaignRecipient | null;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
}

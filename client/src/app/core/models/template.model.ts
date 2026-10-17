import { CampaignAttachment } from './attachment.model';

export type TemplateStatusFilter = 'all' | 'active' | 'inactive';

export interface EmailTemplateSummary {
  id: string;
  name: string;
  description: string | null;
  subject: string;
  signatureId: string | null;
  signatureName: string | null;
  emailAccountId: string | null;
  emailAccountName: string | null;
  emailAccountAddress: string | null;
  category: string | null;
  isActive: boolean;
  attachmentCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
  totalCampaigns: number;
  uniqueRecipients: number;
  successfullyContacted: number;
  failedRecipients: number;
  lastUsedAtUtc: string | null;
  whatsAppTemplateName?: string | null;
  whatsAppTemplateLanguage?: string | null;
  whatsAppHeaderParameter?: string | null;
  whatsAppButtonSubType?: string | null;
  whatsAppButtonIndex?: number | null;
  whatsAppButtonParameter?: string | null;
}

export interface EmailTemplateDetail extends EmailTemplateSummary {
  htmlBody: string;
  signatureHtml: string | null;
  attachments: CampaignAttachment[];
}

export interface UpsertEmailTemplate {
  name: string;
  description: string | null;
  subject: string;
  htmlBody: string;
  signatureId: string | null;
  emailAccountId: string | null;
  category: string | null;
  isActive: boolean;
  attachmentIds: string[];
  whatsAppTemplateName?: string | null;
  whatsAppTemplateLanguage?: string | null;
  whatsAppHeaderParameter?: string | null;
  whatsAppButtonSubType?: string | null;
  whatsAppButtonIndex?: number | null;
  whatsAppButtonParameter?: string | null;
}

export interface EmailTemplatePreview {
  id: string;
  name: string;
  subject: string;
  htmlBody: string;
  signatureName: string | null;
  signatureHtml: string | null;
  attachments: CampaignAttachment[];
}

export interface TemplateListRow extends EmailTemplateSummary {
  updatedLabel: string;
  statusLabelKey: string;
  signatureLabel: string;
  emailAccountLabel: string;
  lastUsedLabel: string;
}

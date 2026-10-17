import { OutreachChannelType } from './prospecting.model';

export type SequenceEnrollmentStatus =
  | 'pending'
  | 'running'
  | 'waiting'
  | 'completed'
  | 'failed'
  | 'cancelled';

export interface OutreachSequenceStep {
  id?: string | null;
  stepOrder: number;
  channel: OutreachChannelType;
  templateId: string | null;
  templateName?: string | null;
  delayHoursAfterPrevious: number;
  whatsAppTemplateName?: string | null;
  whatsAppTemplateLanguage?: string | null;
  variantKey?: string | null;
  trafficPercent?: number;
}

export interface UpsertOutreachSequence {
  name: string;
  description: string | null;
  isActive: boolean;
  steps: OutreachSequenceStep[];
}

export interface OutreachSequenceSummary {
  id: string;
  projectId: string;
  projectName: string | null;
  name: string;
  description: string | null;
  isActive: boolean;
  stepCount: number;
  activeEnrollmentCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface OutreachSequenceDetail extends OutreachSequenceSummary {
  steps: OutreachSequenceStep[];
}

export interface EnrollProspectsRequest {
  prospectIds: string[];
}

export interface SequenceEnrollment {
  id: string;
  sequenceId: string;
  sequenceName: string | null;
  prospectId: string;
  prospectCompanyName: string | null;
  projectId: string;
  status: SequenceEnrollmentStatus;
  currentStepOrder: number;
  assignedVariantKey: string | null;
  nextRunAtUtc: string | null;
  enrolledAtUtc: string;
  completedAtUtc: string | null;
  lastError: string | null;
}

export interface SequenceVariantAnalytics {
  variantKey: string;
  enrollmentCount: number;
  completedCount: number;
  failedCount: number;
  activeCount: number;
  completionRate: number;
}

export interface SequenceStepAnalytics {
  stepOrder: number;
  variantKey: string | null;
  channel: OutreachChannelType;
  templateName: string | null;
  trafficPercent: number;
  reachedCount: number;
}

export interface SequenceAnalytics {
  sequenceId: string;
  sequenceName: string;
  totalEnrollments: number;
  activeCount: number;
  completedCount: number;
  failedCount: number;
  cancelledCount: number;
  completionRate: number;
  variants: SequenceVariantAnalytics[];
  steps: SequenceStepAnalytics[];
}

export interface SequenceListRow extends OutreachSequenceSummary {
  createdLabel: string;
  statusLabelKey: string;
  descriptionLabel: string;
  projectLabel: string;
}

export interface SequenceStepEditorRow {
  clientKey: string;
  id: string | null;
  stepOrder: number;
  channel: OutreachChannelType;
  templateId: string;
  delayHoursAfterPrevious: number;
  whatsAppTemplateName: string;
  whatsAppTemplateLanguage: string;
  variantKey: string;
  trafficPercent: number;
  showWhatsAppFields: boolean;
}

export interface SequenceStepViewRow {
  id: string | null;
  stepOrder: number;
  channelLabelKey: string;
  templateLabel: string;
  delayHoursAfterPrevious: number;
  whatsAppTemplateName: string;
  whatsAppTemplateLanguage: string;
  variantLabel: string;
  trafficPercent: number;
  showWhatsAppFields: boolean;
}

export interface SequenceEnrollmentRow extends SequenceEnrollment {
  enrolledLabel: string;
  nextRunLabel: string;
  completedLabel: string;
  canCancel: boolean;
  companyLabel: string;
  errorLabel: string;
  variantLabel: string;
}

export interface SequenceAnalyticsView {
  totalEnrollments: number;
  activeCount: number;
  completedCount: number;
  failedCount: number;
  cancelledCount: number;
  completionRateLabel: string;
  variants: SequenceVariantAnalyticsRow[];
  steps: SequenceStepAnalyticsRow[];
}

export interface SequenceVariantAnalyticsRow {
  variantKey: string;
  enrollmentCount: number;
  completedCount: number;
  failedCount: number;
  activeCount: number;
  completionRateLabel: string;
}

export interface SequenceStepAnalyticsRow {
  stepOrder: number;
  variantLabel: string;
  channelLabelKey: string;
  templateLabel: string;
  trafficPercent: number;
  reachedCount: number;
}

export interface SequenceProspectOption {
  id: string;
  label: string;
  selected: boolean;
}

export interface SequenceOption {
  id: string;
  name: string;
}

export interface SequenceTemplateOption {
  id: string;
  name: string;
}

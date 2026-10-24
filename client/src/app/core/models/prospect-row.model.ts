import { ProspectSummary, ResearchRunSummary } from './prospecting.model';

export interface ResearchRunListRow extends ResearchRunSummary {
  createdLabel: string;
  statusLabelKey: string;
}

export interface ProspectListRow extends ProspectSummary {
  createdLabel: string;
  statusLabelKey: string;
  duplicateLabelKey: string;
  qualificationLabel: string;
  sourceLabel: string;
  templateLabel: string;
  selected: boolean;
  contactLabel: string;
  projectLabel: string;
  locationLabel: string;
  emailLabel: string;
  whatsAppLabel: string;
  hasEmail: boolean;
  hasWhatsApp: boolean;
}

export interface ProjectFilterOption {
  id: string;
  name: string;
}

export interface ProspectFilterChoice {
  value: string;
  labelKey: string;
  selected: boolean;
}

export interface ProspectActiveFilterChip {
  group: 'status' | 'contactMethod' | 'qualification' | 'duplicate' | 'researchRun';
  value: string;
  labelKey: string;
}

export interface ContactHistoryRow {
  id: string;
  channelLabel: string;
  contactValue: string;
  contactedLabel: string;
}

export interface FetchedContactRow {
  id: string;
  value: string;
  kindLabel: string;
}


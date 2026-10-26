import { OutreachProjectSummary } from './prospecting.model';

export interface ProjectListRow extends OutreachProjectSummary {
  descriptionLabel: string;
  countriesLabel: string;
  citiesLabel: string;
  keywordsLabel: string;
  contactMethodsLabel: string;
  lastResearchLabel: string;
  statusLabelKey: string;
}

export interface ProjectTemplateOption {
  id: string;
  name: string;
  selected: boolean;
}

export interface ProjectResearchRunRow {
  id: string;
  status: string;
  progressPercent: number;
  requestedProspectCount: number;
  qualifiedCount: number;
  createdLabel: string;
}

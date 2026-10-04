import { CampaignSummary } from './campaign.model';

export interface CampaignHistoryRow extends CampaignSummary {
  createdLabel: string;
}

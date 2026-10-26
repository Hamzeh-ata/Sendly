import { CampaignRecipient } from './campaign.model';

export interface RecipientRow extends CampaignRecipient {
  lastAttemptLabel: string;
  alreadyContacted: boolean;
  templateStatusLabelKey: string;
  previousSendLabel: string;
}

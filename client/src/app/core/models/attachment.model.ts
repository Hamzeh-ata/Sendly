export interface CampaignAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface AttachmentUploadResult {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface AttachmentItem extends AttachmentUploadResult {
  file?: File;
  canDelete?: boolean;
}

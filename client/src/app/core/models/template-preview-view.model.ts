export interface TemplatePreviewAttachmentView {
  id: string;
  fileName: string;
  contentType: string;
  sizeLabel: string;
}

export interface TemplatePreviewView {
  id: string;
  name: string;
  subject: string;
  htmlBody: string;
  signatureName: string | null;
  attachments: TemplatePreviewAttachmentView[];
}

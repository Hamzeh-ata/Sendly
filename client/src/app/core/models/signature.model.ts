export interface EmailSignature {
  id: string;
  name: string;
  htmlContent: string;
  isDefault: boolean;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface UpsertEmailSignature {
  name: string;
  htmlContent: string;
  isDefault: boolean;
  isActive: boolean;
}

export interface SignatureSettings {
  signaturesEnabled: boolean;
}

export interface UpdateSignatureSettings {
  signaturesEnabled: boolean;
}

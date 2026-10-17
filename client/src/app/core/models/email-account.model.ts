export type EmailProvider = 'smtp' | 'microsoftGraph';

export type SmtpSecurityMode = 'none' | 'startTls' | 'sslTls';

export interface EmailAccount {
  id: string;
  provider: EmailProvider;
  displayName: string;
  emailAddress: string;
  isDefault: boolean;
  isActive: boolean;
  isConnected: boolean;
  host: string | null;
  port: number | null;
  securityMode: SmtpSecurityMode | null;
  username: string | null;
  hasPassword: boolean;
  senderEmail: string | null;
  senderDisplayName: string | null;
  replyToEmail: string | null;
  isInsecure: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface UpsertSmtpAccount {
  displayName: string;
  isDefault: boolean;
  host: string;
  port: number;
  securityMode: SmtpSecurityMode;
  username: string;
  password: string | null;
  senderEmail: string;
  senderDisplayName: string;
  replyToEmail: string | null;
}

export interface SmtpAccountTestRequest extends UpsertSmtpAccount {
  id: string | null;
}

export interface EmailAccountStatus {
  isConfigured: boolean;
  accountCount: number;
  microsoftOAuthConfigured: boolean;
}

export interface MicrosoftConnectResult {
  authorizeUrl: string;
}

export interface EmailTestResult {
  success: boolean;
  message: string;
}

export type SmtpSecurityMode = 'none' | 'startTls' | 'sslTls';

export interface SmtpSettings {
  host: string;
  port: number;
  securityMode: SmtpSecurityMode;
  username: string;
  hasPassword: boolean;
  senderEmail: string;
  senderDisplayName: string;
  replyToEmail: string | null;
  updatedAtUtc: string | null;
  isConfigured: boolean;
  isInsecure: boolean;
}

export interface UpsertSmtpSettings {
  host: string;
  port: number;
  securityMode: SmtpSecurityMode;
  username: string;
  password: string | null;
  senderEmail: string;
  senderDisplayName: string;
  replyToEmail: string | null;
}

export interface SmtpTestResult {
  success: boolean;
  message: string;
}

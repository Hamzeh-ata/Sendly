# Sendly

A modern bulk email sender with individual per-recipient delivery, secure SMTP configuration, reusable email signatures, live campaign progress, and campaign history.

## Stack

- **Backend:** ASP.NET Core 10, EF Core (SQLite), MailKit, SignalR
- **Frontend:** Angular 21 (standalone, signals), ngx-translate (EN/AR)
- **Architecture:** Domain / Application / Infrastructure / Api

## Features

- Compose campaigns with recipient parsing, validation, rich HTML body, attachments
- Multiple reusable email signatures with default selection
- Individual SMTP delivery (one message per recipient, never shared To/CC/BCC lists)
- Configurable delay, batch size, and transient retry limits
- Secure SMTP password storage (AES-GCM); passwords never returned to the client
- Live progress over SignalR
- Campaign history, filters, retry failed (transient only), CSV export
- Dark premium SaaS UI

## Prerequisites

- .NET 10 SDK
- Node.js 22+
- npm 11+

## Configure

Generate a 32-byte encryption key (recommended for any non-throwaway environment):

```powershell
cd src/Sendly.Api
dotnet user-secrets init
dotnet user-secrets set "SecretProtection:EncryptionKey" "<base64-32-byte-key>"
```

Example key generation:

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
```

## Run API

```powershell
cd c:\Users\User\source\repos\Sendly
dotnet restore
dotnet ef database update --project src/Sendly.Infrastructure --startup-project src/Sendly.Api
dotnet run --project src/Sendly.Api
```

API: `http://localhost:5088`  
Swagger (Development): `http://localhost:5088/swagger`  
SignalR hub: `http://localhost:5088/hubs/campaigns`

Migrations also apply automatically on API startup.

## Run UI

```powershell
cd c:\Users\User\source\repos\Sendly\client
npm install
npm start
```

UI: `http://localhost:4200`

## Test

```powershell
cd c:\Users\User\source\repos\Sendly
dotnet test Sendly.slnx
cd client
npm run build
```

## SMTP setup

1. Open **Settings**
2. Enter host, port, security mode, username, password/app password, sender identity
3. Use **Test Connection**
4. Save configuration
5. Optionally create email signatures
6. Compose a campaign and optionally send a test email to yourself first

Common ports:

| Mode | Typical port |
|------|--------------|
| STARTTLS | 587 |
| SSL/TLS | 465 |
| None | 25 |

## Security notes

- SMTP credentials are stored only on the server, encrypted at rest
- API responses never include SMTP passwords
- Logs redact password-like content from SMTP errors
- Successful send means the SMTP server accepted the message — not guaranteed inbox delivery
- Use permission-based recipient lists and respect provider rate limits

## Project layout

```
Sendly/
├── src/
│   ├── Sendly.Domain/
│   ├── Sendly.Application/
│   ├── Sendly.Infrastructure/
│   └── Sendly.Api/
├── client/                  # Angular SPA
└── tests/
```

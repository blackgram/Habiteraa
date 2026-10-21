# Habiteraa

ASP.NET Core 8 API for Habitera (Postgres + Redis).

## Local run

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (this machine uses `~/.dotnet`)
- Homebrew Docker tooling: `colima`, `docker`, `docker-compose`

Put these on your PATH (zsh):

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:/opt/homebrew/bin:$PATH"
eval "$(/opt/homebrew/bin/brew shellenv)"
```

### 1. Start dependencies

```bash
colima start
docker compose up -d
```

This starts:

- Postgres/PostGIS on `localhost:5432` (`habitera` / `habitera` / `habitera`)
- Redis on `localhost:6379`

### 2. Configure the API

`appsettings.json` and `appsettings.Development.json` are in source control. Development overrides the empty production values with the local Postgres, Redis, and placeholder email/image settings.

For a deployed environment, fill the values in `appsettings.json` or set the same keys as environment variables. See the deployment section below.

### 3. Restore, migrate, run

```bash
cd src/api
dotnet restore
dotnet ef database update
# Bind all interfaces so Expo Go / physical devices can reach the API
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://0.0.0.0:5141 --no-launch-profile
```

API: http://localhost:5141 (or `http://YOUR_LAN_IP:5141` from a phone)  
Swagger: http://localhost:5141/swagger  
Health: http://localhost:5141/health

In Development, verification and password-reset codes are logged as:
`DEV email verification code for ...` / `DEV password reset code for ...`

## Deployment

The API does not apply EF migrations on startup. Run `dotnet ef database update` against the production database before the new build serves traffic.

Provision:

- **Postgres with PostGIS** (the app uses NetTopologySuite). Plain Postgres is not enough. Set `ConnectionStrings:DefaultConnection`.
- **Redis**. Required at startup for cache, chat presence, and the SignalR backplane. Set `ConnectionStrings:Redis`. The process throws if Redis cannot be reached.
- **HTTPS host** for the API, with WebSockets enabled. Chat connects to `/hubs/chat` and sends the JWT as the `access_token` query parameter, so the proxy must forward that query string and upgrade WebSocket requests.
- **Persistent disk for `wwwroot/uploads`** if Cloudinary is not configured. Local images are written there and served from the API.

Set these before production traffic:

| Setting | Required | Notes |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | Yes | PostGIS connection string |
| `ConnectionStrings:Redis` | Yes | Redis connection string |
| `JwtSettings:Secret` | Yes | Long random string, at least 32 characters. Changing it signs everyone out |
| `JwtSettings:Issuer` / `Audience` | Yes | Keep them identical to what tokens were issued with |
| `App:PublicBaseUrl` | Yes | Public `https://` origin of the API, no trailing path. Used when an image URL is built outside a request |
| `Cloudinary:CloudName` / `ApiKey` / `ApiSecret` | For hosted images | Empty or placeholder values store uploads on local disk |
| `SendGrid:ApiKey` / `FromEmail` / `FromName` | For signup | Verification codes are sent through SendGrid. The From address must be a verified sender |
| `Brevo:SmtpServer` / `Port` / `FromEmail` / `FromName` / `Username` / `Password` | For account email | Password reset, welcome, and password-changed mail use Brevo SMTP |
| `ListingModeration:AutoApproveAfterMinutes` | Optional | Default 30. Pending listings go live after this |
| `ListingModeration:AutoApproveCron` | Optional | Cron in UTC. Default every 5 minutes |
| `Verification:*` | Optional | OTP lifetime, attempts, and resend cooldown |

On the mobile app, set `EXPO_PUBLIC_API_URL` to that same public API origin. Chat, images, and REST all use it.

Also:

- Create an Admin user yourself. Startup only creates the `Admin`, `Agent`, and `User` roles.
- Elasticsearch is not enabled. Search runs against Postgres.
- `MockUsers` is only for local auth helpers. Leave it empty in production.
- Signup requires a confirmed email, so SendGrid has to work before new users can sign in.

# SmartSchool API

Backend for the SmartSchool application. It currently provides the authentication foundation:
user registration, JWT login, refresh-token rotation, password changes, and role-aware access
tokens on top of PostgreSQL.

## Stack

| Concern | Choice |
| --- | --- |
| Runtime | .NET 10 |
| Web | ASP.NET Core controllers |
| Data | EF Core 10 + Npgsql (PostgreSQL) |
| Auth | JWT bearer access tokens + opaque refresh tokens |
| API exploration | Microsoft.AspNetCore.OpenApi + Scalar (Development only) |
| Tests | NUnit 4 + EF Core InMemory |

## Repository layout

```
backend/SmartSchool/
├── SmartSchool.API/
│   ├── Configurations/   # EF Core entity configurations
│   ├── Contracts/        # Request/response DTOs
│   ├── Controllers/      # HTTP endpoints
│   ├── Data/             # ApplicationDbContext
│   ├── Migrations/       # EF Core migrations
│   ├── Models/           # Persistence entities
│   ├── Options/          # Strongly typed configuration (JwtSettings)
│   └── Services/         # AuthService, TokenService
├── SmartSchool.Tests/    # NUnit tests
└── SmartSchool.slnx
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL (local instance or container)
- `dotnet-ef` CLI tool if you apply migrations from the command line:
  `dotnet tool install --global dotnet-ef`

## Configuration

`appsettings.json` intentionally contains no secrets: the connection string and signing key are
empty. Both must be configured; the API refuses to start when `Jwt:SigningKey` is missing or shorter
than 32 bytes, and database-backed endpoints fail until a valid connection string is supplied. The
API project has a `UserSecretsId`, so user secrets are the simplest way to configure a development
machine:

```powershell
cd backend/SmartSchool

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=smartschool;Username=postgres;Password=your-password" --project SmartSchool.API
dotnet user-secrets set "Jwt:SigningKey" "<at-least-32-bytes-of-random-text>" --project SmartSchool.API
```

| Key | Required | Default | Notes |
| --- | --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | Yes | – | Npgsql connection string |
| `Jwt:SigningKey` | Yes | – | HMAC-SHA256 key, at least 32 bytes |
| `Jwt:Issuer` | No | `SmartSchool.API` | Written to and required from access tokens |
| `Jwt:Audience` | No | `SmartSchool.Client` | Written to and required from access tokens |
| `Jwt:AccessTokenExpiryMinutes` | No | `15` | Access token lifetime |
| `Jwt:RefreshTokenExpiryDays` | No | `7` | Refresh token lifetime |

For CI or containers, use environment variables with a double underscore, for example
`ConnectionStrings__DefaultConnection` and `Jwt__SigningKey`.

## Running the API

```powershell
cd backend/SmartSchool

# Create/update the database schema
dotnet ef database update --project SmartSchool.API

# Start the API (http profile, http://localhost:5220)
dotnet run --project SmartSchool.API
```

`dotnet ef` boots the application's `Program.cs` (there is no design-time factory), so the JWT
settings must be configured before running migration commands as well.

In Development the OpenAPI document is served at `/openapi/v1.json` and the Scalar UI at
`/scalar/v1`.

## Endpoints

| Method | Route | Auth | Request | Success | Failures |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/auth/login` | Anonymous | `LoginRequest` | `200` `LoginResponse` | `400` validation, `401` invalid credentials |
| POST | `/api/auth/refresh` | Anonymous | `RefreshTokenRequest` | `200` `RefreshTokenResponse` | `400` validation, `401` invalid/expired token |
| POST | `/api/auth/change-password` | Bearer | `ChangePasswordRequest` | `204` | `400` wrong current password, `401` invalid access token |
| POST | `/api/register` | Anonymous | `RegisterRequest` | `201` `RegisterResponse` | `400` validation, `409` duplicate email |

`POST /api/register` is a **temporary** endpoint used to create users until a proper user
management flow exists. It is anonymous, unverified, and should not ship to production as-is.

Error responses come in two shapes today:

- Model-validation failures produce ASP.NET Core `ProblemDetails` with an `errors` dictionary.
- Service-level failures produce `{ "message": "..." }`.

Login failures deliberately return the same message for an unknown email and a wrong password to
avoid disclosing which accounts exist.

## Authentication model

- **Access token**: signed JWT, 15 minutes by default. Contains `sub` (user id), `email`, `jti`,
  and one role claim per *active* role assignment (issued with the `ClaimTypes.Role` claim type).
- **Refresh token**: random 512-bit value, 7 days by default. Only its SHA-256 hex digest is
  persisted; the raw value is returned to the client once.
- **Rotation**: refreshing revokes the presented token, stores the replacement, and links the two
  through `ReplacedByTokenId`. A revoked or expired token is rejected.
- **Password change**: re-hashes the password and revokes every non-revoked refresh token for that
  user, ending other sessions.
- **Emails**: login and registration compare emails case-insensitively and trim surrounding
  whitespace.

## Tests

```powershell
cd backend/SmartSchool
dotnet test
```

The suite runs entirely in memory; no database, Docker, or network access is required. It covers:

- `AuthService`: login outcomes, email matching, role filtering, refresh-token rotation/revocation,
  password changes.
- `TokenService`: JWT claims/expiry/signature, refresh-token generation, SHA-256 hashing.
- Auth request validation: required fields, email format, and the 8-character password minimum.

Notes:

- The validation tests validate the record constructor parameters the same way MVC does. Record
  models must keep their validation attributes on the parameters (`[param: ...]`); moving them to
  properties (`[property: ...]`) makes MVC throw at request time.
- `RegisterController` is intentionally untested because it is temporary.
- EF InMemory does not exercise PostgreSQL-specific SQL, indexes, or collations, so an integration
  test against a real PostgreSQL instance is still worth adding.

## Migrations

```powershell
cd backend/SmartSchool

dotnet ef migrations add AddStudentTable --project SmartSchool.API
dotnet ef database update --project SmartSchool.API
```

## Known limitations / next steps

- No background job removes expired or revoked refresh tokens.
- Refresh-token reuse is not detected; replaying an old token cannot revoke its rotation chain.
- No email verification, password reset, or role management API yet.
- The unique index on `Users.Email` is case-sensitive while login/registration are case-insensitive,
  so concurrent registration of the same address in different casing can surface as a `500`.
- Password rules are only "at least 8 characters" on registration and password change.

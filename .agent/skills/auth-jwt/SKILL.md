# Skill: Auth JWT

## Current Implementation

JWT authentication is fully implemented using ASP.NET Core Identity + custom JWT token generation.

### Architecture

- **Identity provider**: ASP.NET Core Identity via `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`
- **Token generation**: Custom `JwtService` — HMAC-SHA256 signed JWTs
- **Refresh tokens**: Stored as SHA-256 hashes in `refresh_tokens` table with rotation and revocation
- **Configuration**: `JwtOptions` bound from `Jwt` config section, validated on startup
- **Current user**: `CurrentUserService` reads claims from `HttpContext`

### Token Flow

1. **Register/Login** → generates access token + refresh token → refresh token set as HttpOnly cookie
2. **Refresh** → validates refresh token from cookie → rotates to new token pair → revokes old token
3. **Logout** → revokes current refresh token → clears cookie
4. **Protected endpoints** → JWT Bearer middleware validates access token → `ICurrentUser` provides user info

### Claims in Access Token

- `sub` / `NameIdentifier` → User ID (Guid)
- `email` / `ClaimTypes.Email` → User email
- `jti` → JWT ID (links to refresh token)
- `role` / `ClaimTypes.Role` → User roles

## Rules

- Do not change JWT behavior casually — it affects all authenticated users.
- Do not hard-code JWT secret — it comes from `Jwt:SigningKey` configuration.
- Do not expose password hashes or raw refresh tokens.
- Protect endpoints that require authentication with `[Authorize]`.
- Use `ICurrentUser` to access the current user's ID and email — do not parse claims manually.
- Refresh tokens must always be hashed before storage (`HashHelper.ComputeSha256Hash`).

## Files to Inspect

| File | Purpose |
|------|---------|
| `API/Controllers/AuthController.cs` | Auth endpoints (register, login, refresh, logout, me) |
| `API/Program.cs` | JWT Bearer middleware configuration (lines 70-85) |
| `Application/Common/Interfaces/IJwtService.cs` | JWT service interface |
| `Application/Common/Interfaces/IIdentityService.cs` | Identity operations interface |
| `Application/Common/Interfaces/ICurrentUser.cs` | Current user abstraction |
| `Application/UseCases/Auth/` | All auth use case handlers |
| `Infrastructure/Options/JwtOptions.cs` | JWT configuration model |
| `Infrastructure/Services/JwtService.cs` | Token generation implementation |
| `Infrastructure/Services/IdentityService.cs` | Identity operations implementation |
| `Infrastructure/Services/CurrentUserService.cs` | Reads current user from HttpContext |
| `Infrastructure/Persistence/ApplicationUser.cs` | Identity user entity |
| `Domain/Entities/RefreshToken.cs` | Refresh token domain entity |
| `Common/Constants/AuthConstants.cs` | Auth-related constants |

## Verification

- Register works → returns access token + sets refresh token cookie.
- Login works → returns access token + sets refresh token cookie.
- Refresh works → rotates tokens, old token is revoked.
- Logout works → revokes refresh token, clears cookie.
- `GET /api/auth/me` → returns current user when authenticated.
- Protected endpoint rejects anonymous requests (401).
- Protected endpoint accepts valid JWT (200).

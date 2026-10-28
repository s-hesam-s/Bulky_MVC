# Local security configuration

This repository contains educational examples. Credential values are intentionally empty in tracked application settings. Supply your own development values through environment variables before starting the relevant application. ASP.NET Core maps a double underscore in an environment variable to a configuration colon.

| Application settings | Configuration key | Environment variable |
|---|---|---|
| `BulkyWeb/appsettings.json` | `Stripe:SecretKey` | `Stripe__SecretKey` |
| `BulkyWeb/appsettings.json` | `Stripe:PublishableKey` | `Stripe__PublishableKey` |
| `BulkyWeb/appsettings.json` | `SendGrid:SecretKey` | `SendGrid__SecretKey` |
| Optional Facebook sign-in | `Authentication:Facebook:AppId` | `Authentication__Facebook__AppId` |
| Optional Facebook sign-in | `Authentication:Facebook:AppSecret` | `Authentication__Facebook__AppSecret` |

For a JWT signing secret, use a locally generated random value of at least 32 bytes. Services that issue or validate the same JWT must use the same local secret, issuer and audience. Do not reuse a published training value. Do not commit local credentials or print them in logs.

PowerShell example for the current shell only:

```powershell
$env:ApiSettings__Secret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

For applications using `ApiSettings:JwtOptions:Secret`, set `ApiSettings__JwtOptions__Secret` instead. Supply payment, storage, messaging and Redis settings only for the features you intend to run, using your own development resources. Empty settings are not fallback credentials.

Some examples apply Entity Framework migrations during application startup. Review startup behavior and select an isolated development database before running them. No migration or database update is performed by this configuration change.

Public registration must create only a customer account. Administrative accounts and role changes require a trusted administrative process; the registration page is not an admin bootstrap mechanism. Do not enable public admin registration to set up a demonstration.

Historical commits may still contain previous training values. If a value was ever connected to a real resource, its owner should revoke or rotate it; clearing the current files does not invalidate an old value or rewrite Git history.

Run `pwsh -File scripts/Test-PublicConfiguration.ps1` to check public configuration without displaying values.

Run `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj`. Identity managers, sign-in and external resources are mocked; no database or email service is contacted.

An already authenticated Admin can create accounts with the existing supported roles. Anonymous and ordinary users always create Customer accounts, regardless of submitted role or company fields.

## Account bootstrap and browser/payment boundaries

Startup initializes each missing role independently and checks creation results. It does not create an administrator account or use a fixed password. Use a trusted Identity administration process to provision an administrator in your own development database; public registration remains customer-only for ordinary users.

Facebook authentication is disabled when both settings are absent. Configure both through local environment settings to enable it; an incomplete pair fails with a generic configuration message.

See [order and payment security boundaries](docs/SECURITY-BOUNDARIES.md) for ownership checks, POST verification, antiforgery, payment reconciliation limits and fixture evidence.

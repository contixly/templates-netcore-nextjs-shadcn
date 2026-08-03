---
title: "Runtime security"
description: "Security-relevant runtime defaults for app origin, image hosts, browser headers, and protected routes."
group: "Application"
groupOrder: 500
parentItem: "Runtime"
parentItemOrder: 90
order: 10
toc: true
purpose: "Runtime security reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Runtime security

Runtime security is split across ASP.NET Core's HTTP boundary and the thin Next.js UI. The same-origin design keeps credentials out of browser JavaScript and makes the API authoritative.

## Session and CSRF

`__Host-template.session` is `HttpOnly`, `Secure`, `SameSite=Lax`, path `/`, no `Domain`, with persistent seven-day sliding expiration. It contains a Data-Protection-protected opaque ticket-store key. PostgreSQL stores its SHA-256 hash and a separately protected ticket.

Every unsafe browser operation fetches `GET /api/v1/auth/csrf` and sends `X-CSRF-TOKEN` with the paired strict antiforgery cookie. API challenge/forbid returns JSON `401`/`403`, never HTML redirects. JavaScript reads neither cookie and stores no bearer token.

## Protection and OAuth state

Data Protection keys persist in PostgreSQL with discriminator `Template`. Production also requires an RSA PFX configured by `DataProtection__CertificatePath` and `DataProtection__CertificatePassword`; startup fails closed for invalid material. OpenIddict Client state is protected and one-time; provider tokens are callback-local and not persisted.

## Origins and routing

Browser calls use relative same-origin `/api/**`. SSR uses server-only `API_INTERNAL_BASE_URL` and forwards only allowed cookie/correlation data. `API_PROXY_TARGET` is a Development/E2E rewrite; the future production topology lets Kestrel own `/api/**`. CORS is not enabled.

`APP_PUBLIC_ORIGIN` configures Next.js metadata URLs. OAuth separately validates `ExternalAuthentication__PublicOrigin`, requiring HTTPS except for loopback development.

Stateful API/auth/health/account/collaboration/search/API-key responses use `Cache-Control: no-store`. Safe `traceId` correlation is exposed; stack traces, SQL, secrets, cookies, and authorization headers are not.

The YARP/Kestrel production proxy, container hardening, final product shell, and Redis/Aspire orchestration remain future iterations.

## Related pages

- [OAuth providers](/docs/application/oauth-providers)
- [Caching](/docs/application/caching)
- [API access](/docs/api)
- [Sessions and security](/docs/account/sessions-security)

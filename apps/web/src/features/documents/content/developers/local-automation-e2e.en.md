---
title: "Local automation and E2E"
description: "Use the ASP.NET Core local-automation endpoints and generated SDK helpers in deterministic Playwright workflows."
group: "For developers"
groupOrder: 300
parentItem: "Quality workflow"
parentItemOrder: 70
order: 20
toc: true
purpose: "Developer testing how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Local automation and E2E

ASP.NET Core provides a deliberately local-only credential flow for Playwright and browser
verification. It creates real clean-store users and persistent HttpOnly-cookie sessions without
adding a production sign-in shortcut.

## Availability boundary

Local automation is available only when both conditions are true:

- the API environment is `Development` or `Test`;
- `LocalAutomationAuth__Enabled=true`.

Production returns `404 local_auth_disabled` even if the flag is accidentally enabled. Keep real
credentials out of committed settings; the local flow generates its own scenario credentials. Every
unsafe automation request follows the normal CSRF contract.

## Playwright orchestration

`npm run e2e` uses `apps/web/playwright.config.ts`. It starts
`apps/api/tests/Template.E2EHost`, which creates and migrates a disposable PostgreSQL 18.4 database
and launches the real `Template.Api` process. It also starts Next.js on `127.0.0.1:3127` with the
local `/api/**` rewrite to the API on `127.0.0.1:5297`.

`Template.E2EHost` is an orchestration executable, not a second HTTP host. The readiness probe is
`/api/health/ready`, and normal teardown stops Next.js, the API process, and the disposable database.

## Create, sign in, and clean up

Use generated-SDK helpers from `apps/web/e2e/support/generated-auth-api.ts`:

- `createLocalAutomationUser` creates a scenario through `/api/local-auth/scenario`;
- `signInLocalAutomationUser` creates another persistent session when a scenario needs it;
- `confirmGeneratedLocalAutomationEmail` confirms only an eligible local scenario user;
- `cleanupLocalAutomationUser` deletes the current local scenario through the authenticated
  context.

Helpers obtain a fresh CSRF token and use the same-origin generated client. Keep each user's browser
context isolated. Register cleanup as soon as a scenario is created, and never print returned
passwords, API keys, cookies, or response bodies containing secrets.

## Write a focused scenario

Prefer role-based locators and wait for the application's explicit interaction-readiness markers.
Assert browser behavior and the observable REST request, not internal component state or direct SQL.
Use API helpers to arrange only behavior that is part of the supported contract. Do not change
verification flags directly in PostgreSQL.

Run one file while iterating, then the complete deterministic suite:

```bash
cd apps/web
npm run e2e -- authentication.spec.ts
npm run e2e
```

Live OAuth provider navigation is separate and opt-in through `E2E_LIVE_PROVIDER_SMOKE=1`; it does
not submit credentials or prove a callback.

## Related pages

- [Requirements, E2E, and docs](/docs/developers/openspec-e2e-docs)
- [Runtime security](/docs/application/runtime-security)
- [Account settings](/docs/account)
- [Workspace](/docs/workspace)

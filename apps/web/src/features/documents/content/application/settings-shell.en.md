---
title: "Settings shell"
description: "How account and workspace settings pages stay consistent across sections and themes."
group: "Application"
groupOrder: 500
parentItem: "Settings"
parentItemOrder: 60
order: 10
toc: true
purpose: "Settings surface explanation"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Settings shell

The current settings pages are working REST surfaces for account, organization, collaboration, and API-key scenarios. They are not the final visual product shell planned for iteration 9.

## Current routes

Account settings under `/user/**` include profile, connections, security, danger, and personal API keys. Organization settings under `/w/{organizationKey}/settings/**` include workspace details, users, roles, teams, invitations, and organization API keys.

The URL slug is presentation context. Server loaders resolve trusted organization detail and use its canonical UUID for API calls. Server permissions control navigation and mutation visibility; UI hiding is never the only authorization check.

## Loaders and mutations

Server-rendered loaders use isolated generated REST SDK clients, `API_INTERNAL_BASE_URL`, `cache: "no-store"`, and an allow-list of cookie/correlation headers. They never query PostgreSQL or the identity store.

Browser mutations use generated SDK operations with `credentials: "same-origin"`. Every unsafe call gets fresh CSRF state and sends `X-CSRF-TOKEN`; visible state changes after a confirmed API response. Problem Details becomes safe localized copy by stable `code` and optional `traceId`.

Do not add raw `fetch`, handwritten transport DTOs, Server Actions, Next.js Route Handlers, Prisma, Better Auth, direct database access, or browser bearer storage.

## Deferred shell

Today's layouts provide protected routing, loading/error states, forms, lists, opaque pagination, permission gates, and secret reveal for iterations 3–7. Iteration 9 owns the final responsive sidebar, dashboard, settings visual grammar, theme polish, and route-by-route parity review.

## Related pages

- [Application shell](/docs/application)
- [Account settings](/docs/account)
- [Workspace settings](/docs/workspace/settings)
- [Runtime security](/docs/application/runtime-security)

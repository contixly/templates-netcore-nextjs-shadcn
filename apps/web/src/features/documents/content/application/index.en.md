---
title: "Application shell"
description: "Public entry points, protected application shell, dashboard surface, and feature extension points."
group: "Application"
groupOrder: 500
parentItem: "Foundation"
parentItemOrder: 100
order: 10
toc: true
purpose: "Application overview"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Application shell

The system is a REST application with a strict ownership boundary: ASP.NET Core owns `/api/**`, authentication, authorization, business use cases, persistence, and OAuth; Next.js owns rendering and browser interaction.

## Data flow

Server-rendered and browser features use the committed OpenAPI contract and generated REST SDK. Adapters unwrap `{ data }` and normalize Problem Details into safe UI failures. They do not use raw `fetch`, redefine DTOs, query the database, or read authentication storage. There are no Server Actions or Next.js API Route Handlers for product data.

Browser calls use same-origin `/api/**` and automatic cookies. SSR creates an isolated client per credential context, uses server-only `API_INTERNAL_BASE_URL`, and forwards only approved cookie/correlation data. Neither path stores bearer tokens.

## Backend layers

- `Template.Domain` contains domain value objects and rules without HTTP or infrastructure dependencies.
- `Template.Application` contains use cases and ports and depends only on Domain.
- `Template.Infrastructure` implements persistence, Identity, OpenIddict Client, cryptography, and other ports.
- `Template.Api` is the only HTTP host and validates/authenticates at the boundary before Application.

Business rules belong in Application or Domain, not endpoint handlers or React components.

## Current and future UI

Public UI includes `/`, `/auth/login`, `/auth/error`, and `/docs/**`. Protected surfaces include the transient `/dashboard` resolver, `/welcome`, `/workspaces`, `/user/**`, `/invite/{invitationId}`, and `/w/{organizationKey}/**`. The current pages operate iterations 1–7. The final dashboard, navigation, and visual application shell are deferred to iteration 9.

## Related pages

- [Quick start](/docs/general/quick-start)
- [Workspace](/docs/workspace)
- [Localization](/docs/application/localization)
- [Runtime security](/docs/application/runtime-security)
- [Feature slice architecture](/docs/developers/feature-slice)

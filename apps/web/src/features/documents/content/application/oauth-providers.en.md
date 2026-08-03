---
title: "OAuth providers"
description: "Configure provider credentials and understand how configured-only login and connection UI works."
group: "Application"
groupOrder: 500
parentItem: "Authentication"
parentItemOrder: 80
order: 10
toc: true
purpose: "Authentication how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# OAuth providers

External sign-in is owned by ASP.NET Core through OpenIddict Client. The closed provider set is Google, GitHub, GitLab, VK, and Yandex. OpenIddict is a client/state-replay boundary, not an authorization server or token vault.

## Configure providers

Set HTTPS `ExternalAuthentication__PublicOrigin`; HTTP is allowed only for loopback development. Configure both `ClientId` and `ClientSecret` under `ExternalAuthentication__Providers__Google`, `GitHub`, `GitLab`, `Vk`, or `Yandex`.

A provider is advertised only with a complete canonical pair. Zero providers is valid; partial or unknown configuration fails validation without logging secrets.

## Callback paths

| Provider | Callback                           |
| -------- | ---------------------------------- |
| Google   | `/api/auth/callback/google`        |
| GitHub   | `/api/auth/callback/github`        |
| GitLab   | `/api/auth/callback/gitlab`        |
| VK       | `/api/auth/callback/vk`            |
| Yandex   | `/api/auth/oauth2/callback/yandex` |

These unversioned protocol callbacks are excluded from OpenAPI and the generated REST SDK. Next.js starts sign-in/connect only through `POST /api/v1/auth/external/{provider}/challenge`, with fresh CSRF and a safe same-origin return path, then navigates top-level to the server-issued HTTPS URL.

## Token boundary

Success produces the secure HttpOnly session; JavaScript stores no bearer token. Provider access/refresh tokens exist only during callback normalization and are not persisted in Identity, OpenIddict rows, the database, logs, responses, or browser storage. Local disconnect therefore does not revoke remote consent, and provider-token refresh is unsupported.

## Related pages

- [Profile and connections](/docs/account/profile-connections)
- [Application shell](/docs/application)
- [Runtime security](/docs/application/runtime-security)

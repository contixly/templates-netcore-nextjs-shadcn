---
title: "Glossary"
description: "Definitions for the ASP.NET Core REST API, separate Next.js UI, authentication, workspaces, generated contracts, and documentation pipeline."
group: "General"
parentItem: "Glossary"
parentItemOrder: 10
order: 10
toc: true
purpose: "Template user documentation"
status: "published"
author: "Template Maintainers"
version: "1.1.0"
editedAt: "2026-07-06"
---

# Glossary

These terms describe the current target template. Historical pages may mention the former full-stack
Next.js implementation; treat that behavior as legacy unless a current page explicitly confirms it.

## Architecture and routing

| Term                      | Meaning                                                                                                                                                                  |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Template                  | A .NET 10 ASP.NET Core REST API and a separate Next.js UI with PostgreSQL persistence, authentication, workspaces, API keys, localized documentation, and quality gates. |
| `Template.Api`            | The only HTTP host. It owns `/api/**`, Minimal API composition, HTTP validation, authentication, authorization, and response mapping.                                    |
| `Template.Domain`         | Domain value objects and rules with no infrastructure or HTTP dependencies.                                                                                              |
| `Template.Application`    | Use cases, application models, and ports. It depends only on Domain.                                                                                                     |
| `Template.Infrastructure` | Implementations of Application/Domain ports, including EF Core persistence, Identity, OpenIddict Client, cryptography, and document search storage.                      |
| Endpoint module           | An implementation of `IEndpointModule` that maps one API capability through the shared route groups.                                                                     |
| REST boundary             | The only product-data boundary between Next.js and ASP.NET Core. The web app has no Server Actions or API Route Handlers for product data.                               |
| Same-origin topology      | Deployment shape where browser pages and `/api/**` share an origin; local Next.js uses `API_PROXY_TARGET`, while production leaves API ownership with ASP.NET Core.      |
| Application shell         | The Next.js layouts, navigation, and protected screens rendered from REST projections.                                                                                   |

## Authentication and security

| Term                  | Meaning                                                                                                                                           |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| Browser session       | An ASP.NET Core authenticated session represented in the browser by a secure HttpOnly cookie and backed by a persistent ticket in PostgreSQL.     |
| HttpOnly cookie       | A cookie sent automatically by the browser but unavailable to JavaScript. The UI does not copy it into browser storage.                           |
| CSRF token            | A fresh request token from `GET /api/v1/auth/csrf`, sent as `X-CSRF-TOKEN` for unsafe browser operations.                                         |
| OpenIddict Client     | The ASP.NET Core OAuth client integration. It is not an authorization server and does not persist provider access or refresh tokens.              |
| Local automation auth | A Development/Test-only ASP.NET Core flow for generated Playwright users and sessions. It is unavailable in Production even if its flag is set.   |
| OAuth provider        | One of the configured external sign-in providers advertised by the API. A provider is absent when its required local configuration is incomplete. |
| Connected account     | A persisted link between the user and an external provider identity.                                                                              |
| Danger zone           | Account UI for destructive operations such as deleting the current user.                                                                          |

## Workspaces

| Term                      | Meaning                                                                                                             |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| Workspace                 | The user-facing collaboration boundary backed by an ASP.NET Core organization aggregate and PostgreSQL records.     |
| Organization              | The backend tenant that owns members, roles, teams, invitations, allowed email domains, and organization API keys.  |
| Organization key          | The slug or UUID accepted by `/w/{organizationKey}/...`; resolved pages redirect to the current canonical slug.     |
| Active workspace          | The accessible organization stored in the current persistent browser session and used by the `/dashboard` resolver. |
| Member                    | A user linked to an organization with the built-in owner, admin, or member role.                                    |
| Team                      | An explicit subgroup of organization members. A new organization may validly have no teams.                         |
| Invitation                | A bounded request for an email recipient to join an organization, optionally targeting a team.                      |
| Allowed email domains     | Organization policy used for invitation and direct-member admission decisions.                                      |
| Zero-workspace onboarding | The `/welcome` experience for an authenticated user with no accessible organization.                                |

## API and generated contracts

| Term               | Meaning                                                                                                                                         |
| ------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| API v1             | Versioned operations under `/api/v1`; individual routes declare browser-session, API-key, mixed, or anonymous access.                           |
| API key            | A reveal-once machine credential sent in `x-api-key`; it is never stored in browser storage or placed in a URL.                                 |
| Scope              | A concrete permission checked for an API-key operation.                                                                                         |
| Success envelope   | The typed JSON shape `{ data: ... }`. Paginated collections place `items` and `nextCursor` inside `data`.                                       |
| Problem Details    | The `application/problem+json` failure shape with required RFC fields plus stable `code` and safe `traceId`; validation also includes `errors`. |
| OpenAPI contract   | The committed `contracts/openapi/v1.json` description exported from `Template.Api`.                                                             |
| Generated REST SDK | The committed TypeScript client under `apps/web/src/lib/api/generated`, regenerated from OpenAPI and consumed by web adapters.                  |
| Opaque cursor      | A server-issued pagination value returned unchanged by clients; it must not be decoded, edited, or reused for another collection.               |

## Documentation and quality

| Term                        | Meaning                                                                                                                                 |
| --------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| Locale variant              | An `.en.md`/`.ru.md` or `.en.mdx`/`.ru.mdx` source file for one canonical documentation URL.                                            |
| Canonical documentation URL | A `/docs/...` route with no locale suffix or source extension.                                                                          |
| Content compiler            | The deterministic scripts that validate metadata, locale pairs, links, images, MDX syntax, and generate the registry and search index.  |
| Closed MDX vocabulary       | The fixed allow-list of custom documentation components; imports, exports, and unknown components are rejected.                         |
| `content:check`             | The command that recompiles documentation and byte-compares both committed generated artifacts.                                         |
| `PUBLIC_DEFAULT_LOCALE`     | The deployment-wide `en` or `ru` selection used by the UI and documentation; invalid values fall back to `en`.                          |
| OpenSpec                    | An initialized but currently inactive specification workspace. No active capability or change exists unless explicitly requested later. |
| `WebApplicationFactory`     | The ASP.NET Core integration-test host used to verify HTTP endpoints without inventing a second application host.                       |
| `Template.E2EHost`          | A test orchestrator that creates disposable PostgreSQL, applies migrations, and launches the real `Template.Api` for Playwright.        |

## Related pages

- [Template documentation](/docs) — overview of the available sections.
- [Quick start](/docs/general/quick-start) — first local setup flow.
- [Application shell](/docs/application) — current runtime boundary.
- [Workspace](/docs/workspace) — collaboration model and user scenarios.
- [API access](/docs/api) — API keys and `/api/v1`.
- [For developers](/docs/developers) — test-first extension workflow.

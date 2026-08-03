---
title: "Feature slice architecture"
description: "Place a feature across Domain, Application, Infrastructure, Api, and the separate Next.js UI while preserving inward dependencies."
group: "For developers"
groupOrder: 300
parentItem: "Project development"
parentItemOrder: 100
order: 20
toc: true
purpose: "Developer how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Feature slice architecture

A feature slice follows one product capability across the backend layers and, when needed, the web
UI. It does not collapse those layers into one folder or let an outer concern become a business
rule.

## Backend placement

| Layer                     | Typical feature content                                                                   | May depend on                                   |
| ------------------------- | ----------------------------------------------------------------------------------------- | ----------------------------------------------- |
| `Template.Domain`         | Value objects, closed policies, and rules that need no I/O.                               | Nothing in Application, Infrastructure, or Api. |
| `Template.Application`    | Use cases, application models, and ports under `Ports/`.                                  | Domain only.                                    |
| `Template.Infrastructure` | EF Core stores, Identity/OpenIddict adapters, cryptography, and port implementations.     | Application and Domain.                         |
| `Template.Api`            | Request/response contracts, boundary helpers, an `IEndpointModule`, and OpenAPI metadata. | Application and Infrastructure composition.     |

Use the existing capability folders such as `Organizations`, `Collaboration`, or `ApiKeys` as the
shape to follow. `Template.Api` is the only HTTP host. It validates and authorizes at the boundary,
then delegates to Application. Domain and Application must not know about `HttpContext`, Minimal
API results, EF Core, or React.

## Web placement

The separate UI uses these boundaries:

- routes and layouts under `apps/web/src/app` compose pages;
- product coordination belongs under `apps/web/src/features`;
- reusable rendered controls belong under `apps/web/src/components`;
- server and browser API adapters belong under `apps/web/src/lib/api`;
- transport DTOs and operations come from `apps/web/src/lib/api/generated`.

Next.js never imports .NET assemblies, queries PostgreSQL, reads authentication storage, or owns an
`/api/**` route. It uses REST for both server-rendered reads and browser interactions.

## Add behavior test-first

1. Add a failing Domain or Application unit test for the business rule.
2. Add a failing API test through `WebApplicationFactory` for HTTP validation, authorization,
   status, headers, and response shape.
3. Implement the smallest inward-layer change and a thin Minimal API endpoint.
4. Export `contracts/openapi/v1.json` and regenerate the TypeScript SDK.
5. Add or update the Next.js adapter and its focused Jest test.
6. Add Playwright only for the complete browser workflow that needs it.

Keep ports owned by Application and their external implementations in Infrastructure. A React
component must not reproduce an Application or Domain authorization rule; client validation is only
a usability aid, and the API remains authoritative.

## Cross-slice dependencies

Do not make one feature reach into another feature's internal store or endpoint boundary. Share a
domain concept, an Application port/use case, or an explicit API contract instead. Keep changes
small enough that the owning tests and public documentation identify the capability clearly.

## Related pages

- [REST boundary instead of Server Actions](/docs/developers/server-actions)
- [Add an API v1 endpoint](/docs/developers/api-v1-endpoint)
- [Application shell](/docs/application)
- [How to write documentation](/docs/general/authoring/how-to-write-docs)

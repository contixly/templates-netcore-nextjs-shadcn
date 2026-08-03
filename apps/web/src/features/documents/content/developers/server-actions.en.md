---
title: "Server actions"
description: "Legacy Server Action guidance and the target REST boundary between the ASP.NET Core API and the separate Next.js UI."
group: "For developers"
groupOrder: 300
parentItem: "Project development"
parentItemOrder: 100
order: 30
toc: true
purpose: "Developer how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Server actions

This canonical route is retained so existing links remain valid. Its former full-stack Next.js
pattern is legacy guidance: the target application has no Server Actions for product reads or
mutations. ASP.NET Core owns every `/api/**` operation, and the separate Next.js UI communicates
with it over REST.

## Why the boundary changed

A single REST boundary gives browser code, server rendering, automated tests, and external
consumers one observable contract. ASP.NET Core remains the authority for validation,
authentication, authorization, business use cases, persistence, rate limits, and Problem Details.
OpenAPI records that contract, and the generated TypeScript SDK keeps the UI aligned with it.

Putting a mutation in a Server Action would create a second server-side application boundary,
bypass the committed OpenAPI contract, and risk duplicating authorization or data-access rules.
Next.js therefore contains no Prisma, Better Auth, direct database access, or product API Route
Handlers either.

## Read through REST

Server Components create an isolated generated-SDK client with the server-only
`API_INTERNAL_BASE_URL`. A loader forwards only approved cookie and correlation context. A
cookie-bearing safe projection may add the narrow session-renewal suppression marker because a
Server Component cannot deliver the API's renewal cookie to the browser.

Client Components use a relative same-origin client with `credentials: "same-origin"`. In local
development, `API_PROXY_TARGET` enables the Next.js rewrite to ASP.NET Core; in the final
same-origin topology ASP.NET Core owns `/api/**` directly.

## Mutate through REST

For a browser mutation:

1. obtain a fresh request token from `GET /api/v1/auth/csrf`;
2. call the generated SDK operation with `X-CSRF-TOKEN`;
3. treat the API response as authoritative;
4. normalize Problem Details to a safe UI result;
5. refresh or reconcile with generated GET operations without repeating a committed mutation.

The secure HttpOnly session cookie travels automatically. JavaScript never reads it and never
stores a bearer token. Machine integrations use the documented `x-api-key` contract instead of the
browser-session flow.

## Migrating a legacy action

Write a failing Application/API test for the behavior, move its business rules into Domain or
Application, put persistence behind an Application port, and expose a thin Minimal API operation in
`Template.Api`. Export OpenAPI, regenerate the SDK, then replace the Server Action caller with a
server or browser REST adapter. Remove legacy transport types and direct database imports rather
than maintaining both paths.

## Related pages

- [Feature slice architecture](/docs/developers/feature-slice)
- [Add an API v1 endpoint](/docs/developers/api-v1-endpoint)
- [Runtime security](/docs/application/runtime-security)
- [API v1 reference](/docs/api/api-v1)

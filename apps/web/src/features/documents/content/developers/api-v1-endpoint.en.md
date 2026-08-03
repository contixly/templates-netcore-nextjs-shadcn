---
title: "Add an API v1 endpoint"
description: "Add a test-first ASP.NET Core Minimal API endpoint, publish its OpenAPI contract, and consume it through the generated REST SDK."
group: "For developers"
groupOrder: 300
parentItem: "API development"
parentItemOrder: 80
order: 10
toc: true
purpose: "Developer how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Add an API v1 endpoint

Use this flow for a new operation under ASP.NET Core's `/api/v1` surface. Decide first whether the
operation is for a browser session, an API key, either credential, or anonymous use. That decision
selects the endpoint group and security contract; do not create a Next.js Route Handler.

## Start with failing tests

1. Add an Application test under `apps/api/tests/Template.Application.Tests` for the use case or
   domain rule.
2. Add an API test under `apps/api/tests/Template.Api.Tests` using the shared
   `ApiWebApplicationFactory`/`WebApplicationFactory` host.
3. Assert the observable contract: method and path, credential mode, validation, authorization,
   success envelope, Problem Details, headers, and safe failure behavior.
4. Run the focused test and keep its failure as the RED evidence before implementation.

A route that changes API-key scope or tenant behavior also needs the corresponding permission,
principal, isolation, and rate-limit coverage. Do not infer those rules only from UI behavior.

## Implement the inward slice

Add a Domain rule or value object only when the concept is domain-wide. Put orchestration and ports
in `Template.Application`, then implement external I/O behind those ports in
`Template.Infrastructure`. Keep transport contracts and boundary translation in
`Template.Api/Features/{Capability}`.

Map the operation in the capability's `IEndpointModule` through the appropriate
`EndpointRouteContext` group:

- `VersionedApi` for browser-session operations;
- `VersionedMixedApi` for explicitly supported browser-or-machine reads;
- `VersionedMachineApi` for API-key-only operations;
- an explicit `AllowAnonymous()` only for a deliberately public operation.

The Minimal API handler validates and authorizes at the HTTP boundary, calls Application, and maps
the result. It does not contain persistence or business rules.

## Describe the HTTP contract

Successful JSON uses the typed `{ "data": ... }` envelope. Failures use
`application/problem+json` with a stable `code` and safe `traceId`; validation adds `errors`. Add a
unique operation name and exact `Produces` metadata so OpenAPI describes every response and security
mode. Unsafe browser operations also use the existing CSRF endpoint/filter contract.

Update [API conventions](/docs/api/api-v1) when the public consumer behavior changes.

## Export OpenAPI and regenerate the SDK

From the repository root:

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
```

Review and commit `contracts/openapi/v1.json`. Then run from `apps/web`:

```bash
npm run api:generate
npm run api:check
```

`api:generate` replaces the committed `src/lib/api/generated` tree. Never edit generated files by
hand. `api:check` regenerates and byte-compares the tree.

## Connect the Next.js UI

Call the generated operation from a focused adapter under `apps/web/src/lib/api`. Browser clients
use same-origin credentials; SSR clients use `API_INTERNAL_BASE_URL` and explicitly allow-listed
forwarded context. Browser mutations obtain a fresh CSRF token. Do not use raw `fetch`, handwritten
transport DTOs, Server Actions, direct database access, or bearer tokens in browser storage.

Finish with the focused tests, full .NET suite, generated-contract checks, relevant Jest tests, and
a Playwright scenario only when the operation participates in a browser workflow.

## Related pages

- [API v1 reference](/docs/api/api-v1)
- [Feature slice architecture](/docs/developers/feature-slice)
- [REST boundary instead of Server Actions](/docs/developers/server-actions)
- [Local automation and E2E](/docs/developers/local-automation-e2e)

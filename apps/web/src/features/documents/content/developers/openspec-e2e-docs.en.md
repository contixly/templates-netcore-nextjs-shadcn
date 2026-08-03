---
title: "OpenSpec, E2E, and docs"
description: "Use tests, contracts, browser scenarios, and paired public documentation together; OpenSpec remains initialized-only until explicitly adopted."
group: "For developers"
groupOrder: 300
parentItem: "Quality workflow"
parentItemOrder: 70
order: 10
toc: true
purpose: "Developer workflow reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# OpenSpec, E2E, and docs

Keep requirements, implementation, tests, generated contracts, and public documentation aligned.
The repository currently uses the migration plan and durable files under `docs/` for approved
architecture, API, security, operations, and migration decisions.

## Current OpenSpec state

OpenSpec is initialized, but `openspec/specs` and the active `openspec/changes` area contain no
capability or change. Do not create an active OpenSpec artifact as routine feature work. Create one
only when an explicit request or project decision adopts OpenSpec for that change.

Until then, use the accepted task or design as the requirement source and record any new durable
forward-looking decision in the appropriate file under `docs/` in the same change.

## Drive implementation with tests

Start with the narrowest failing test that proves the requested behavior:

1. Domain/Application tests for rules and use cases;
2. API tests through `WebApplicationFactory` for HTTP behavior and persistence integration;
3. OpenAPI contract assertions for operation shape and security;
4. web adapter/component Jest tests for UI behavior;
5. Playwright for a complete browser workflow.

Capture the RED failure before implementation. Run the focused GREEN test after the smallest change,
then the full relevant suites. Do not use a broad E2E test as the only evidence for a business or
HTTP boundary rule.

## Keep generated contracts current

An endpoint change is not complete when only C# passes. Export and review
`contracts/openapi/v1.json`, run `npm run api:generate`, and verify `npm run api:check`. Next.js must
consume the generated operation and DTOs through its REST adapters.

Documentation changes regenerate both the web registry and the API-embedded search index:

```bash
cd apps/web
npm run content:generate
npm run content:check
npm run content:test
```

Never hand-edit either generated documentation artifact.

## Add E2E at the supported boundary

Playwright specifications live directly under `apps/web/e2e`; reusable helpers live under
`apps/web/e2e/support`. Use the ASP.NET Core local-automation flow and generated REST SDK helpers.
Arrange through supported API behavior, avoid direct database mutation, and keep secret-bearing
artifacts disabled where the scenario handles reveal-once credentials.

## Update public documentation

When behavior visible to users or API consumers changes, update the closest canonical page in
`apps/web/src/features/documents/content`. Published pages require paired `.en` and `.ru` variants.
Preserve canonical `/docs/...` links, run the content compiler, and include release or weekly history
only when the change is actually published.

## Related pages

- [Feature slice architecture](/docs/developers/feature-slice)
- [Local automation and E2E](/docs/developers/local-automation-e2e)
- [How to write documentation](/docs/general/authoring/how-to-write-docs)
- [Releases and changelog](/docs/developers/releases-changelog)

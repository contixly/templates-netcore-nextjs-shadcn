---
title: "For developers"
description: "How to extend the ASP.NET Core REST API and separate Next.js UI while keeping contracts, tests, and public documentation aligned."
group: "For developers"
groupOrder: 300
parentItem: "Project development"
parentItemOrder: 100
order: 10
toc: true
purpose: "Developer overview"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# For developers

Use this section when extending a service built from the template. The repository has two runtime
applications with one explicit boundary: ASP.NET Core owns `/api/**`, authentication, authorization,
business use cases, and persistence; Next.js owns rendering and browser interaction.

## Repository layout

| Path                                   | Responsibility                                                                               |
| -------------------------------------- | -------------------------------------------------------------------------------------------- |
| `apps/api/src/Template.Domain`         | Domain value objects and rules with no HTTP or infrastructure dependencies.                  |
| `apps/api/src/Template.Application`    | Use cases and ports; depends only on Domain.                                                 |
| `apps/api/src/Template.Infrastructure` | Persistence, Identity, OAuth client, cryptography, and other port implementations.           |
| `apps/api/src/Template.Api`            | The only HTTP host; owns Minimal API endpoints and boundary validation and authorization.    |
| `apps/api/tests`                       | Application unit tests, API tests through `WebApplicationFactory`, and the E2E orchestrator. |
| `apps/web`                             | Separate Next.js UI that consumes the generated REST SDK.                                    |
| `contracts/openapi/v1.json`            | Committed API contract used to generate the TypeScript SDK.                                  |

Dependencies point inward: Api and Infrastructure may depend on Application, Application depends
only on Domain, and Domain depends on neither HTTP nor infrastructure. The web app never imports
backend code or accesses the database or authentication store.

## Development model

Start behavior changes with a failing focused test. Put business rules in Domain or Application,
implement external concerns behind Application ports in Infrastructure, and keep Minimal API
handlers thin. Then export OpenAPI, regenerate the SDK, and connect the Next.js feature through the
generated operation.

The browser keeps sessions in secure HttpOnly cookies. Browser mutations obtain a fresh CSRF token;
they do not store bearer tokens. Server-rendered calls use the server-only API origin and forward
only explicitly allowed request context.

## What to keep aligned

A user-visible or consumer-visible change can affect several committed surfaces:

- Domain/Application behavior and its focused tests;
- the ASP.NET Core endpoint and `WebApplicationFactory` coverage;
- `contracts/openapi/v1.json` and `apps/web/src/lib/api/generated`;
- Next.js adapters, components, Jest tests, and Playwright scenarios;
- English and Russian public documentation plus release history when published.

OpenSpec is initialized but intentionally has no active capability or change. Do not create one
unless a project decision or explicit request adopts that workflow.

## Start here

- [Feature slice architecture](/docs/developers/feature-slice)
- [REST boundary instead of Server Actions](/docs/developers/server-actions)
- [Add an API v1 endpoint](/docs/developers/api-v1-endpoint)
- [Requirements, E2E, and docs](/docs/developers/openspec-e2e-docs)
- [Local automation and E2E](/docs/developers/local-automation-e2e)
- [Releases and changelog](/docs/developers/releases-changelog)

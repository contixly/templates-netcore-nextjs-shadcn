---
title: "API access"
description: "How the template exposes machine access with personal and workspace API keys."
group: "API and integrations"
groupOrder: 700
parentItem: "Overview"
parentItemOrder: 100
order: 10
toc: true
purpose: "API user documentation"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# API access

ASP.NET Core is the only HTTP host for `/api/**`. The Next.js UI calls it through the generated REST SDK and never owns API routes, sessions, business logic, or database access.

## Credential boundaries

| Caller         | Credential                                 | Surface                                                      |
| -------------- | ------------------------------------------ | ------------------------------------------------------------ |
| Browser UI     | Secure HttpOnly same-origin session cookie | Account, organization, collaboration, and API-key management |
| Machine client | Exactly one `x-api-key` header             | Supported read-only `/api/v1` operations                     |

Browser JavaScript never reads the cookie or stores a bearer token. API-key management is a browser-session operation; create, update, rotate, and revoke also fetch a fresh token from `GET /api/v1/auth/csrf` and send `X-CSRF-TOKEN`. An API key cannot manage keys.

Some organization reads accept either credential. If `x-api-key` is present, API-key authentication is selected exclusively; a valid cookie cannot rescue an invalid key.

## Current machine surface

Iteration 7 supports `GET /api/v1/me`, organization list/detail, organization members, teams, and team members. Successful JSON uses `{ "data": ... }`. Failures use RFC Problem Details as `application/problem+json`; branch on HTTP status and stable `code`.

## Client rule

Application adapters call generated SDK operations and import generated DTOs. Do not add raw `fetch`, handwritten transport types, Server Actions, API Route Handlers, or direct database access. `contracts/openapi/v1.json` is the committed contract and generated output is checked deterministically.

## Related pages

- [Manage API keys](/docs/api/api-keys)
- [API v1 reference](/docs/api/api-v1)
- [Permissions and rate limits](/docs/api/permissions-rate-limits)
- [Add an API v1 endpoint](/docs/developers/api-v1-endpoint)

---
title: "API v1 reference"
description: "Starter read-only API routes, authentication header, success envelope, and common errors."
group: "API and integrations"
groupOrder: 700
parentItem: "API reference"
parentItemOrder: 80
order: 10
toc: true
purpose: "API reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# API v1 reference

The machine surface is read-only. Send exactly one nonblank key in `x-api-key`; never place it in a URL, browser storage, source control, logs, or captured artifacts.

```bash
curl -H "x-api-key: $API_KEY" "$API_ORIGIN/api/v1/me"
```

## Supported reads

| Method and path                                                     | Required scopes                                       | Credential modes  |
| ------------------------------------------------------------------- | ----------------------------------------------------- | ----------------- |
| `GET /api/v1/me`                                                    | `basic:read`                                          | API key only      |
| `GET /api/v1/organizations`                                         | `organization:read`                                   | Cookie or API key |
| `GET /api/v1/organizations/{organizationId}`                        | `organization:read`                                   | API key only      |
| `GET /api/v1/organizations/{organizationId}/members`                | `organization:read` + `member:read`                   | Cookie or API key |
| `GET /api/v1/organizations/{organizationId}/teams`                  | `organization:read` + `team:read`                     | Cookie or API key |
| `GET /api/v1/organizations/{organizationId}/teams/{teamId}/members` | `organization:read` + `team:read` + `teamMember:read` | Cookie or API key |

On mixed routes, presence of `x-api-key` selects API-key authentication exclusively; a cookie cannot rescue an invalid key.

## Envelopes and cursors

Success uses `{ "data": ... }`. Collections use `{ "data": { "items": [], "nextCursor": null } }`; `limit` defaults to `50` and accepts `1..100`. Return `nextCursor` unchanged as `cursor`. Cursors are opaque, versioned, and collection-specific: never decode, edit, synthesize, or reuse them.

A personal key acts as its user and current membership is rechecked per request. An organization key can access only its owning tenant.

## Problem Details

Failures use `application/problem+json`, not an `error` envelope. Required fields include RFC Problem Details plus stable `code` and safe `traceId`; validation also adds `errors`. Branch on status and `code`.

| Status | Typical code                                              | Action                                  |
| ------ | --------------------------------------------------------- | --------------------------------------- |
| `400`  | `invalid_cursor`                                          | Correct the input.                      |
| `401`  | `api_key_missing`, `api_key_invalid`                      | Supply or replace the key.              |
| `403`  | `api_key_permission_denied`, `organization_access_denied` | Correct scopes or tenant access.        |
| `404`  | Resource not-found code                                   | The authorized target is absent.        |
| `429`  | `api_key_rate_limited`                                    | Wait the integer `Retry-After` seconds. |

## Related pages

- [Manage API keys](/docs/api/api-keys)
- [Permissions and rate limits](/docs/api/permissions-rate-limits)
- [Add an API v1 endpoint](/docs/developers/api-v1-endpoint)

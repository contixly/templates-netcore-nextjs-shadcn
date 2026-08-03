---
title: "Manage API keys"
description: "Create, edit, and delete personal and workspace API keys safely."
group: "API and integrations"
groupOrder: 700
parentItem: "Key management"
parentItemOrder: 90
order: 10
toc: true
purpose: "API key how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Manage API keys

API keys authenticate machines to the supported read-only `/api/v1` surface. Management stays behind the browser's secure HttpOnly session and fresh CSRF protection.

## Owners and routes

| Owner         | Browser page                             | REST collection                                   |
| ------------- | ---------------------------------------- | ------------------------------------------------- |
| Personal user | `/user/api-keys`                         | `/api/v1/account/api-keys`                        |
| Organization  | `/w/{organizationKey}/settings/api-keys` | `/api/v1/organizations/{organizationId}/api-keys` |

Every authenticated user can manage personal keys. Organization management requires trusted `canManageApiKeys`. An organization key stays bound to that organization, independent of the creator's later membership.

## Create and store

Creation requires a name, one or more closed presets, an expiry, and explicit fixed-window settings. UI suggestions are conveniences; the REST body is authoritative.

Only successful create and rotate responses reveal the raw credential, exactly once. Copy it to an approved secrets manager before closing the view. Never put it in source control, URLs, browser storage, logs, analytics, screenshots, or traces.

The service stores only a SHA-256 credential hash, safe metadata, and a non-secret 16-character `start` prefix. Lists, updates, revocation, `/api/v1/me`, and resource reads never return the raw key or hash.

## Update, rotate, and revoke

Updates can change name, presets, expiry, enabled state, or rate-limit settings. Omitted fields remain; a no-op returns `409 api_key_update_unchanged`.

Rotation keeps the logical ID and configuration, atomically invalidates the old credential, resets the current window/count, preserves `lastRequestAt`, and reveals the replacement once. Revocation invalidates and removes the key from later lists; repetition returns `404 api_key_not_found`.

Unsafe management calls first fetch `GET /api/v1/auth/csrf` and send `X-CSRF-TOKEN` with the session cookie. API keys cannot call management routes.

## Related pages

- [API access](/docs/api)
- [API v1 reference](/docs/api/api-v1)
- [Permissions and rate limits](/docs/api/permissions-rate-limits)
- [Workspace settings](/docs/workspace/settings)

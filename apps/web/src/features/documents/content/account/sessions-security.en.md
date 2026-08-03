---
title: "Sessions and security"
description: "Review active sessions, revoke access safely, and understand the template's account security boundaries."
group: "Account"
groupOrder: 900
parentItem: "Security"
parentItemOrder: 70
order: 10
toc: true
purpose: "Account security how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Sessions and security

ASP.NET Core persists browser sessions server-side and exposes only safe metadata. The secret key remains in the secure HttpOnly cookie; raw tickets and hashes never enter the REST projection.

## Review sessions

`/user/security` calls `GET /api/v1/account/sessions?limit=20`. Items contain opaque session id, authentication method, current marker, timestamps, and available IP/user-agent data. Results are newest-first. Pages use `nextCursor`; `limit` defaults to `20` and is bounded to `1..100`.

The cursor is an opaque versioned continuation with corruption detection. Pass it back unchanged, derive no identity from it, and restart at page one after `invalid_cursor`.

## Revoke access

- `DELETE /api/v1/account/sessions/{sessionId}` revokes one owned non-current session.
- `DELETE /api/v1/account/sessions/others` removes all others while preserving the current session.

Both require the cookie and fresh CSRF. After revoke-others, the UI reloads page one from authoritative API state.

## Security boundary

Reads use `no-store`; authorization comes from the authenticated cookie, never a browser-supplied user id. Cursor checksum is not authorization: ownership is checked independently.

## Related pages

- [Profile and connections](/docs/account/profile-connections)
- [Runtime security](/docs/application/runtime-security)
- [API v1](/docs/api/api-v1)

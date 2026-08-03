---
title: "Account settings"
description: "How users manage profile details, connected providers, sessions, invitations, API keys, and account deletion."
group: "Account"
groupOrder: 900
parentItem: "Overview"
parentItemOrder: 100
order: 10
toc: true
purpose: "Account user documentation"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Account settings

Account settings are the signed-in user's global management area. ASP.NET Core owns the account and authentication APIs; Next.js uses the generated REST client. The browser proves its session with the secure same-origin `__Host-template.session` HttpOnly cookie, never a bearer token in browser storage or a direct database read.

## Account sections

| Section     | Route               | REST surface                                                            |
| ----------- | ------------------- | ----------------------------------------------------------------------- |
| Profile     | `/user/profile`     | `GET /api/v1/account` and `PATCH /api/v1/account/profile`.              |
| Connections | `/user/connections` | Connection list, external challenge, and provider disconnect endpoints. |
| Security    | `/user/security`    | Session list and single/other-session revoke endpoints.                 |
| Invitations | `/user/invitations` | `GET /api/v1/account/invitations`.                                      |
| API keys    | `/user/api-keys`    | Separately documented personal API-key management for `/api/v1`.        |
| Danger      | `/user/danger`      | Confirmed `DELETE /api/v1/account`.                                     |

All account projections use `Cache-Control: no-store`. Unsafe operations obtain a fresh token from `GET /api/v1/auth/csrf` and send it with the cookie; the API authenticates, validates, and authorizes at the HTTP boundary.

## Current implementation state

Iterations 3-4 implement Identity-backed persistent sessions, the account lifecycle, and five external OAuth providers. Production password registration/reset/change, 2FA, and manual email verification are intentionally not implemented. Provider access and refresh tokens are not persisted, and local disconnect does not revoke remote consent.

Use account settings for personal identity and access. Use workspace settings for organization members, teams, invitations, or domains.

## Related pages

- [Profile and connections](/docs/account/profile-connections)
- [Sessions and security](/docs/account/sessions-security)
- [Personal API keys](/docs/api/api-keys)
- [Invitations](/docs/workspace/invitations)

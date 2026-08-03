---
title: "Delete account"
description: "Understand the destructive account deletion surface and the confirmation required before it runs."
group: "Account"
groupOrder: 900
parentItem: "Danger zone"
parentItemOrder: 10
order: 10
toc: true
purpose: "Account deletion how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Delete account

`/user/danger` drives the implemented irreversible hard-delete REST flow, not soft-delete or background cleanup.

## Before deletion

Review providers, sessions, organizations, teams, invitations, and API keys. In the deletion transaction, an organization where the user is the only member may be deleted; safe memberships may be removed; deletion is blocked when the user is sole owner of a multi-member organization. The UI gives transfer/share-owner guidance only for that exact blocker.

## Delete the account

1. Open `/user/danger`.
2. Enter the current primary email exactly.
3. Send `DELETE /api/v1/account` with `{ "confirmationEmail": "..." }` and fresh CSRF.
4. After success, leave protected pages; ASP.NET Core expires the session cookie.

The API validates confirmation and authorization. The transaction removes the Identity user and dependent verified emails, external logins, sessions, safe organization memberships, and configured dependents through lifecycle stores and database cascades. The browser must not delete records directly.

## Out of scope

Export, retention windows, restore, and an ownership-transfer endpoint are not part of this flow. Products that need them must implement and document them before exposing deletion.

## Related pages

- [Account settings](/docs/account)
- [Personal API keys](/docs/api/api-keys)
- [Workspace settings](/docs/workspace/settings)

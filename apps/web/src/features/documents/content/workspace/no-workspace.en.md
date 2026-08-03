---
title: "Users without a workspace"
description: "How the onboarding guard keeps workspace creation and invitation review available when no workspace is accessible."
group: "Workspace"
groupOrder: 800
parentItem: "Onboarding"
parentItemOrder: 30
order: 10
toc: true
purpose: "Workspace onboarding explanation"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Users without a workspace

A signed-in user may have zero accessible organizations: a new user, removed member, or invitation recipient not yet accepted. This is a normal API state.

## What the guard does

The guard calls `GET /api/v1/organizations` through the generated client. An empty first page sends the user to `/welcome`, which keeps available:

- first workspace creation through `POST /api/v1/organizations`;
- pending invitations through `GET /api/v1/account/invitations`;
- global account settings.

Creation atomically adds owner membership and active organization. Invitation acceptance atomically adds membership, optional team, and the same session preference.

## What remains available

Global account and workspace-management pages remain available without organization context. Invitation detail still applies matching-primary-email non-disclosure.

## Dashboard behavior

`/dashboard` resolves server-side session preference against accessible organizations. If absent/stale it uses a deterministic accessible organization; if none, `/welcome`. Deep links validate `GET /api/v1/organizations/by-key/{organizationKey}`.

The browser must not fabricate organizations, insert memberships, cache access in local storage, or use direct database/SQL setup. A gated Development/Test email-confirmation REST helper exists only for deterministic automation, not production verification. Real invitation email is out of scope.

## Related pages

- [Create and switch workspaces](/docs/workspace/create-switch)
- [Invitations](/docs/workspace/invitations)
- [Account settings](/docs/account)

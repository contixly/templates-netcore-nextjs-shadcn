---
title: "Create and switch workspaces"
description: "Create workspaces, understand workspace URLs, and switch context without losing safe navigation."
group: "Workspace"
groupOrder: 800
parentItem: "Workspace lifecycle"
parentItemOrder: 90
order: 10
toc: true
purpose: "Workspace how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Create and switch workspaces

`/workspaces` is backed by generated organization REST calls. It does not keep a private browser access list.

## Create a workspace

1. Open `/workspaces` or `/welcome`.
2. Enter a name of 1-50 supported Unicode letters/digits, spaces, hyphens, or underscores.
3. Send `POST /api/v1/organizations` with fresh CSRF.

ASP.NET Core generates a unique canonical slug and atomically creates the organization, owner membership, and current session preference. Follow the canonical key in the response.

## Open a workspace

`GET /api/v1/organizations?limit=50` returns accessible items and `nextCursor`; limits are `1..100` and cursors opaque. Routes use `/w/:organizationKey/...`. A UUID or old slug resolves through `GET /api/v1/organizations/by-key/{organizationKey}` and redirects to the current canonical slug when accessible.

## Switch workspace context

Selection sends `PUT /api/v1/auth/session/active-organization` with UUID and fresh CSRF. The preference lives on the server-side session. Later `/dashboard` uses it if accessible; the browser does not write organization access to local storage.

## Delete a workspace

Only an owner may send `DELETE /api/v1/organizations/{organizationId}` with the exact case-sensitive `confirmationName` and CSRF. Deletion clears referencing session preferences and does not silently choose another workspace.

## Related pages

- [Workspace](/docs/workspace)
- [Workspace settings](/docs/workspace/settings)
- [Users without a workspace](/docs/workspace/no-workspace)

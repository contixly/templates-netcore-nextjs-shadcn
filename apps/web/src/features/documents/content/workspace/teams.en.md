---
title: "Teams"
description: "Use explicit workspace teams for subgroups without changing the workspace membership model."
group: "Workspace"
groupOrder: 800
parentItem: "Teams"
parentItemOrder: 50
order: 10
toc: true
purpose: "Workspace team reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Teams

Teams are subgroups inside an organization. They do not replace organization membership/roles/routing, and sessions have no active-team value.

## Team model

Organizations may have zero teams. Names are unique case-insensitively per organization and contain 1-50 supported Unicode letters/digits, spaces, hyphens, or underscores. Team membership references an existing membership in the same organization.

Every member may read teams; owner/admin manage them and composition. Member is read-only. Missing/foreign resources are non-disclosing.

## Manage teams

- `GET`/`POST /api/v1/organizations/{organizationId}/teams` list/create.
- `PATCH`/`DELETE /api/v1/organizations/{organizationId}/teams/{teamId}` rename/delete.
- `GET`/`POST .../teams/{teamId}/members` page/add organization members.
- `DELETE .../teams/{teamId}/members/{userId}` removes team membership.
- `GET .../teams/{teamId}/member-candidates?q=...` searches bounded candidates.

Lists use opaque cursors and `limit` `1..100` (default `50`); query is at most 100 characters. Unsafe calls require generated client, HttpOnly cookie, and fresh CSRF.

## Team-targeted invitations

Acceptance can atomically add organization and optional team membership. Deleting a team detaches historical invitation targets and deletes team membership, not organization membership. Active-team switching and team-specific roles are out of scope.

## Related pages

- [Members and roles](/docs/workspace/members-roles)
- [Invitations](/docs/workspace/invitations)
- [Workspace settings](/docs/workspace/settings)

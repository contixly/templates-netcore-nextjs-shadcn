---
title: "Members and roles"
description: "Workspace member directory, built-in roles, direct member addition, and role updates."
group: "Workspace"
groupOrder: 800
parentItem: "Members"
parentItemOrder: 70
order: 10
toc: true
purpose: "Workspace user management reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Members and roles

Membership has one role from closed `owner | admin | member`. ASP.NET Core returns capabilities and repeats authorization for every REST operation.

## Built-in roles

| Role     | Authorization                                                                                                   |
| -------- | --------------------------------------------------------------------------------------------------------------- |
| `owner`  | Update/delete organization, add members, assign any role, manage teams/invitations/keys.                        |
| `admin`  | Update, add members, assign `member`/`admin`, manage teams/invitations/API keys; cannot delete or create owner. |
| `member` | Read-only safe organization, member, and team context.                                                          |

Self/no-op changes, admin changes to owners, and loss of the last owner are blocked. Team membership grants no organization role. Custom roles are absent.

Admins and owners can manage API keys. This capability does not let an admin delete the organization or assign the `owner` role.

## Member directory

`GET /api/v1/organizations/{organizationId}/members?limit=50` returns `nextCursor`; limits are `1..100`, cursors opaque. Items include role, joined time, verified email/domain, and outside-policy marker. Missing/foreign organizations are non-disclosing.

## Add and update

Managers use `POST /api/v1/organizations/{organizationId}/members` with existing `userId`, allowed role, and required domain acknowledgement. Role change uses `PATCH /api/v1/organizations/{organizationId}/members/{memberId}` against current locked state. Both require HttpOnly session and fresh CSRF.

Organization-member removal is intentionally not implemented. Invitations onboard new users; direct add is only for existing accounts.

## Related pages

- [Email domains](/docs/workspace/email-domains)
- [Invitations](/docs/workspace/invitations)
- [Teams](/docs/workspace/teams)

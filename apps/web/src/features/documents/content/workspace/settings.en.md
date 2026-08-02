---
title: "Workspace settings"
description: "Workspace settings sections, permissions, and the shared settings shell."
group: "Workspace"
groupOrder: 800
parentItem: "Settings"
parentItemOrder: 80
order: 10
toc: true
purpose: "Workspace settings reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Workspace settings

Settings are REST-backed views. The shell resolves `GET /api/v1/organizations/by-key/{organizationKey}` and uses returned role/capabilities; client visibility is not authorization.

## Settings sections

| Section     | Route                                      | Current behavior                                          |
| ----------- | ------------------------------------------ | --------------------------------------------------------- |
| Workspace   | `/w/:organizationKey/settings/workspace`   | `PATCH`/`DELETE /api/v1/organizations/{organizationId}`.  |
| Users       | `/w/:organizationKey/settings/users`       | Page/add existing members and change allowed roles.       |
| Invitations | `/w/:organizationKey/settings/invitations` | Page/filter activity and create 48-hour invitations.      |
| Teams       | `/w/:organizationKey/settings/teams`       | Page teams and manage composition.                        |
| Roles       | `/w/:organizationKey/settings/roles`       | Fixed role explanation; custom roles are not implemented. |
| API keys    | `/w/:organizationKey/settings/api-keys`    | Separately documented organization key management.        |

The root redirects to the first available section after access validation.

## Update and delete rules

PATCH accepts dirty fields and rejects an empty/no-op body. Names trim to 1-50 supported characters. Slugs normalize to at most 64 lowercase ASCII letters/digits separated by single hyphens and cannot be UUID-shaped. UI sends only changes against latest authoritative detail.

Deletion is owner-only and requires exact case-sensitive current name in `confirmationName`. Inaccessible resources are non-disclosing.

## Security and scope

Every mutation uses the generated client, secure HttpOnly cookie, and fresh CSRF. Iterations 5-6 implement organization/collaboration settings. Custom roles, member removal, active-team selection, and invitation cancel/resend remain out of scope.

## Related pages

- [Members and roles](/docs/workspace/members-roles)
- [Invitations](/docs/workspace/invitations)
- [Teams](/docs/workspace/teams)
- [Settings shell](/docs/application/settings-shell)

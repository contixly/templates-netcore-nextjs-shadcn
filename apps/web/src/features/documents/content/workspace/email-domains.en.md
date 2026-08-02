---
title: "Email domains"
description: "Restrict workspace invitations by email domain and surface warnings for existing out-of-policy members."
group: "Workspace"
groupOrder: 800
parentItem: "Access policy"
parentItemOrder: 40
order: 10
toc: true
purpose: "Workspace access policy reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Email domains

Allowed domains are organization settings enforced by ASP.NET Core when members are added or invitations created. The browser does not implement policy through a database shortcut.

## Configure allowed domains

`PATCH /api/v1/organizations/{organizationId}` sends `allowedEmailDomains`. Values are trimmed, lowercased, and stripped of one leading `@`; duplicates collapse. An exact DNS-like domain has at least two labels, ASCII letters/digits/internal hyphens, and at most 253 characters. At most 100 distinct normalized domains are accepted. Empty list disables restriction; `example.com` does not imply `sub.example.com`.

## Invitation checks

`POST /api/v1/organizations/{organizationId}/invitations` rejects an address outside the list, with no acknowledgement override. Existing pending invitations are not rewritten, but acceptance rechecks current policy.

## Direct member add checks

`POST /api/v1/organizations/{organizationId}/members` includes `userId`, `role`, and optional `acknowledgeDomainRestriction`. For an outside or unrecognized verified-email domain, a manager reviews the warning and retries with explicit acknowledgement set to `true`.

Mutations require secure HttpOnly cookie and fresh CSRF. Existing members remain visible and are marked outside-policy; settings changes do not remove them.

## Related pages

- [Members and roles](/docs/workspace/members-roles)
- [Invitations](/docs/workspace/invitations)
- [Workspace settings](/docs/workspace/settings)

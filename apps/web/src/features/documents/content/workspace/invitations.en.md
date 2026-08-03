---
title: "Invitations"
description: "Create workspace invitations, share links, accept or reject invitations, and target teams."
group: "Workspace"
groupOrder: 800
parentItem: "Invitations"
parentItemOrder: 60
order: 10
toc: true
purpose: "Workspace invitation how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Invitations

ASP.NET Core invitation endpoints enforce verified-email ownership, roles, domain policy, expiry, and non-disclosure.

## Create an invitation

Owner/admin sends `POST /api/v1/organizations/{organizationId}/invitations` with email, assignable role, and optional same-organization team UUID. Admin may invite `member`/`admin`; owner may also invite `owner`. API enforces domain, duplicate, existing-member, pending-cap, authorization, rate limit, and CSRF rules.

`GET /api/v1/organizations/{organizationId}/invitations` accepts `status`, opaque `cursor`, and bounded `limit` (default `50`, `1..100`) for managers only.

## Invitation statuses

A new invitation expires exactly 48 hours after creation. Expiry is derived on read; there is no expiry worker. Display states are `pending`, `accepted`, `rejected`, `canceled`, or derived `expired`; cancel/resend is not exposed.

## Accept or reject an invitation

At `/invite/{invitationId}`, `GET /api/v1/invitations/{invitationId}` returns private detail only when current primary verified email matches. `POST .../accept` or `POST .../reject` requires fresh CSRF. Missing, foreign, or mismatched invitations disclose no recipient/organization data.

Accept atomically creates organization membership, optional team membership, accepted state, and active-organization preference. Reject changes only state. Expired/decided invitations cannot be decided again.

## Personal invitation list

`GET /api/v1/account/invitations` provides a bounded pending list to `/user/invitations` and `/welcome`. Current delivery is a relative same-origin path/manual-share fallback. Real email, outbox/retry, and cancel/resend UI are out of scope.

## Related pages

- [Email domains](/docs/workspace/email-domains)
- [Teams](/docs/workspace/teams)
- [Users without a workspace](/docs/workspace/no-workspace)

---
title: "Workspace"
description: "How the template models organization-backed workspaces, routes, members, teams, invitations, and settings."
group: "Workspace"
groupOrder: 800
parentItem: "Overview"
parentItemOrder: 100
order: 10
toc: true
purpose: "Workspace overview"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Workspace

A workspace is the user-facing view of an ASP.NET Core organization. Organizations, memberships, teams, invitations, and the active-session preference are persisted by the API; Next.js uses the generated REST client.

## Model

| Term                | Meaning                                                                     |
| ------------------- | --------------------------------------------------------------------------- |
| Organization        | Tenant boundary with UUID id, canonical slug, settings, and memberships.    |
| Workspace           | Organization UI under `/w/:organizationKey/...`.                            |
| Organization key    | UUID or slug for lookup; UI deep links canonicalize to the current slug.    |
| Membership          | User edge with exactly one `owner`, `admin`, or `member` role.              |
| Active organization | Nullable preference on the persistent session, not a browser or team token. |

`GET /api/v1/organizations` lists accessible organizations. `GET /api/v1/organizations/by-key/{organizationKey}` validates access and returns canonical key and capabilities. Missing and foreign resources are non-disclosing.

## What users can do

- create an organization/owner edge with `POST /api/v1/organizations`;
- select one with `PUT /api/v1/auth/session/active-organization`;
- update/delete according to capabilities;
- page and manage members, teams, and 48-hour invitations according to the fixed role matrix.

Iterations 5-6 implement organization, membership, onboarding, team, and invitation slices. Custom roles, organization-member removal, active-team context, invitation cancel/resend, and real email delivery are out of scope.

## Workspace documentation

- [Create and switch workspaces](/docs/workspace/create-switch)
- [Workspace settings](/docs/workspace/settings)
- [Members and roles](/docs/workspace/members-roles)
- [Invitations](/docs/workspace/invitations)
- [Teams](/docs/workspace/teams)
- [Email domains](/docs/workspace/email-domains)
- [Users without a workspace](/docs/workspace/no-workspace)

## Related pages

- [Account settings](/docs/account)
- [API v1](/docs/api/api-v1)

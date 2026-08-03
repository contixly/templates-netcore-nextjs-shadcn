---
title: "Releases"
description: "Published Next.js template releases and concise summaries of user-visible changes."
group: "History"
groupOrder: 100
parentItem: "Releases"
parentItemOrder: 10
order: 1000
status: "published"
toc: true
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-07"
---

# Releases

**Legacy record.** This page preserves history from the former full-stack Next.js reference application. Names, routes, dependencies, and instructions below describe that era; they are not current guidance. Every mention of Prisma, Better Auth, Server Actions, Next.js-owned API routes, or Redis/Valkey handlers is reference-era behavior. The migration moved API ownership, identity, business logic, and persistence to ASP.NET Core and left Next.js as a separate REST-only UI. See the current [application architecture](/docs/application) and [developer guidance](/docs/developers).

This section describes versions published by the former reference template. Release notes focus on changes that matter to
template users: new screens, auth flows, workspace behavior, API access, localization, security, and
quality checks.

## Latest reference release

- [v0.0.11](/docs/history/releases/0.0.11) - public documentation system, localized Markdown/MDX
  content, `/docs` navigation, documentation search, release and weekly changelog pages.

## Release archive

| Version                                  | Main theme                                                                             |
| ---------------------------------------- | -------------------------------------------------------------------------------------- |
| [v0.0.11](/docs/history/releases/0.0.11) | Public documentation system, localized content, and documentation search.              |
| [v0.0.10](/docs/history/releases/0.0.10) | E2E coverage and automation reliability.                                               |
| [v0.0.9](/docs/history/releases/0.0.9)   | Personal and organization API keys, `/api/v1`, scopes, expiration, and rate limits.    |
| [v0.0.8](/docs/history/releases/0.0.8)   | Local automation auth, Playwright E2E, and invitation policy hooks.                    |
| [v0.0.7](/docs/history/releases/0.0.7)   | Redis/Valkey-backed caching, streaming skeletons, and configured-only OAuth providers. |
| [v0.0.6](/docs/history/releases/0.0.6)   | Better Auth Teams, team-targeted invitations, and workspace team management.           |
| [v0.0.5](/docs/history/releases/0.0.5)   | Email-domain invitation restrictions and warnings for out-of-policy members.           |
| [v0.0.4](/docs/history/releases/0.0.4)   | Security hardening, safer redirects, invitation privacy, and session data protection.  |
| [v0.0.3](/docs/history/releases/0.0.3)   | Better Auth Organization-backed workspaces, invitations, roles, and settings surfaces. |
| [v0.0.2](/docs/history/releases/0.0.2)   | `next-intl` localization, translated UI, and localized metadata.                       |
| [v0.0.1](/docs/history/releases/0.0.1)   | Initial public foundation of the Next.js application template.                         |

## How to read the history

Use releases to understand larger published capabilities. For smaller week-by-week changes, see
[Weekly changes](/docs/history/change-logs). If you need deeper technical history, the repository
also keeps text release notes in `docs/releases/template`.

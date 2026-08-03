---
title: "Profile and connections"
description: "Update the display name and manage OAuth provider connections for the current account."
group: "Account"
groupOrder: 900
parentItem: "Profile and connections"
parentItemOrder: 80
order: 10
toc: true
purpose: "Account how-to"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Profile and connections

These pages are projections of the ASP.NET Core REST API; the browser never reads Identity tables or provider tokens.

## Update the display name

1. Open `/user/profile`; it loads `GET /api/v1/account`.
2. Edit the display name.
3. Save with `PATCH /api/v1/account/profile` and fresh CSRF.

The API trims and validates the value and returns the refreshed account. Primary and secondary emails are read-only here. The projection marks the primary address and providers that currently vouch for every verified email.

## Manage connected providers

`/user/connections` loads `GET /api/v1/account/connections`. The fixed catalogue is Google, GitHub, GitLab, VK, and Yandex. A `connect` challenge requires a current session and complete runtime provider credentials; `signIn` is the anonymous flow.

`DELETE /api/v1/account/connections/{provider}` is rejected for the current authentication provider and when no other connected, runtime-configured provider would survive. It removes the local Identity connection; no stored provider token exists to revoke remote consent.

## Verified email ownership

Provider-normalized emails are globally unique. An external subject has a stable owner. A new anonymous subject may link by email only while an existing provider still vouches for that exact verified address. Conflicts return safe Problem Details instead of merging accounts. Production password and manual email-management flows are out of scope.

## Related pages

- [Quick start](/docs/general/quick-start)
- [Sessions and security](/docs/account/sessions-security)
- [Runtime security](/docs/application/runtime-security)

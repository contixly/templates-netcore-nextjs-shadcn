---
title: "Permissions and rate limits"
description: "How API key permissions, presets, expiration, and rate limits control machine access."
group: "API and integrations"
groupOrder: 700
parentItem: "Permissions"
parentItemOrder: 70
order: 10
toc: true
purpose: "API reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Permissions and rate limits

API keys grant only iteration 7's closed read permissions. Clients select preset IDs and never submit arbitrary scopes.

## Closed presets

| Preset                           | Expanded scopes                                      |
| -------------------------------- | ---------------------------------------------------- |
| `basic-read`                     | `basic:read`                                         |
| `organization-read`              | `organization:read`                                  |
| `organization-members-read`      | `organization:read`, `member:read`                   |
| `organization-teams-read`        | `organization:read`, `team:read`                     |
| `organization-team-members-read` | `organization:read`, `team:read`, `teamMember:read`  |
| `organization-read-all`          | all organization/member/team/team-member read scopes |

At least one preset is required. IDs and scopes are case-sensitive closed sets. Unknown values and raw scopes are rejected. No machine write scopes or mutations exist today.

## Tenant isolation

A personal key is checked against its owner's current membership on every organization request. An organization key is bound to exactly one organization. Scopes never bypass this boundary. A team read without `teamMember:read` returns safe counts but no embedded member identities.

## Expiry and fixed windows

Expiry is `never`, `7d`, `30d`, `90d`, or `365d`. Rate limiting is optional per key: window `1m`, `1h`, or `1d`; maximum integer `1..1000000`. Every valid-key presentation consumes a unit even if later authorization fails.

A limited request returns `429 api_key_rate_limited`, `Cache-Control: no-store`, and integer `Retry-After` seconds from `1` through `86400`. Do not retry earlier. Invalid credentials do not reveal whether a key exists.

## Related pages

- [API access](/docs/api)
- [API v1 reference](/docs/api/api-v1)
- [Manage API keys](/docs/api/api-keys)

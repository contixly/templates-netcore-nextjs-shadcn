---
title: "Caching"
description: "Use the template's Cache Components and optional Redis or Valkey backed cache handlers."
group: "Application"
groupOrder: 500
parentItem: "Runtime"
parentItemOrder: 90
order: 20
toc: true
purpose: "Caching reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Caching

The current application favors correctness for session-dependent and mutable REST data. Cache Components are enabled for rendering, but authenticated API projections are not a shared application cache.

## Current no-store rules

Server API clients use `cache: "no-store"`. Auth, session, account, organization, collaboration, document-search, health, and API-key boundaries use `Cache-Control: no-store` where their mutable/request-specific contracts require it. Mutations render confirmed API results rather than invalidating a Next.js database cache.

Cookie-bearing SSR reads suppress sliding-session renewal because a Server Component cannot forward API `Set-Cookie`. One unmarked same-origin browser session read owns renewal so the secure HttpOnly cookie reaches the browser jar.

## Cache Components boundary

Runtime SSR starts below `connection()` and `Suspense`, after request headers and runtime configuration exist. Builds therefore do not require a live API, and cookies or `API_INTERNAL_BASE_URL` are not frozen into cached output. Static presentation and documentation may use framework rendering caches without caching private REST responses.

## Not implemented

There is no Redis/Valkey handler, remote cache configuration, repository cache-tag system, or cross-instance invalidation contract today. Redis/Aspire orchestration belongs to iteration 10. Add distributed caching only through a separate architecture decision with ownership, tenant-safe keys, invalidation behavior, and tests.

## Related pages

- [Application shell](/docs/application)
- [Runtime security](/docs/application/runtime-security)
- [Quick start](/docs/general/quick-start)

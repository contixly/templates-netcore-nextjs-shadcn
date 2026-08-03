---
title: "6-13 July 2026"
description: "Weekly update for 6-13 July 2026."
group: "History"
parentItem: "Weekly changes"
order: 600
status: "published"
toc: true
author: "Template Maintainers"
version: "1.0.0"
editedAt: "2026-07-07"
---

# Weekly update - 6-13 July 2026

**Legacy record.** This page preserves history from the former full-stack Next.js reference application. Names, routes, dependencies, and instructions below describe that era; they are not current guidance. Every mention of Prisma, Better Auth, Server Actions, Next.js-owned API routes, or Redis/Valkey handlers is reference-era behavior. The migration moved API ownership, identity, business logic, and persistence to ASP.NET Core and left Next.js as a separate REST-only UI. See the current [application architecture](/docs/application) and [developer guidance](/docs/developers).

## ✨ New features

- **Bilingual documentation library**: Added English and Russian pages for account management, API access, application settings, workspace flows, developer publishing guidance, and release history, so `/docs` now covers more everyday template tasks.

## 🔧 Improvements

- **Documentation navigation**: Refined the `/docs` shell so the sidebar and table of contents keep the current section clearer while readers move through long pages.

- **Documentation search relevance**: Short queries now match exact words more reliably, and multi-word queries keep a readable results layout when an exact phrase is not available.

## 🐛 Fixes

- **Code-example headings**: Lines inside tilde-fenced code examples no longer appear as real documentation headings, anchors, or search hits.

- **Documentation page structure**: Link, metadata, sidebar, and table-of-contents checks were tightened so public documentation pages render with cleaner navigation and fail earlier when a page points at an invalid target.

## 📝 Documentation

- Updated the documents-system OpenSpec requirements for localized pages, canonical links, search behavior, table of contents, metadata, and link validation.

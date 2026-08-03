---
title: "Localization"
description: "How the template uses the configured default locale for UI messages, metadata, and documentation content."
group: "Application"
groupOrder: 500
parentItem: "Localization"
parentItemOrder: 70
order: 10
toc: true
purpose: "Localization explanation"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Localization

Localization belongs to the Next.js presentation layer. ASP.NET Core REST routes and JSON field names stay locale-neutral, so language never changes the API contract or URL.

## Fixed deployment locale

Supported locales are `en` and `ru`. `PUBLIC_DEFAULT_LOCALE` selects one locale for the whole app instance; missing or unsupported values fall back to `en`. Build and runtime use the same value, and changing language requires rebuild/restart.

Routes have no locale prefix. Cookies, `Accept-Language`, user settings, and a language switcher do not select locale in the current Cache Components strategy. Time zone is fixed to `UTC` on server and client.

## UI and REST separation

Messages live in paired `src/messages/*.en.json` and `*.ru.json` catalogues. Metadata and safe API-failure copy come from these catalogues. The UI localizes by stable Problem Details `code`; it does not display or parse invariant-English API `title` or `detail`.

ASP.NET Core validation, authorization, `/api/v1/organizations/{organizationId}`, and generated DTO names are identical for both locales.

## Documentation routes

Documents use `.en.md`/`.ru.md` variants, while canonical routes remain neutral: `/docs/workspace/settings` is the same URL in both languages. Paired content is preferred; the registry can mark fallback content when one locale is missing.

## Related pages

- [Localized documentation content](/docs/general/authoring/localized-content)
- [Application shell](/docs/application)
- [Runtime security](/docs/application/runtime-security)

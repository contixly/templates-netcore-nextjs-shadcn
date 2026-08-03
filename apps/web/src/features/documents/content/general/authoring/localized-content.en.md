---
title: "Localized documentation content"
description: "Maintain paired English and Russian source files at one canonical URL and understand publication and fallback rules."
group: "Documentation"
groupOrder: 400
parentItem: "Authoring"
parentItemOrder: 100
order: 20
toc: true
purpose: "Documentation authoring reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Localized documentation content

Documentation localization is encoded in source filenames, not public URLs. The deployment locale
selects the matching compiled variant while links remain canonical.

## File pairs

Use an explicit supported suffix:

- `page.en.md` and `page.ru.md`;
- `page.en.mdx` and `page.ru.mdx` when custom components are required;
- `index.en.md` and `index.ru.md` for a directory index.

A production-visible canonical page must have both English and Russian variants. A draft or review
variant may be temporarily unpaired during local authoring, but it cannot be used to publish an
incomplete pair.

## Canonical URLs

The compiler removes the locale suffix, extension, and terminal `index` segment:

| Source file                    | Canonical URL               |
| ------------------------------ | --------------------------- |
| `general/quick-start.en.md`    | `/docs/general/quick-start` |
| `general/quick-start.ru.md`    | `/docs/general/quick-start` |
| `general/glossary/index.en.md` | `/docs/general/glossary`    |
| `general/glossary/index.ru.md` | `/docs/general/glossary`    |

Internal links never include `.en`, `.ru`, `.md`, or `.mdx`. Duplicate source files for the same
canonical URL and locale fail compilation.

## Pair semantics

Translate meaning rather than sentence structure. Keep these facts equivalent:

- supported behavior, limits, security rules, and deferred capabilities;
- heading hierarchy and task order;
- API methods, routes, field names, codes, and commands;
- status, version, `editedAt`, navigation order, and related canonical routes.

Use stable technical identifiers exactly as the implementation exposes them. Do not translate a
route, environment variable, command, Problem Details code, JSON field, or generated operation name.

## Fallback marker

The registry records available locales and the runtime can label fallback content when the selected
variant is absent. This supports local draft/review work and safe handling of an incomplete source,
but it does not relax the publication rule: a `published` or `archived` page must have both
production-visible variants.

## Validate the pair

Run from `apps/web`:

```bash
npm run content:generate
npm run content:check
npm run content:test
```

Then review the paired source diff and generated search entries. Confirm that internal links and
heading fragments resolve in both locales and that no locale suffix appears in a canonical link.

## Related pages

- [How to write documentation](/docs/general/authoring/how-to-write-docs)
- [Documentation components](/docs/general/authoring/sample)
- [Localization](/docs/application/localization)

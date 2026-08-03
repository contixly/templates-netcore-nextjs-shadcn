---
title: "How to write documentation"
description: "Author paired public pages with strict frontmatter, closed MDX components, validated links and images, and deterministic generated artifacts."
group: "Documentation"
groupOrder: 400
parentItem: "Authoring"
parentItemOrder: 100
order: 10
status: "published"
toc: true
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# How to write documentation

Source pages live under `apps/web/src/features/documents/content`. Write one clear user goal,
capability, explanation, or reference per canonical page, and create the English and Russian
variants together before publication.

## Use strict frontmatter

Every migrated production page requires this shape:

```md
---
title: "Page title"
description: "Short description for headers and search"
group: "General"
groupOrder: 400
parentItem: "Authoring"
parentItemOrder: 100
order: 10
status: "published"
toc: true
purpose: "Content authors"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-08-03"
---
```

`title`, `description`, `group`, and `parentItem` must be non-empty strings; `order` must be finite;
`toc` must be boolean; and `status` must be `draft`, `review`, `published`, or `archived`. The
production corpus also supplies non-empty `author` and `version` plus a real ISO date-only
`editedAt` value.

The optional allow-list is `groupOrder`, `parentItemOrder`, `purpose`, `hide`, `reading`, and
`source`. Unknown fields, invalid types or dates, duplicate canonical URL/locale variants, and an
unpaired production-visible page fail compilation. `published` and `archived` are production-visible
unless `hide: true`; `draft` and `review` are not.

## Name and structure the pair

Use `page.en.md` and `page.ru.md`, or `.mdx` when the page needs a custom component. Index variants
use `index.en.md` and `index.ru.md`. Keep equivalent facts, headings, status, navigation placement,
and related routes in both locales.

Use a stable page shape: purpose first, then concepts or prerequisites, task steps or reference
facts, limits and failure behavior, verification, and related pages. The right table of contents is
built from `##` and `###` headings.

## Use the closed MDX vocabulary

Plain Markdown is preferred. MDX accepts only these custom components:

- `Callout`;
- `Steps` and `Step`;
- `Files`, `Folder`, and `File`;
- `Tabs` and `Tab`;
- `DocumentLinkGrid`, `DocumentLinkGroup`, and `DocumentLinkCard`.

Executable MDX `import` and `export` syntax and unknown components fail compilation. Use
[Documentation components](/docs/general/authoring/sample) as the live fixture for supported
rendering.

## Add links and images

Internal document links use canonical `/docs/...` URLs without file extensions or locale suffixes.
The compiler verifies that the target page exists and that any heading fragment matches a generated
heading ID. Keep English and Russian related-page routes identical.

Use normal `https://` links for external material. Repository-local documentation images use an
absolute `/img/...` path to a real file under `apps/web/public/img`; missing images fail compilation.
Write meaningful alt text and do not embed secrets, credentials, or user data in examples or image
artifacts.

## Generate and check artifacts

From `apps/web`, run:

```bash
npm run content:generate
npm run content:check
npm run content:test
```

`npm run content:generate` rewrites the committed TypeScript registry and JSON search index.
`npm run content:check` recompiles and byte-compares both outputs. Never edit
`src/features/documents/generated/documents-registry.gen.ts` or
`contracts/documents/search-index.json` by hand.

Before completion, also run the focused MDX/rendering tests, typecheck, full Jest suite, and
production build required by the change.

## Related pages

- [Localized documentation content](/docs/general/authoring/localized-content)
- [Documentation components](/docs/general/authoring/sample)
- [Requirements, E2E, and docs](/docs/developers/openspec-e2e-docs)
- [Releases and changelog](/docs/developers/releases-changelog)

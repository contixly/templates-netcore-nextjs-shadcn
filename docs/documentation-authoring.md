# Documentation authoring

This reference is for maintainers who add or change public documentation in
iteration 8. It defines the source contract enforced by the compiler and the
additional publication policy for the current bilingual corpus.

## Source and canonical route

Author only under:

```text
apps/web/src/features/documents/content/**/*.{en,ru}.{md,mdx}
```

Use `.md` unless a page needs a closed custom component; use `.mdx` for that
case. Each canonical page has an explicit pair such as `quick-start.en.md` and
`quick-start.ru.md`. A directory index uses `index.en.md` and `index.ru.md`.
Every source must have one of those explicit suffixes; a bare `.md` or `.mdx`
source is rejected rather than treated as English. The compiler then removes
the locale suffix, extension, and terminal `/index`:

| Source | Public route |
| --- | --- |
| `index.en.mdx` | `/docs` |
| `general/quick-start.ru.md` | `/docs/general/quick-start` |
| `general/glossary/index.en.md` | `/docs/general/glossary` |

Never put `.en`, `.ru`, `.md`, or `.mdx` in a public link. A duplicate
canonical-route/locale source is an error.

## Frontmatter reference

A production page uses this complete shape:

```yaml
---
title: "Page title"
description: "Short header, metadata, and search description"
group: "Documentation"
groupOrder: 400
parentItem: "Authoring"
parentItemOrder: 100
order: 10
status: "published"
hide: false
toc: true
purpose: "Documentation maintainers"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-08-03"
reading: "8 minutes"
source: "Target-owned documentation"
---
```

The compiler accepts only these fields:

| Field | Compiler rule | Publication policy |
| --- | --- | --- |
| `title`, `description`, `group`, `parentItem` | required non-empty strings | keep meaning aligned across the pair |
| `order` | required finite number | use the same placement intent in both locales |
| `status` | required `draft`, `review`, `published`, or `archived` | publish only an aligned pair |
| `toc` | required boolean | `true` when the page should expose its `h2` table of contents |
| `groupOrder`, `parentItemOrder` | optional finite numbers | keep pair navigation placement aligned |
| `hide` | optional boolean | omitted/`false` unless intentionally hidden |
| `purpose`, `reading`, `source` | optional non-empty strings | do not put secrets or private provenance here |
| `author`, `version` | compiler-optional non-empty strings | required by the migrated production-corpus policy |
| `editedAt` | compiler-optional real `YYYY-MM-DD` calendar date | required by the migrated production-corpus policy; never derive it from mtime |

Unknown fields, invalid values, an invalid calendar date, and duplicate variants
stop compilation. Do not use the former `readingMinutes` field.

## Locale, status, visibility, and order

The supported locales are exactly `en` and `ru`. `published` and `archived` are
production-visible when `hide` is not `true`. If any variant of a canonical page
is production-visible, both `en` and `ru` must have a production-visible variant.
A `draft` or `review` variant may remain temporarily unpaired for local authoring
only; runtime fallback markers do not relax the publication rule.

Translate behavior, not syntax. Keep HTTP methods and routes, JSON fields,
Problem Details codes, commands, environment-variable names, generated
operation names, limits, security rules, version, edit date, heading hierarchy,
and related canonical routes consistent across the pair. Current pages describe
ASP.NET Core + REST. Historical pages may describe the former full-stack Next.js
runtime only when they label it as legacy and direct readers to current guidance.

Navigation sorts group order descending then group label, parent order descending
then parent label, document order descending then title, with canonical route and
locale as deterministic final ties. Previous/next links and empty search use that
same generated order.

## Headings and content

Write one user goal or reference subject per page. The rendered article owns its
visible `h1`; a source `#` heading is suppressed, so keep it useful for source
readability but do not rely on it for navigation. `##` and `###` headings become
searchable headings and stable anchors. Duplicate anchor text receives `-2`,
`-3`, and so on. Backtick and tilde fenced code does not create headings, links,
images, or MDX validations.

GFM tables, task lists, footnotes, blockquotes, inline code, and fenced code are
supported. Give images meaningful alt text and keep examples free of secrets,
credentials, access tokens, real user data, or private URLs.

## Closed MDX components

Executable MDX `import`/`export`, member expressions, namespaced expressions,
and every unknown capitalized component are rejected. The complete custom set is:

- `Callout` — optional `title`; `variant` is `default`, `info`, `success`,
  `warning`, or `danger`;
- `Steps` containing `Step`; each `Step` requires `title`;
- `Files` containing nested `Folder` and `File`; both leaf types require `name`;
- `Tabs` containing `Tab`; `Tabs` may set `defaultValue`, while each `Tab`
  requires `title` and `value`;
- `DocumentLinkGrid` containing `DocumentLinkGroup` and `DocumentLinkCard`;
  groups use `title` and may use `description`, while cards require canonical
  `href` and `title`.

See the paired live fixture at
`apps/web/src/features/documents/content/general/authoring/sample.{en,ru}.mdx`
before changing component markup. Extending the set requires compiler,
component, rendering, and both-locale fixture tests in the same change.

## Links

Internal documentation links use canonical absolute `/docs` or `/docs/...`
paths. Query strings and trailing slashes are normalized for validation;
`/docs/index` is the root. Hash-only links target the current page. A fragment
must match a generated `h2`/`h3` anchor in the target locale, with fallback to the
first source variant only for a non-production source when that locale variant
is absent. Markdown inline links, reference definitions, and supported MDX
`href` literals are checked.

The compiler gives a broken-link diagnostic when no canonical target exists. A
production-visible source also requires a production-visible target in the same
locale; a matching-locale draft, review, hidden, or absent variant gives a
distinct unpublished-link diagnostic. Fragment validation follows that target
resolution.
Normal `http://` and `https://` links, `mailto:` links, hash-only links, and
non-document paths are outside canonical-document target validation. At render
time unsafe protocols are suppressed and unavailable `/docs` links are rendered
as disabled text.

## Images

A repository-local image uses an absolute `/img/...` source and must resolve to
a real file beneath `apps/web/public`; escaping that directory or referencing a
missing/non-file path fails compilation. Query and fragment text does not affect
the file lookup. Remote rendered images may use `http://` or `https://`. The
article renderer emits responsive, lazy native images because MDX supplies
author-controlled dimensions. Always provide useful `alt` text.

Do not modify `template/` to add an asset. Copy only the necessary reference
asset into the target-owned `apps/web/public` tree and verify the branch-range
immutable-reference guard.

## Generate and verify

From `apps/web`, run after every source edit:

```bash
npm run content:generate
npm run content:check
npm run content:test
```

`content:generate` deterministically rewrites:

- `apps/web/src/features/documents/generated/documents-registry.gen.ts`, the
  typed metadata registry and exact static module import map used by Next.js;
- `contracts/documents/search-index.json`, the runtime-neutral, public
  page/heading search projection embedded by .NET Infrastructure.

Never edit either generated file by hand. `content:check` recompiles and
byte-compares both complete outputs; it must pass after a second generation.
The outputs contain no timestamp or filesystem-mtime fallback.

Before completing a documentation change, also run the focused documents Jest
suites, `npm run boundaries:check`, formatting, lint, typecheck, full Jest, and a
clean standalone production build. Run API/Application/OpenAPI/SDK and
Playwright gates when search, generated shapes, routes, metadata, OG, or browser
behavior can change. Finally run:

```bash
git diff --check
git diff --exit-code origin/main...HEAD -- template/
test ! -d openspec/changes || \
  test -z "$(find openspec/changes -mindepth 1 -maxdepth 1 ! -name archive -print -quit)"
```

OpenSpec remains initialized-only unless the user explicitly requests an active
change.

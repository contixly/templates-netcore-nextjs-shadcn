---
title: "Releases and changelog"
description: "Publish factual paired release notes and weekly summaries from the documentation content tree and regenerate its committed artifacts."
group: "For developers"
groupOrder: 300
parentItem: "Publishing"
parentItemOrder: 60
order: 10
toc: true
purpose: "Developer publishing reference"
status: "published"
author: "Template Maintainers"
version: "1.2.0"
editedAt: "2026-07-06"
---

# Releases and changelog

The public history has two surfaces: versioned release notes and dated weekly summaries. Both are
source pages in the documentation content tree; there is no separate legacy release-note source to
copy from.

## Release notes

Add a version pair under
`apps/web/src/features/documents/content/history/releases/{version}.{locale}.md` or `.mdx`. Release
notes describe verified behavior available in that version: user workflows, API contracts,
configuration or operational changes, security-relevant defaults, and required upgrade actions.

Do not advertise a deferred capability. If historical behavior belongs to the former full-stack
Next.js reference, label it as legacy instead of presenting it as the current ASP.NET Core target.
Keep the release index aligned with the actual published files.

## Weekly changes

Add paired dated pages under
`apps/web/src/features/documents/content/history/change-logs`. Derive the summary from the exact
commit range and repository diff. Group user-visible results by capability; omit speculative work and
internal detail that does not affect adopters.

A weekly summary is not a substitute for durable architecture, API, security, or operations
documentation. Update the appropriate file under `docs/` in the same change when such a decision is
made.

## Localization and metadata

Every production-visible history URL needs an English and Russian variant with equivalent facts,
status, version, dates, and canonical links. Keep locale suffixes in source filenames only. Use the
same strict frontmatter policy as every other public page.

## Generate and verify

From `apps/web`:

```bash
npm run content:generate
npm run content:check
npm run content:test
```

Review both committed outputs:

- `apps/web/src/features/documents/generated/documents-registry.gen.ts`;
- `contracts/documents/search-index.json`.

Never hand-edit them. Also run the focused rendering/link tests and the normal web type, test, and
build gates before publishing.

## Related pages

- [Releases](/docs/history/releases)
- [Weekly changes](/docs/history/change-logs)
- [Requirements, E2E, and docs](/docs/developers/openspec-e2e-docs)
- [How to write documentation](/docs/general/authoring/how-to-write-docs)

# Task 6 Report — Documentation UI Ownership / Reference Parity

## Status

**DONE_WITH_CONCERNS**

Implementation base: `ec4e121bbdeb558265ed95ae86ac91c61f93885c` (`refactor(web): align API key management UI`)

Implementation commit: this report's containing commit (`refactor(web): align documentation UI`); the exact SHA is recorded in the task handoff.

The documentation presentation is feature-owned and aligned to the immutable reference while preserving the existing registry, routes, locale resolution, generated search request, metadata, and OG behavior. The requested contract, focused and full Jest suites, documentation Playwright journeys, TypeScript, boundaries, content validation, production build, scoped strict lint, formatting, visual QA, and repository-integrity checks pass. The only codebase concern is the existing 17-warning generated-SDK lint debt; ordinary lint exits 0 and task-scoped strict lint is clean.

## Requirements and documentation read

Read before implementation:

- `.superpowers/sdd/2026-08-10-ui-reference-parity/task-6-brief.md`
- `docs/aspnetcore-migration-plan.md`
- `docs/superpowers/plans/2026-08-10-ui-reference-parity.md`
- `docs/superpowers/specs/2026-08-10-ui-reference-parity-design.md`
- immutable documentation reference components and routes under `template/`
- installed Next.js 16.2.11 documentation for layouts/pages, dynamic routes, route groups, and MDX.

The resulting route files remain Server Components, while the interactive documents shell and its search/sidebar behavior remain client-owned. No API, authentication, generated SDK, contract, content-registry, or OG-output boundary changed.

## TDD evidence

### RED

Created `apps/web/test/features/documents/reference-documents-shell.test.tsx`, then ran:

```bash
npm test -- --runInBand test/features/documents/reference-documents-shell.test.tsx
```

The test failed against the pre-port shell because `[data-slot='sidebar-wrapper']` did not exist, so the expected reference `--sidebar-width: 24rem` geometry could not be observed. This established the presentation gap before implementation.

### GREEN

After moving the documentation UI and applying the reference composition, the focused contract passed: 1 suite and 1 test.

Final brief-focused run:

```bash
npm test -- --runInBand test/features/documents/reference-documents-shell.test.tsx test/app/documents-pages.test.tsx test/components/documents/documents-mdx-components.test.tsx test/components/documents/documents-page.test.tsx test/components/documents/documents-search.test.tsx test/components/documents/documents-shell.test.tsx
```

```text
Test Suites: 6 passed, 6 total
Tests:       34 passed, 34 total
Snapshots:   0 total
```

## Implementation summary

### Feature ownership and route composition

- Moved all 14 requested documentation presentation modules with `git mv` from `apps/web/src/components/documents/` to `apps/web/src/features/documents/ui/`, including the MDX subdirectory and scroll-spy utility.
- Updated documentation routes, tests, and E2E references to use feature-owned paths and removed the legacy directory/import surface.
- Preserved the exact `DocumentsShell` inputs: `children`, `navigation`, and `pageNavigationByHref`.
- Kept the document registry, route helpers, navigation/headings data, generated search SDK, metadata generation, locale resolution, and presentation-only OG handler unchanged.

### Reference shell, navigation, and search

- Ported the shared shadcn responsive `SidebarProvider`/`Sidebar` composition with a `24rem` desktop width, collapsible navigation groups, status stripes, active descendants, desktop rail, and mobile sheet behavior.
- Ported the fixed-height header hierarchy, responsive breadcrumb, localized sidebar trigger, home/theme actions, reference search field, command-palette dialog, and visible keyboard shortcut treatment.
- Preserved debounced generated-SDK search, abort/generation protection, keyboard navigation, accessibility roles, result grouping, empty states, and client routing.
- Made the shell's semantic `main` the bounded viewport scroll container and retained the skip-link target.

### Article, metadata, TOC, navigation, and MDX

- Ported the reference max-width article rail, responsive metadata grid, top page actions, fixed mobile page navigation, bottom previous/next cards, and sticky desktop TOC.
- Retained scroll-spy exports and behavior while using the shell scroll container and a stable heading lookup map.
- Ported reference spacing and surface treatment for prose, headings, task/footnote lists, callouts, steps, file trees, tabs, images, code, tables, and link grids.
- Preserved the closed MDX component registry and external-link safety (`rel="noopener noreferrer"`).

## Verification evidence

### Documentation Playwright journeys

```bash
npm run e2e -- documents.spec.ts
```

```text
Running 3 tests using 1 worker
3 passed (22.8s)
```

The first attempt was blocked before browser startup by a PostgreSQL Testcontainer `pg_wal: No space left on device` error. Host disk space was healthy, while Docker reported 25.63 GB of builder cache; `docker builder prune -f` reclaimed 22.55 GB. The first browser-capable run then exposed an accessibility-locator ambiguity because both the sidebar nav and breadcrumb matched `Documentation`; scoping the existing sidebar assertion with `exact: true` resolved that test-only locator issue. The final run passed all anonymous route, search, article, and social-image coverage.

### Full Jest suite

```bash
npm test -- --runInBand
```

```text
Test Suites: 111 passed, 111 total
Tests:       862 passed, 862 total
Snapshots:   0 total
```

The first full-suite audit caught the moved documents-shell path in the wrong lexical position in the protected-main-landmark allowlist. Reordering the expectation to match the test's `.sort()` behavior restored the full-suite pass without weakening the landmark contract.

### TypeScript, boundaries, content, and production build

```bash
npm run typecheck
npm run boundaries:check
npm run content:check
APP_PUBLIC_ORIGIN=http://localhost:3000 npm run build
```

Result: PASS. `next typegen`/`tsc --noEmit` completed; all 8 boundary tests and the source-boundary scan passed; documentation content validation completed; Next.js 16.2.11 compiled, typechecked, and generated 144 static/partially prerendered pages.

### Lint, formatting, and repository integrity

```bash
npx eslint src/features/documents/ui 'src/app/(documents)/docs/layout.tsx' 'src/app/(documents)/docs/page.tsx' 'src/app/(documents)/docs/[...slug]/page.tsx' e2e/documents.spec.ts test/app/documents-pages.test.tsx test/app/protected-main-landmark.test.tsx test/components/documents test/features/documents/reference-documents-shell.test.tsx --max-warnings=0
npx prettier --check ...
git diff --check
git diff --exit-code -- template/
```

Result: PASS / empty. A repository scan also finds no remaining `components/documents` import under `apps/web/src`, `apps/web/test`, or `apps/web/e2e`.

```bash
npm run lint
```

Result: exit 0 with 0 errors and the repository's same 17 pre-existing unused type-proof warnings in unchanged `test/contracts/generated-sdk.test.ts`.

### Visual QA

- Ran the documentation page at `/docs/api/api-v1` in the Codex in-app browser.
- Captured and inspected desktop (`1440x1000`) and mobile (`390x844`) viewport screenshots with `view_image`.
- Verified responsive sidebar/sheet geometry, header/breadcrumb/search hierarchy, metadata grid, article/code/table containment, desktop TOC, mobile page actions, and bottom navigation against the immutable reference source.
- Reset the temporary viewport and finalized the browser tab after inspection; no QA artifact was added to the repository.

## Self-review and constraints

- Confirmed every requested documentation presentation module exists at the feature-owned path and the legacy directory is absent.
- Confirmed the new contract renders the real shell and asserts both the reference sidebar width and semantic scroll-container main.
- Confirmed desktop and mobile navigation remain keyboard/accessibility reachable and the mobile sidebar closes after navigation.
- Confirmed search still uses the generated SDK; no raw fetch, handwritten transport DTO, Server Action, Prisma, Better Auth, bearer-token/browser-storage, cookie, CSRF, backend, database, schema, or contract change was introduced.
- Confirmed document content, registry values, metadata, locale selection, public routes, social-image output, and API/auth behavior are unchanged.
- Confirmed `template/` is untouched.

## Concerns

1. Non-blocking/pre-existing: repository-wide strict zero-warning lint would still exit non-zero because unchanged `test/contracts/generated-sdk.test.ts` has 17 `@typescript-eslint/no-unused-vars` warnings. Task-scoped strict lint passes.
2. Non-blocking/tooling: Playwright emits repeated `NO_COLOR`/`FORCE_COLOR` warnings; all three requested journeys pass.
3. Resolved/tooling: the initial E2E Testcontainer failure was Docker builder-cache exhaustion rather than a repository defect; pruning the cache restored the environment and subsequent E2E runs completed.

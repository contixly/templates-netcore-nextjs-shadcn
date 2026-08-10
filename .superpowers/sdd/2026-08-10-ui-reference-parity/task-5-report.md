# Task 5 Report — API Key Management UI Ownership / Reference Parity

## Status

**DONE_WITH_CONCERNS**

Implementation base: `74c8c81` (`fix(web): align workspace collaboration reference surfaces`)

Implementation commit: this report's containing commit (`refactor(web): align API key management UI`); the exact SHA is recorded in the task handoff.

The personal and organization API-key presentation is feature-owned and aligned to the immutable reference. The new reference contract, all focused API-key Jest tests, the full Jest suite, all six API-key Playwright journeys, TypeScript, boundaries, production build, scoped strict lint, formatting, and repository-integrity checks pass. The only concern is the repository's existing 17-warning generated-SDK lint debt; ordinary lint exits 0 and task-scoped strict lint is clean.

## Requirements and documentation read

Read before implementation:

- `.superpowers/sdd/2026-08-10-ui-reference-parity/task-5-brief.md`
- `docs/superpowers/plans/2026-08-10-ui-reference-parity.md`
- `docs/superpowers/specs/2026-08-10-ui-reference-parity-design.md`
- immutable API-key reference components and routes under `template/`
- installed Next.js 16.2.11 documentation:
  - `node_modules/next/dist/docs/01-app/01-getting-started/03-layouts-and-pages.md`
  - `node_modules/next/dist/docs/01-app/01-getting-started/05-server-and-client-components.md`
- current shadcn project metadata and official docs/examples for Alert Dialog, Badge, Button, Card, Checkbox, Dialog, Dropdown Menu, Empty, Field, Input, Select, Switch, and Table.

The move retains every existing `"use client"` and Server Component boundary. Route loaders remain server-owned, while browser reads and mutations continue through the generated REST client and existing mutation adapters.

## TDD evidence

### RED

Created `apps/web/test/features/api-keys/reference-api-key-table.test.tsx` against the required future feature path, then ran:

```bash
npm test -- --runInBand test/features/api-keys/reference-api-key-table.test.tsx
```

The first run failed because `@/src/features/api-keys/ui/api-key-management` did not exist. After the `git mv`, the contract reached the pre-port UI and failed at assertion level:

```text
Expected the API keys heading to have class: text-sm
Received: text-lg

Expected Basic account read to be rendered in a Badge
Received: an unstructured list item
```

This demonstrated both feature-ownership and reference-presentation gaps before implementation.

### GREEN

After the feature move and reference port:

```text
PASS test/features/api-keys/reference-api-key-table.test.tsx
Test Suites: 1 passed, 1 total
Tests:       2 passed, 2 total
```

Final brief-focused run:

```bash
npm test -- --runInBand test/features/api-keys/reference-api-key-table.test.tsx test/components/api-keys/api-key-create-dialog.test.tsx test/components/api-keys/api-key-edit-dialog.test.tsx test/components/api-keys/api-key-management.test.tsx test/components/api-keys/api-key-rotate-dialog.test.tsx test/components/api-keys/api-key-secret-view.test.tsx
```

```text
Test Suites: 6 passed, 6 total
Tests:       41 passed, 41 total
Snapshots:   0 total
```

The related route-page suite also passes: 1 suite, 9 tests.

## Implementation summary

### Feature ownership and route composition

- Moved all eight requested API-key presentation modules with `git mv` from `apps/web/src/components/api-keys/` to `apps/web/src/features/api-keys/ui/`.
- Added a feature-owned permission-preview component and updated all route, component-test, mock, and E2E imports.
- Removed the legacy presentation directory. A repository scan finds no `src/components/api-keys` imports.
- Removed redundant route-level settings cards; both personal and organization routes now render the feature-owned education and management settings sections directly beneath the page intro.

### Reference table, empty state, education, and actions

- Ported the reference `SettingsSection` hierarchy, compact section heading, header create action, descriptive copy, bordered Empty state, and four-item education grid.
- Ported the reference table structure: combined name/status cell, safe key prefix, permission Badges, rate/expiry/usage dates, horizontally contained dense layout, and compact overflow action menu.
- Preserved the exact existing `ApiKeyTable` runtime props: `apiKeys`, `busyKeyIds`, `mutationArbiter`, `onConfirmed`, `onRevoked`, `onToggle`, `owner`, and `secretViewRef`.
- Kept Edit, Enable/Disable, Rotate, and Revoke capability in the action menu, with mutation-busy and hydration-readiness disabling unchanged.
- Ensured dialog-triggered menus close when their dialog closes, including mismatch/failure cancellation paths.

### Dialog and secret handling

- Ported create/edit forms to the reference wider scrollable dialog, FieldSet/FieldLegend composition, exact derived permission preview, compact three-column expiry/rate settings, and switch hierarchy.
- Ported rotate to the compact confirmation dialog and revoke to the semantic destructive Alert Dialog.
- Ported reveal-once secret styling and action hierarchy while retaining the acknowledgement boundary: the credential is cleared on acknowledgement/dismissal and never copied into storage.
- Preserved exact preset-to-scope derivation, validation, generated request bodies, REST methods/routes, CSRF adapters, identity-mismatch handling, optimistic reconciliation, mutation leases, stale-response rejection, rotate secret clearing, and revoke semantics.
- Added paired EN/RU reference copy and labels without changing existing scope IDs, preset IDs, statuses, failure IDs, or API data.

## Verification evidence

### Required API-key Playwright journeys

```bash
npm run e2e -- api-keys.spec.ts
```

```text
Running 6 tests using 1 worker
6 passed (28.6s)
```

This covers auth precedence, the complete personal create/reveal/use/edit/disable/enable/rotate/revoke lifecycle, owner/admin/member organization permissions and isolation, personal and organization scope enforcement, redaction/non-disclosure, credential invalidation, and opaque terminal pagination.

### Full Jest suite

```bash
npm test -- --runInBand
```

```text
Test Suites: 110 passed, 110 total
Tests:       861 passed, 861 total
Snapshots:   0 total
```

### TypeScript, boundaries, and production build

```bash
npm run typecheck
npm run boundaries:check
npm run build
```

Result: PASS. `next typegen`/`tsc --noEmit` completed; all 8 boundary tests and the source-boundary scan passed; Next.js 16.2.11 compiled, typechecked, and generated 144 static/partially prerendered pages.

### Lint, formatting, and repository integrity

```bash
npx eslint src/features/api-keys/ui 'src/app/(protected)/user/api-keys/page.tsx' 'src/app/(protected)/w/[organizationKey]/settings/api-keys/page.tsx' e2e/support/api-key-e2e-harness.ts test/app/api-key-pages.test.tsx test/components/api-keys test/features/api-keys/reference-api-key-table.test.tsx --max-warnings=0
```

Result: PASS; 0 errors and 0 warnings.

```bash
npm run lint
```

Result: exit 0 with 0 errors and the repository's same 17 pre-existing unused type-proof warnings in unchanged `test/contracts/generated-sdk.test.ts`.

```bash
npx prettier --check ...
git diff --check
git diff --exit-code -- template/
```

Result: PASS / empty.

## Self-review

- Confirmed the new contract renders the real management UI and asserts reference heading, Empty, permission Badge, and compact dropdown composition rather than source strings.
- Confirmed all eight requested presentation files exist at feature-owned paths and the legacy directory is absent.
- Confirmed no old API-key presentation import remains under `src`, `test`, or `e2e`.
- Confirmed table and dialogs remain responsive through horizontal table containment, minimum column widths, multi-column breakpoints, and bounded dialog scrolling.
- Confirmed no raw fetch, handwritten transport DTO, Prisma, Better Auth, Server Action, bearer-token/browser storage, generated SDK, API/backend, cookie, CSRF, database, or schema change was introduced.
- Confirmed `template/` is untouched.

## Concerns

1. Non-blocking/pre-existing: repository-wide strict zero-warning lint would still exit non-zero because unchanged `test/contracts/generated-sdk.test.ts` has 17 `@typescript-eslint/no-unused-vars` warnings. Task-scoped strict lint passes.
2. Non-blocking/tooling: Playwright emits repeated `NO_COLOR`/`FORCE_COLOR` warnings; all six requested journeys pass.

# Task 4 Report — Organization and Collaboration UI Ownership / Reference Parity

## Status

**DONE_WITH_CONCERNS**

Implementation base: `5ab3f18` (`fix(web): align account reference surfaces`)

Implementation commit: this report's containing commit (`refactor(web): align workspace collaboration UI`); the exact SHA is recorded in the task handoff.

The organization/collaboration presentation is feature-owned, the workspace reference contract is green, focused and full Jest suites pass, all seven requested organization/collaboration Playwright journeys pass, TypeScript/boundary/build checks pass, and the immutable `template/` tree remains unchanged. The only concern is the repository's existing 17-warning generated-SDK lint debt; ordinary lint exits 0 and task-scoped strict lint is clean.

## Requirements and documentation read

Read before implementation:

- `.superpowers/sdd/2026-08-10-ui-reference-parity/task-4-brief.md`
- `docs/superpowers/plans/2026-08-10-ui-reference-parity.md`
- `docs/superpowers/specs/2026-08-10-ui-reference-parity-design.md`
- immutable organization/collaboration reference components under `template/`
- installed Next.js 16.2.11 documentation:
  - `node_modules/next/dist/docs/01-app/01-getting-started/03-layouts-and-pages.md`
  - `node_modules/next/dist/docs/01-app/01-getting-started/05-server-and-client-components.md`
- current shadcn project metadata and official component docs/examples for Card, Dialog, Empty, Field, Item, Sidebar, Table, Dropdown Menu, Avatar and Badge.

The moves retain every existing `"use client"` and Server Component boundary. Route loaders remain server-owned; browser reads, mutations, request arbitration, reconciliation and refresh/navigation behavior remain in the same Client Components.

## TDD evidence

### RED

Created `apps/web/test/features/organizations/reference-workspace-surfaces.test.tsx` against the required future feature path, then ran:

```bash
npm test -- --runInBand test/features/organizations/reference-workspace-surfaces.test.tsx
```

The first run failed because `@/src/features/organizations/ui/organization-settings-nav` did not yet exist. After the `git mv`, the contract reached the pre-port UI and failed at assertion level:

```text
Expected the navigation to have class: md:w-64
Received: ... md:w-56
Test Suites: 1 failed, 1 total
Tests:       1 failed, 1 total
```

This demonstrated that the old settings rail did not match the reference 16-rem desktop geometry.

### GREEN

After the feature move and reference port:

```text
PASS test/features/organizations/reference-workspace-surfaces.test.tsx
Test Suites: 1 passed, 1 total
Tests:       1 passed, 1 total
```

Final brief-focused run:

```bash
npm test -- --runInBand test/features/organizations/reference-workspace-surfaces.test.tsx test/components/organization-add-member-dialog.test.tsx test/components/organization-delete-dialog.test.tsx test/components/organization-list.test.tsx test/components/organization-member-directory.test.tsx test/components/organization-onboarding.test.tsx test/components/organization-settings-form.test.tsx test/components/organization-switcher.test.tsx test/components/invitation-activity.test.tsx test/components/invitation-create-dialog.test.tsx test/components/invitation-decision.test.tsx test/components/team-controls.test.tsx test/components/team-directory.test.tsx
```

```text
Test Suites: 13 passed, 13 total
Tests:       202 passed, 202 total
Snapshots:   0 total
```

## Implementation summary

### Feature ownership and imports

- Moved all twelve organization presentation modules with `git mv` into `apps/web/src/features/organizations/ui/`.
- Moved all seven collaboration presentation modules with `git mv` into `apps/web/src/features/collaboration/ui/`.
- Updated protected routes, shared application presentation consumers, tests, mocks and E2E readiness imports to feature-owned paths.
- Removed both legacy component directories. A source/test/E2E scan finds no `components/organizations` or `components/collaboration` import.
- Did not relocate shared application shell primitives or change the generated API client.

### Workspace navigation, selection and onboarding

- Ported workspace settings navigation to the reference icon-led `SidebarMenu` rail at `md:w-64`, with a full-width right-side mobile Drawer and capability-gated Invitations/API keys entries.
- Preserved stable URLs, exact/nested active-route semantics, translated labels and `aria-current` behavior.
- Ported the workspace switcher trigger to the reference initials/name/subtitle/selector hierarchy while preserving the existing mutation arbiter, route-suffix handling and refresh/navigation ordering.
- Ported the workspace index to the reference split layout: responsive card grid, sticky creation rail, shadowless cards and a composed Empty state. The page rail now uses the reference `max-w-[1360px]` width.
- Ported zero-workspace onboarding to the centered, readable reference card with wrap-safe actions without changing create or refresh behavior.

### Workspace settings and membership

- Replaced redundant nested workspace-settings cards with the shared settings-section composition while retaining identity and danger boundaries.
- Ported current-user and other-member presentation to independent reference settings cards, Avatar fallbacks, role badges and horizontally contained member rows.
- Preserved capability-gated add/remove/role controls, member ordering, mutation synchronization, error projection and the existing REST/CSRF adapters.

### Invitations and teams

- Ported invitation activity to a reference settings card with header create action, status filter, structured failure alert, Empty state and a mobile-contained table for email, role, team, inviter, expiry, status and copy actions.
- Ported account invitations to responsive bordered invitation rows and decision detail to the reference status-card hierarchy.
- Ported team membership to contained reference tables with Avatar fallback, role badges, Empty state and compact actions; team cards now expose member-count badges and wrap-safe controls.
- Retained canonical cursor/filter behavior, confirmed-mutation overlays, stale-read arbitration, request/response waits, safe failure projections and permission/read-only behavior.
- Added paired EN/RU table-column labels for teams and invitation activity. No existing action or status IDs changed.

## Verification evidence

### Required organization/collaboration Playwright journeys

```bash
npm run e2e -- organizations.spec.ts collaboration.spec.ts
```

```text
Running 7 tests using 2 workers
7 passed (57.4s)
```

This covers zero-workspace creation and fallback routing, owner/member permission composition, stale concurrent settings patches, canonical load-more refresh, slug collision and suffix-preserving switching, last-workspace protection, team membership management and safe invitation decisions.

The first visual-port run caught missing status translations in the new decision badge. The badge was corrected to use the established invitation-status namespace; the final seven-test run above is clean of missing-message diagnostics.

### Full Jest suite

```bash
npm test -- --runInBand
```

```text
Test Suites: 109 passed, 109 total
Tests:       853 passed, 853 total
Snapshots:   0 total
```

### TypeScript, boundaries and production build

```bash
npm run typecheck
```

Result: PASS; `next typegen` and `tsc --noEmit` completed.

```bash
npm run boundaries:check
```

Result: PASS; 8 Node boundary tests passed and the source-boundary scan is clean.

```bash
npm run build
```

Result: PASS; Next.js 16.2.11 compiled, typechecked and generated 144 static/partially prerendered pages.

### Lint, formatting and repository integrity

```bash
npx eslint src/features/organizations/ui src/features/collaboration/ui 'src/app/(protected)' e2e/support/app-readiness.ts test/features/organizations/reference-workspace-surfaces.test.tsx --max-warnings=0
```

Result: PASS; 0 errors, 0 warnings.

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

- Confirmed the reference contract renders the real settings navigation and asserts geometry plus the active Workspace link, rather than checking source strings.
- Confirmed all nineteen requested presentation files exist at feature-owned paths and both legacy directories are absent.
- Confirmed no old organization/collaboration presentation imports remain under `src`, `test` or `e2e`.
- Confirmed reference layouts remain responsive: workspace cards use an auto-fill grid; settings navigation uses a mobile Drawer; dense member, team and invitation tables own horizontal containment/minimum widths; action groups wrap.
- Confirmed capability conditions and current 401/403/404 failure behavior are unchanged.
- Confirmed no raw fetch, handwritten transport DTO, Prisma, Better Auth, Server Action, bearer-token storage, generated SDK, API/backend, cookie, CSRF or schema change was introduced.
- Confirmed `template/` is untouched.

## Concerns

1. Non-blocking/pre-existing: repository-wide strict zero-warning lint would still exit non-zero because unchanged `test/contracts/generated-sdk.test.ts` has 17 `@typescript-eslint/no-unused-vars` warnings. Task-scoped strict lint passes.
2. Non-blocking/tooling: Playwright emits repeated `NO_COLOR`/`FORCE_COLOR` warnings and expected Next development cache-bypass notices; all seven requested journeys pass.

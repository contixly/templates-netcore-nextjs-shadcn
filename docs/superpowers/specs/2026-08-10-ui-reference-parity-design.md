# UI reference-parity remediation design

**Date:** 2026-08-10

**Scope:** Reopened migration iteration 9 — UI-only visual-parity remediation for every already migrated route in `apps/web`.

## Goal

Restore the appearance and composition of the immutable `template/` reference for all migrated UI surfaces while preserving the target architecture: a Next.js UI that communicates only with the ASP.NET Core API over existing REST/OpenAPI clients.

Parity is screenshot-level for desktop and mobile, light and dark themes. Differences are permitted only for API/data-driven values and for target-owned wording that must accurately describe the ASP.NET Core architecture instead of the reference's former Prisma/Better Auth stack.

## Constraints and decisions

- `template/` remains read-only. It is inspected as source material only and is never edited, formatted, moved, or used for migrations.
- No API endpoint, OpenAPI operation, EF Core migration, schema change, transaction, authentication behaviour, authorization policy, CSRF mechanism, cursor pagination/filtering behaviour, or generated SDK changes belong to this slice.
- Browser authentication continues to use the current `HttpOnly` same-origin session cookie. No bearer token storage is introduced.
- Existing server loaders and browser mutation adapters remain the only UI data boundary. UI refactoring must not introduce Prisma, Better Auth, Server Actions, or direct database access.
- Iteration 9 is reopened as a UI-parity remediation. Iterations 10–12 (Aspire, production topology, final parity/hardening) remain out of scope.

## Chosen approach

The implementation uses a feature-owned, reference-led presentation port:

1. Reproduce the reference design tokens, global base rules, typography, motion and shadcn primitive implementations in the target.
2. Move target presentation components from broad per-domain `src/components/` folders to the already established `src/features/<feature>/ui/**` feature boundary.
3. Recompose route layouts and screens from the feature-owned UI and unchanged REST data adapters.
4. Assert visual/structural contracts before each port, then validate all direct migrated routes in browser checks.

A CSS-only patch was rejected because it would preserve the current ownership and composition divergence. A wholesale page rewrite was rejected because it would duplicate data logic and put functional REST paths at unnecessary risk.

## Target component boundaries

```text
apps/web/src/
├── app/                         # Thin Next route/layout composition only
├── components/
│   └── ui/                      # Shared shadcn primitives only
└── features/
    ├── application/ui/          # Providers, app shell, navigation, theme, landing, settings shell
    ├── account/ui/              # Profile, sessions, connections and dangerous-account UI
    ├── authentication/ui/       # Login, provider and browser-session presentation
    ├── api-keys/ui/             # API-key list, dialogs, secret and education surfaces
    ├── organizations/ui/        # Workspace switcher, onboarding, members and settings navigation
    ├── collaboration/ui/        # Teams, invitations and invitation decisions
    ├── dashboard/ui/            # Dashboard cards, chart, table and skeleton
    └── documents/ui/            # Documentation shell, search, article, TOC and MDX UI
```

`components/ui` contains no domain policy or API calls. `app` does not own reusable presentation. A feature UI component may depend on that feature's types/routes and on shared UI primitives, but it must not own a REST client implementation. Existing `src/lib/api/**` adapters remain unchanged.

## Reference-to-target mapping

| Reference source | Existing API/data boundary | Target UI ownership | Acceptance evidence |
| --- | --- | --- | --- |
| `template/src/app/globals.css`, `template/src/components/ui/**` | None | `app/globals.css`, `components/ui/**` | shadcn visual-contract and component tests |
| `template/src/components/application/**`, home and protected layouts | Existing session/organization loaders | `features/application/ui/**` | shell, landing, header/sidebar and settings E2E screenshots |
| `template/src/features/accounts/**` | Existing auth/account REST loaders and CSRF mutations | `features/account/ui/**`, `features/authentication/ui/**` | login/profile/security/connections/danger state tests |
| `template/src/features/organizations/**`, `template/src/features/workspaces/**` | Existing organizations REST loaders and mutations | `features/organizations/ui/**`, `features/collaboration/ui/**` | onboarding, members, teams, settings and invitation journey tests |
| `template/src/features/api-keys/**` | Existing API-key REST loaders and CSRF mutations | `features/api-keys/ui/**` | personal/organization table and dialog state tests |
| `template/src/features/dashboard/**` | Existing target-owned immutable dashboard fixture | `features/dashboard/ui/**` | dashboard desktop/mobile visual and interaction tests |
| `template/src/features/documents-system/**` | Existing documents registry and generated search SDK | `features/documents/ui/**` | docs navigation, search, article and TOC visual tests |

## Layout and presentation plan

### Global design system

- Bring `globals.css` to reference parity: complete semantic tokens (including popover and chart colours), type/theme token exposure, transition easing, colour-scheme behaviour, reduced-motion view transitions and button interaction rules.
- Align all currently used shadcn primitives with their reference variants and add a reference primitive only when target UI needs it. Primitive additions remain shared only when they have no feature-domain behaviour.
- Preserve the target's fixed locale setup and hydration-safe theme switcher behaviour.

### Application composition

- Align protected shell geometry with the reference sidebar/header/inset/document-header composition, including cookie-backed sidebar state, rail behaviour, responsive drawer interaction and max content rail.
- Restore the reference settings rails, navigation placement, headers, card sections and destructive variant presentation.
- Align public landing, simple auth layouts and documents shell/header/sidebar with the reference visual hierarchy while retaining target-accurate copy and current route contracts.

### Feature surfaces

- Port visual structure, spacing, responsive rules, empty/loading/error states, dialogs, tables, forms, badges, navigation and accessibility labels from each corresponding reference feature.
- Keep current feature data passed through explicit props. Presentation changes must neither change mutation sequencing nor alter server failure handling.
- Reuse the same shared primitive for identical appearance; do not create an application-wide abstraction for a component used by only one feature.

## Validation and test-first policy

1. Before a UI port, add or update a failing test that expresses the reference-derived structural, slot, class or state contract.
2. Move/implement the presentation code until the focused test passes.
3. Keep existing unit and E2E behaviour coverage green while relocating components; add boundary tests that prevent domain UI from drifting back to broad per-domain `components/` folders.
4. Extend Playwright evidence to cover every direct migrated route at desktop and mobile viewports, in light and dark themes. EN/RU checks cover navigation and text-overflow at representative complex screens.
5. Complete with `npm run lint`, `npm run typecheck`, `npm test`, `npm run build`, relevant Playwright suites, API contract/client checks, `dotnet restore Template.sln`, `dotnet build Template.sln --no-restore`, `dotnet test Template.sln --no-restore`, and `git diff -- template/`.

## Delivery sequence

1. Create the branch from fresh `origin/main`.
2. Implement the shared CSS/shadcn foundation and its tests.
3. Implement application layouts/shell and settings composition.
4. Port independent feature UI waves through subagents, avoiding concurrent edits to shared foundation files.
5. Run route-wide integration and visual validation; document exact acceptance evidence and remaining data/copy deltas in `docs/aspnetcore-migration-plan.md`.
6. Commit, push, create a ready PR, wait for automated review, resolve every actionable finding test-first, then repeat push/review until there are no actionable comments.

## Explicit exclusions

- New product features or routes.
- Backend, persistence, OAuth, API-key, documents-index or OpenAPI redesign.
- Aspire orchestration, production reverse proxy/container work and the broader iteration 12 final parity/hardening audit.
- Editing or executing migrations in `template/`.

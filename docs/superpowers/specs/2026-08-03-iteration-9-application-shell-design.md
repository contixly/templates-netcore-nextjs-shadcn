# Iteration 9: Application Shell, Dashboard, and Frontend Parity

**Date:** 2026-08-03

**Status:** approved design

**Branch:** `codex/iteration-9-application-shell`

## 1. Objective

Complete the shared product UI composition for the migrated application while
preserving the target architecture:

- ASP.NET Core 10 owns `/api/**`, authentication, authorization, business
  behavior, persistence, and external integrations;
- Next.js is a separate REST-only UI;
- browser authentication remains a secure same-origin HttpOnly cookie;
- `template/` remains an immutable reference.

Iteration 9 covers the public product landing page, the protected application
shell, the reference dashboard presentation, shared account/workspace settings
composition, responsive states, theme, localization, metadata, and route-level
loading/error behavior. It does not introduce a new product domain.

## 2. Sources and dependency state

The design was derived from:

- `AGENTS.md`;
- `docs/aspnetcore-migration-plan.md`;
- `docs/api-conventions.md` and `docs/web-conventions.md`;
- the merged implementations of iterations 4 through 8 on `main` at
  `0ab1d77`;
- the relevant application, dashboard, account, workspace, metadata,
  localization, test, and E2E sources under immutable `template/`.

Iterations 4 through 8 already provide the account, OAuth, organization,
onboarding, team, invitation, API-key, and documentation data sources required
by this UI slice. All reference URLs already have target routes. The gap is
shared composition, presentation parity, metadata, and responsive behavior,
not missing domain endpoints.

## 3. Chosen approach

Use one reusable protected application shell with route-owned feature loaders.
The shell composes existing generated-SDK projections for navigation; feature
pages retain ownership of their reads and mutations.

This was selected over:

1. per-route wrappers, which would duplicate authentication, navigation,
   loading, and error behavior; and
2. a dashboard aggregate API or persisted analytics model, which would invent
   business and schema semantics absent from the reference and iteration scope.

The reference organization dashboard is presentation-owned static data. The
target therefore keeps its cards, chart, table, filtering, pagination,
reordering, and edit interactions local to the browser and does not imply
persistence.

## 4. Scope

### 4.1 Included routes

The following URLs retain their existing public contracts:

- `/`;
- `/auth/login` and `/auth/error`;
- `/dashboard`;
- `/welcome` and `/workspaces`;
- `/invite/{invitationId}`;
- `/user` and `/user/{profile,invitations,connections,security,api-keys,danger}`;
- `/w/{organizationKey}` and `/w/{organizationKey}/dashboard`;
- `/w/{organizationKey}/settings` and
  `/w/{organizationKey}/settings/{workspace,invitations,users,teams,roles,api-keys}`;
- `/docs/**`, which keeps its iteration-8 documents shell rather than joining
  the protected shell.

### 4.2 Included UI work

- adapt the reference landing-page composition for ASP.NET Core plus REST;
- split public, auth, documents, and protected route groups without changing
  URLs;
- add a persistent responsive protected shell with sidebar, header,
  breadcrumbs, page header, documentation shortcut, account navigation, and
  organization navigation;
- preserve dashboard and organization canonical-routing behavior;
- reproduce the static interactive organization dashboard;
- establish shared account/workspace settings shell primitives;
- preserve permission-derived navigation visibility and API enforcement;
- complete paired English/Russian application and dashboard messages;
- add safe localized route metadata, manifest, robots, sitemap, icons, and
  public social metadata surfaces;
- standardize loading, error, not-found, unauthorized, and forbidden surfaces;
- perform route-by-route desktop and mobile behavioral/visual review.

### 4.3 Excluded work

- dashboard persistence, analytics ingestion, KPI calculation, or reporting;
- new ASP.NET endpoints, OpenAPI operations, EF models, migrations, indexes,
  seeds, transactions, cache invalidation, background jobs, or audit events;
- user-selected or URL-prefixed locale;
- Yandex Metrika or another analytics integration;
- Aspire, ServiceDefaults, Redis/Valkey, or local orchestration from iteration
  10;
- YARP, Docker/process supervision, or production topology from iteration 11;
- the final security/performance/accessibility/SEO/backup/license parity audit
  or any decision to delete/archive `template/` from iteration 12;
- active OpenSpec changes or specs.

## 5. Reference-to-target correspondence

| Reference | Existing API | New UI | Primary evidence |
| --- | --- | --- | --- |
| `components/application/app-sidebar.tsx`, `app-site-header.tsx`, breadcrumbs and navigation | current session, account, organizations, active-organization, CSRF/logout operations | responsive protected shell | shell Jest tests; desktop/mobile Playwright journeys |
| `/dashboard` resolver | session plus organization list/detail | active organization, deterministic first organization, or welcome redirect | routing Jest tests and organization-routing E2E |
| organization dashboard, `features/dashboard/ui/template/**`, `dashboard/data.json` | none; no new API | target-owned static cards, chart, table, drawer, and skeleton | component interaction tests and dashboard E2E |
| `settings-shell.tsx`, account/workspace settings navigation | existing account/organization capability projections and existing feature APIs | shared settings rail, sections, responsive navigation, permission-aware links | settings component tests and permission E2E |
| `(public)/(home)` and `features/application/**` | none required | target-architecture landing page | landing component tests and landing-to-login/docs E2E |
| `messages/common.*` plus application/dashboard messages | none | paired `en`/`ru` catalogs | deep message-shape tests |
| metadata helpers, per-route metadata, manifest, robots, sitemap, social images | none | safe target-owned product metadata | metadata/SEO unit tests and production build |
| root error/status surfaces and reference Suspense fallbacks | existing safe Problem Details | shell-aware loading/errors and provider-independent global error | boundary unit tests and E2E |

## 6. Route and component architecture

### 6.1 Route groups

`apps/web/src/app` will have distinct route groups for:

- public product landing;
- simple authentication pages;
- public documents;
- protected product routes.

Moving files between route groups must not alter their URLs. Public landing,
authentication, and documents routes must never inherit the protected loader or
expose protected fallback content.

The protected layout calls `connection()` and renders one shared shell. Cache
Components must not cause live API access during `next build`.

### 6.2 Shell data model

One React-cached server loader composes the minimum presentation model from the
existing generated SDK:

- current authenticated session;
- current account projection;
- the bounded first page of accessible organizations;
- current organization detail when the URL is organization-scoped.

The shell must not become a business/application service. It may map API types
to a narrow presentation model but must not duplicate API DTOs or business
rules. Feature pages continue to own their own data loaders and mutations.
Existing cached loaders must be reused so layout and page composition do not
issue duplicate equivalent requests.

`BrowserSessionRefresh` is mounted once for a protected navigation. SSR reads
continue to suppress sliding renewal.

### 6.3 Shell components

Components have narrow responsibilities:

- `ProtectedApplicationShell`: page frame, scroll ownership, skip-link target,
  and `#main-content`;
- `ApplicationSidebar`: desktop collapse, mobile drawer, rail, and state;
- `ApplicationHeader`: sidebar trigger, breadcrumbs, docs shortcut, theme;
- `PrimaryNavigation`: dashboard, workspaces, and create-workspace actions;
- `OrganizationNavigation`: organization switcher and compatible deep-route
  suffix preservation;
- `AccountNavigation`: account routes and logout;
- `PageHeader`: localized page title, description, and optional actions;
- `SettingsPageShell`: responsive settings navigation and content rail;
- `SettingsSection`: semantic readable/wide/destructive section islands.

Sidebar preference is non-sensitive and may use a dedicated application
preference cookie. It must be separate from session cookies. Session cookies
remain secure and HttpOnly and are never read by browser JavaScript.

### 6.4 Dashboard

The dashboard preserves the reference presentation:

- four KPI cards;
- a responsive 90/30/7-day chart, with the reference mobile default;
- a local table with tabs, selection, sorting/filtering, visibility,
  pagination, row reorder, and edit drawer;
- a route-specific skeleton.

The fixture is target-owned static data. All edits are page-local state and
reset with navigation/reload. The UI must not claim that demo changes were
saved to the server.

Only direct, pinned dependencies necessary for these behaviors will be added,
including the compatible chart, table, drag-and-drop, toast, and drawer
primitives. Installed Next.js documentation and official library documentation
must be checked before relying on version-sensitive behavior.

### 6.5 Landing and settings

The public `/` page uses the reference hero, calls to action, feature sections,
and footer composition, but all copy describes the target ASP.NET Core plus
REST architecture. Reference-era claims about Prisma, Better Auth, Server
Actions, and Next.js-owned product APIs must not be presented as current.

Existing settings forms and generated-SDK operations are recomposed inside the
shared settings shell rather than rewritten. Workspace navigation order and
visibility come from an explicit route catalog and trusted API capabilities.
Hiding an unavailable link is a presentation convenience and never replaces
API authorization.

## 7. REST, authorization, and state contract

### 7.1 Existing operations

Iteration 9 does not change OpenAPI. The shell uses existing operations,
including:

- `GET /api/v1/auth/session`;
- `GET /api/v1/account`;
- `GET /api/v1/organizations`;
- `GET /api/v1/organizations/by-key/{organizationKey}`;
- `PUT /api/v1/auth/session/active-organization`;
- existing CSRF and `POST /api/v1/auth/logout` operations.

Every browser/SSR call uses the generated SDK. There are no handwritten
transport DTOs, raw product `fetch` calls, product Route Handlers, Server
Actions, Prisma, Better Auth, direct database access, or bearer tokens in
browser storage.

### 7.2 Authentication and authorization

- protected routes use only the secure same-origin browser-session cookie;
- server reads forward only allowed cookie and correlation headers;
- safe reads are `no-store` and suppress sliding renewal;
- unsafe operations retain the current antiforgery-cookie plus
  `X-CSRF-TOKEN` flow;
- an anonymous protected request redirects to login with a validated local
  return path;
- API/config/network failure is not treated as anonymous or as a user with no
  organizations;
- organization access is resolved before canonical redirects and before
  displaying organization names or capabilities;
- the API remains the final authorization boundary.

### 7.3 Validation, errors, pagination, and filtering

No new HTTP request schema or validation rule is introduced.

The organization list retains its existing opaque cursor, default/max limit,
ordering, and validation. The shell consumes the bounded first page and passes
`nextCursor` through without decoding it; the workspaces surface remains the
place to continue browsing. Every other migrated feature retains its existing
cursor/filter contract unchanged.

Dashboard sorting, filtering, selection, range choice, pagination, and edits
are local presentation state, not REST semantics.

UI failures expose localized safe messages and, when available, only a safe
`traceId`. Problem `detail`, exception/provider text, route/query/body/cursor
values, cookies, and credentials are not rendered. Authorization and
non-disclosure precedence remain unchanged.

### 7.4 Transactions and schema

There is no iteration-9 data migration or transaction. Existing account,
organization, collaboration, invitation, and API-key transaction behavior is
unchanged. No new persistent state, cache invalidation event, or background
work exists for the shell or dashboard.

## 8. Localization, metadata, and boundaries

Locale remains deployment-fixed `en` or `ru`, with the established safe English
fallback. There is no locale switcher or URL prefix. Application/dashboard
catalogs must have identical deep shapes in both languages.

Product routes receive localized titles and descriptions from a target-owned
route catalog. Dynamic protected metadata must not contain organization names
or other protected data before successful authorized loading. Protected and
authentication routes are `noindex`; sitemap contains only public landing and
documentation URLs.

The target adds its own manifest, icons, robots, and public Open Graph/Twitter
surfaces. It does not copy obsolete architecture keywords or analytics config.

Loading and error UI renders inside the persistent shell where safe. The
following conditions remain distinct:

- anonymous authentication redirect;
- API/config/network failure;
- zero-organization onboarding;
- inaccessible or missing organization;
- route not found;
- unexpected segment or global error.

The global error boundary must render without an i18n or application provider
and must not display raw error text.

## 9. Test-first implementation strategy

Implementation is divided among bounded subagent workstreams after a written
implementation plan:

1. route groups, cached shell loader, and auth boundaries;
2. sidebar/header/navigation/settings primitives;
3. dashboard presentation and skeleton;
4. landing, localization, metadata, and error/loading surfaces;
5. E2E, parity audit, and durable documentation.

Each workstream starts with a focused failing test. The primary agent
integrates changes, resolves overlaps, and runs the complete repository gates.

### 9.1 Unit/component/contract coverage

- one browser session renewal and no duplicate equivalent shell reads;
- desktop/mobile sidebar, active states, close behavior, keyboard and focus;
- organization-aware dashboard/navigation links;
- permission-derived settings navigation;
- shared settings widths, semantic regions, and destructive emphasis;
- KPI/chart/table local interactions and dashboard skeleton;
- English/Russian deep message-shape parity;
- safe localized metadata, manifest, robots, and sitemap;
- safe loading, error, unauthorized, forbidden, not-found, and global-error
  boundaries;
- source-boundary rejection of forbidden frontend dependencies/patterns.

### 9.2 Browser coverage

- landing to login and documentation;
- authenticated desktop and mobile shell navigation;
- dashboard resolver, canonical organization route, and zero-organization
  onboarding;
- static dashboard interactions without persistence claims;
- account/workspace settings navigation and active states;
- capability-derived link hiding plus direct authorization denial;
- theme persistence and hydration stability;
- responsive smoke for every migrated protected route.

Desktop/mobile screenshots provide review evidence, but CI should prefer
semantic and behavioral assertions over brittle full-page pixel baselines.

## 10. Acceptance gates

At minimum, final evidence includes:

```text
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
```

The web gates include content generation/checks, generated API drift checks,
frontend boundary checks, formatting, lint, type checking, full Jest,
production standalone build, production dependency audit, and relevant full
Playwright E2E.

Repository guards include:

- `git diff --check`;
- no branch-range or working-tree changes under `template/`;
- no active OpenSpec change/spec;
- deterministic generated content/client artifacts.

`docs/aspnetcore-migration-plan.md` will record the corrected current
iteration, scope, correspondence table, actual command results, known
differences, and next blockers. Durable web composition and security decisions
will also be reflected in `docs/web-conventions.md`.

## 11. Pull-request completion loop

After local acceptance:

1. commit and push the branch;
2. open a ready (not draft) pull request against `main`;
3. request GitHub Codex automatic review on the current head;
4. inspect every inline/general finding and unresolved thread;
5. reproduce actionable findings with a failing test before fixing them;
6. rerun focused and relevant full gates, commit, and push;
7. request a fresh review for the new head;
8. repeat until the latest head has a clean automatic review and zero
   unresolved actionable review threads.

Review status is external PR/controller evidence. Durable repository documents
must not claim a clean result for a head that was not actually observed.

## 12. Intentional differences from the reference

- dashboard data and edits remain presentation-only rather than becoming a
  fabricated analytics domain;
- landing copy describes ASP.NET Core plus REST rather than the former
  full-stack Next.js architecture;
- protected/authentication routes are not indexed;
- fixed deployment locale remains unchanged;
- Yandex Metrika is excluded;
- security, authorization, non-disclosure, and REST boundaries take priority
  over implementation-level visual parity.

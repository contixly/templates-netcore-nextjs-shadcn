# Iteration 9 Application Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`/`- [x]`) syntax for tracking.

**Goal:** Deliver the public landing page, responsive protected application shell, static interactive dashboard, shared settings composition, localized metadata, and route-parity evidence for migration iteration 9 without changing the REST or persistence contract.

**Architecture:** Keep ASP.NET Core as the owner of `/api/**`, authentication, authorization, business logic, and persistence. A route-aware Next.js parallel slot composes a shell projection from request-cached constituent generated-SDK loaders for session, account, organizations, and current organization; the composite shell function itself is not React-cached. Feature pages retain their current REST loaders and mutations. The reference dashboard remains target-owned static presentation state rather than becoming a new backend domain.

**Implemented caching decision:** This supersedes design spec §6.2's proposed composite React-cached loader. One route-aware navigation slot owns one composite invocation for each protected leaf. Request-cached constituent session, account, organization-list, and current-organization loaders deduplicate equivalent upstream projections shared with page composition, so the composite function is intentionally not cached.

**Tech Stack:** .NET 10 verification only; Next.js 16.2.11 App Router with Cache Components; React 19.2.8; TypeScript 6.0.3; next-intl 4.13.4; Tailwind CSS 4.3.3; shadcn/radix-ui; Jest 30; Playwright 1.61.1; generated Hey API client; Recharts 3.9.1; TanStack Table 8.21.3; dnd-kit; Sonner; Vaul; Zod 4.4.3.

**Execution status (2026-08-03):** Tasks 1–8 are implemented and locally reviewed at implementation head `180f29b40099633bcbf55baadb6b873bd88965c3`. Task 9 is complete with fresh local acceptance evidence and its documentation commit. Task 10 (push, ready PR, and current-head GitHub review) has not started and is not claimed.

## Global Constraints

- `template/` is immutable: read and compare only; never edit, format, move, delete, install into, or run migrations in it.
- Work only on `codex/iteration-9-application-shell`, created from fresh `origin/main` at `0ab1d77`.
- Do not create an OpenSpec change/spec.
- ASP.NET Core owns `/api/**`; `apps/web` uses only the generated REST SDK for product data.
- Do not add Server Actions, raw product `fetch`, Prisma, Better Auth, direct database access, product Route Handlers, or bearer tokens in browser storage.
- Browser authentication remains the secure same-origin HttpOnly session cookie; unsafe requests retain the existing antiforgery flow.
- Preserve existing authorization, non-disclosure, cursor, filtering, validation, error, and transaction behavior.
- Do not add an API endpoint, OpenAPI operation, EF model/migration, table, index, seed, transaction, audit event, cache invalidation event, or background job.
- Locale remains deployment-fixed `en | ru` with no URL prefix or language switcher.
- Protected/authentication routes are `noindex`; sitemap contains only public landing and documentation URLs.
- Dashboard data and edits are presentation-only and must not claim persistence.
- Write a failing focused test before each behavioral implementation.
- Before modifying Next.js behavior, read `apps/web/node_modules/next/README.md`, `apps/web/node_modules/next/dist/server/request/connection.d.ts`, and installed metadata types under `apps/web/node_modules/next/dist/lib/metadata/types/`.
- Pin every new direct npm dependency to an exact version and update `apps/web/package-lock.json` with npm 11.18.0.
- Every commit must leave `git diff --check` clean and `git diff -- template/` empty.

## File and Responsibility Map

### Route and message contracts

- `apps/web/src/features/application/application-page-catalog.ts`: closed product route IDs, matchers, visibility, navigation, and metadata keys.
- `apps/web/src/features/application/application-routes.ts`: typed public and product URLs.
- `apps/web/src/messages/application.{en,ru}.json`: landing, shell, navigation, page titles, and boundary copy.
- `apps/web/src/messages/dashboard.{en,ru}.json`: dashboard cards, chart, table, and demo-state copy.
- `apps/web/src/i18n/messages.ts`: paired namespace registration.

### Protected shell

- `apps/web/src/features/application/application-shell-model.ts`: narrow shell presentation types.
- `apps/web/src/lib/api/application/server/load-application-shell.ts`: composite generated-SDK shell projection over request-cached constituent loaders.
- `apps/web/src/components/application/application-navigation-slot.tsx`: safe loader-to-UI boundary and the single session-refresh mount.
- `apps/web/src/app/(protected)/@applicationNavigation/**`: exact route-aware login return path and organization context.
- `apps/web/src/components/application/{protected-application-shell,application-sidebar,application-header,application-breadcrumbs,primary-navigation,account-navigation,page-header}.tsx`: isolated shell components.
- `apps/web/src/components/application/sidebar-state.ts`: non-sensitive preference cookie parsing/serialization.
- `apps/web/src/hooks/use-mobile-sidebar-close.ts`: close-only mobile navigation behavior.

### Settings and dashboard

- `apps/web/src/components/application/settings/settings-shell.tsx`: settings rail, page intro, section, and semantic card primitives.
- Existing account/workspace pages: retain REST behavior and adopt the shared composition.
- `apps/web/src/features/dashboard/dashboard-data.ts`: validated static fixture.
- `apps/web/src/components/dashboard/**`: cards, chart, table, page, and skeleton.

### Landing, metadata, and verification

- `apps/web/src/components/application/landing/**`: public hero, feature grid, footer, and page composition.
- `apps/web/src/lib/metadata.ts`: fixed-locale page metadata builder.
- `apps/web/src/app/{manifest,robots,opengraph-image,twitter-image}.*`: public metadata surfaces.
- `apps/web/src/app/{unauthorized,forbidden,error,global-error,loading,not-found}.tsx`: safe boundaries.
- `apps/web/e2e/application-shell.spec.ts`: desktop/mobile application journeys.
- `docs/web-conventions.md` and `docs/aspnetcore-migration-plan.md`: durable decisions and observed evidence.

---

### Task 1: Freeze route, navigation, and localization contracts

**Files:**
- Create: `apps/web/src/features/application/application-page-catalog.ts`
- Create: `apps/web/src/messages/application.en.json`
- Create: `apps/web/src/messages/application.ru.json`
- Create: `apps/web/src/messages/dashboard.en.json`
- Create: `apps/web/src/messages/dashboard.ru.json`
- Modify: `apps/web/src/features/application/application-routes.ts`
- Modify: `apps/web/src/i18n/messages.ts`
- Test: `apps/web/test/features/application-page-catalog.test.ts`
- Test: `apps/web/test/i18n/messages.test.ts`

**Interfaces:**
- Consumes: typed builders from `applicationRoutes`, `accountRoutes`, `organizationRoutes`, `collaborationRoutes`, and `apiKeyRoutes`.
- Produces: `ApplicationPageId`, `ApplicationPageDefinition`, `applicationPageCatalog`, `resolveApplicationPage(pathname)`, and paired `application`/`dashboard` namespaces.

- [x] **Step 1: Write failing catalog and message tests**

```ts
import {
  applicationPageCatalog,
  resolveApplicationPage,
} from "@/src/features/application/application-page-catalog";

it.each([
  ["/", "home", true],
  ["/dashboard", "dashboard", false],
  ["/user/security", "accountSecurity", false],
  ["/w/acme/dashboard", "organizationDashboard", false],
  ["/w/acme/settings/teams", "organizationTeams", false],
])("resolves %s to %s", (pathname, id, indexable) => {
  expect(resolveApplicationPage(pathname)).toMatchObject({ id, indexable });
});

it("keeps catalog IDs unique", () => {
  const ids = applicationPageCatalog.map(({ id }) => id);
  expect(new Set(ids).size).toBe(ids.length);
});
```

Extend `apps/web/test/i18n/messages.test.ts`:

```ts
expect(english.application.shell.navigation.dashboard).toBe("Dashboard");
expect(russian.application.shell.navigation.dashboard).toBe("Панель управления");
expect(english.dashboard.table.demoNotice).toMatch(/not saved/i);
expect(russian.dashboard.table.demoNotice).toMatch(/не сохраня/iu);
```

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand test/features/application-page-catalog.test.ts test/i18n/messages.test.ts
```

Expected: FAIL because the catalog and namespaces do not exist.

- [x] **Step 3: Implement the closed page catalog**

```ts
export type ApplicationPageDefinition = Readonly<{
  id: ApplicationPageId;
  indexable: boolean;
  messageKey: string;
  match: (pathname: string) => boolean;
}>;

const exact = (path: string) => (pathname: string) => pathname === path;
const pattern = (value: RegExp) => (pathname: string) => value.test(pathname);

export function resolveApplicationPage(
  pathname: string,
): ApplicationPageDefinition | null {
  return applicationPageCatalog.find(({ match }) => match(pathname)) ?? null;
}
```

Include IDs for home, login/auth-error, dashboard, welcome, workspaces,
invitation decision, all six account pages, organization root/dashboard, and
all six workspace settings subsections. Static matches precede anchored dynamic
matches. Add `docs: "/docs" as Route` to `applicationRoutes`.

- [x] **Step 4: Add complete paired message bundles**

Add English/Russian strings for landing sections, shell navigation, sidebar,
breadcrumbs, all product page titles/descriptions, safe boundaries, dashboard
cards/ranges/table/drawer, and the explicit non-persistence notice. Register
both namespaces in `englishMessages` and `messagesByLocale`.

- [x] **Step 5: Run focused tests and type checking**

```bash
cd apps/web
npm test -- --runInBand test/features/application-page-catalog.test.ts test/i18n/messages.test.ts
npm run typecheck
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add apps/web/src/features/application apps/web/src/i18n/messages.ts apps/web/src/messages apps/web/test/features/application-page-catalog.test.ts apps/web/test/i18n/messages.test.ts
git commit -m "feat: define iteration 9 UI contracts"
```

### Task 2: Establish route-aware protected shell loading

**Files:**
- Create: `apps/web/src/features/application/application-shell-model.ts`
- Create: `apps/web/src/lib/api/application/server/load-application-shell.ts`
- Create: `apps/web/src/components/application/application-navigation-slot.tsx`
- Modify: `apps/web/src/lib/api/account/server/load-account.ts`
- Rename: `apps/web/src/app/(site)` to `apps/web/src/app/(protected)`
- Rename: `apps/web/src/app/(protected)/@organizationSwitcher` to `apps/web/src/app/(protected)/@applicationNavigation`
- Move: `apps/web/src/app/(protected)/page.tsx` to `apps/web/src/app/(public)/(home)/page.tsx`
- Modify: `apps/web/src/app/(protected)/layout.tsx`
- Modify: route-slot pages under `apps/web/src/app/(protected)/@applicationNavigation/`
- Modify: Jest imports that reference `@/src/app/(site)`
- Test: `apps/web/test/lib/api/application-shell.test.ts`
- Test: `apps/web/test/app/protected-layout.test.tsx`
- Test: `apps/web/test/app/application-navigation-slot.test.tsx`

**Interfaces:**
- Consumes: `loadProtectedSession(redirectPath)`, `loadAccount`, `loadOrganizations`, optional `loadOrganization`, and Task 1 routes.
- Produces:

```ts
export type ApplicationShellData = Readonly<{
  account: AccountResponse;
  organizations: readonly Extract<
    OrganizationSummaryResponse,
    { accessPrincipal: "user" }
  >[];
  nextOrganizationCursor: string | null;
  session: AuthSessionMetadataResponse;
  user: AuthUserResponse;
  currentOrganization: OrganizationDetailResponse | null;
}>;

export type ApplicationShellResult = ApiResult<ApplicationShellData>;

export function loadApplicationShell(
  redirectPath: string,
  organizationKey?: string,
): Promise<ApplicationShellResult>;
```

- [x] **Step 1: Write failing loader/layout tests**

```ts
it("does not load shell data after an anonymous redirect", async () => {
  mockLoadProtectedSession.mockImplementation(() => {
    throw new Error("redirect:/auth/login?redirect=%2Fuser%2Fsecurity");
  });
  await expect(loadApplicationShell("/user/security")).rejects.toThrow(
    "redirect:/auth/login?redirect=%2Fuser%2Fsecurity",
  );
  expect(mockLoadAccount).not.toHaveBeenCalled();
  expect(mockLoadOrganizations).not.toHaveBeenCalled();
});

it("loads each authenticated projection once", async () => {
  await expect(loadApplicationShell("/w/acme/dashboard", "acme"))
    .resolves.toMatchObject({
      ok: true,
      data: { currentOrganization: { canonicalKey: "acme" } },
    });
  expect(mockLoadAccount).toHaveBeenCalledTimes(1);
  expect(mockLoadOrganizations).toHaveBeenCalledTimes(1);
  expect(mockLoadOrganization).toHaveBeenCalledWith("acme");
});
```

The layout test asserts one navigation slot, one `main-content` target, and no
public-home import through the protected layout.

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand test/lib/api/application-shell.test.ts test/app/protected-layout.test.tsx test/app/application-navigation-slot.test.tsx
```

Expected: FAIL because shell modules and the protected route group are absent.

- [x] **Step 3: Cache account loading and compose the shell model**

Refactor `load-account.ts` without changing its generated call:

```ts
async function loadAccountUncached(): Promise<AccountResult> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  try {
    const result = await getAccount({
      client: client.client,
      cache: "no-store",
      headers: { "X-Template-Session-Renewal": "suppress" },
    });
    return result.data !== undefined
      ? { ok: true, data: result.data.data }
      : { ok: false, failure: normalizeApiFailure(result.error, result.response) };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}

export const loadAccount = cache(loadAccountUncached);
```

`loadApplicationShell` calls `loadProtectedSession` first. Only authenticated
success proceeds to parallel account, organization-page, and optional current-
organization reads. A malformed success maps to the existing safe
`api_unavailable` failure; any API/config/network failure remains distinct from
anonymous and zero-organization states.

- [x] **Step 4: Rename route groups and preserve URLs**

```bash
git mv 'apps/web/src/app/(site)' 'apps/web/src/app/(protected)'
git mv 'apps/web/src/app/(protected)/@organizationSwitcher' 'apps/web/src/app/(protected)/@applicationNavigation'
mkdir -p 'apps/web/src/app/(public)/(home)'
git mv 'apps/web/src/app/(protected)/page.tsx' 'apps/web/src/app/(public)/(home)/page.tsx'
```

Update test imports. Each slot leaf calls `ApplicationNavigationSlot` with the
exact redirect URL; organization leaves also pass `organizationKey`. Add an
inventory assertion that every protected leaf has a matching navigation-slot
leaf.

- [x] **Step 5: Implement the safe navigation-slot boundary**

Call `connection()`, then `loadApplicationShell`. On failure render localized
safe copy and only an optional `traceId`. On success render exactly one
`BrowserSessionRefresh` and a temporary semantic
`<nav data-slot="application-navigation">`; Task 3 replaces its presentation.

- [x] **Step 6: Run affected tests**

```bash
cd apps/web
npm test -- --runInBand test/lib/api/application-shell.test.ts test/app/protected-layout.test.tsx test/app/application-navigation-slot.test.tsx test/app/organization-routing.test.tsx test/app/organization-switcher-slot.test.tsx test/app/account-pages.test.tsx
npm run typecheck
```

Expected: PASS with unchanged URLs and redirect targets.

- [x] **Step 7: Commit**

```bash
git add apps/web/src apps/web/test
git commit -m "feat: establish protected application shell boundary"
```

### Task 3: Build responsive sidebar, header, and navigation

**Files:**
- Create: `apps/web/src/components/application/protected-application-shell.tsx`
- Create: `apps/web/src/components/application/application-sidebar.tsx`
- Create: `apps/web/src/components/application/application-header.tsx`
- Create: `apps/web/src/components/application/application-breadcrumbs.tsx`
- Create: `apps/web/src/components/application/primary-navigation.tsx`
- Create: `apps/web/src/components/application/account-navigation.tsx`
- Create: `apps/web/src/components/application/page-header.tsx`
- Create: `apps/web/src/components/application/sidebar-state.ts`
- Create: `apps/web/src/hooks/use-mobile-sidebar-close.ts`
- Create: `apps/web/src/components/ui/avatar.tsx`
- Create: `apps/web/src/components/ui/breadcrumb.tsx`
- Create: `apps/web/src/components/ui/collapsible.tsx`
- Create: `apps/web/src/components/ui/sheet.tsx`
- Create: `apps/web/src/components/ui/sidebar.tsx`
- Create: `apps/web/src/components/ui/tooltip.tsx`
- Modify: `apps/web/src/components/application/application-navigation-slot.tsx`
- Modify: `apps/web/src/components/application/app-providers.tsx`
- Modify: `apps/web/src/app/(protected)/layout.tsx`
- Modify: `apps/web/src/app/globals.css`
- Test: `apps/web/test/components/application-sidebar.test.tsx`
- Test: `apps/web/test/components/application-header.test.tsx`
- Test: `apps/web/test/components/application-navigation.test.tsx`
- Test: `apps/web/test/components/sidebar-state.test.ts`

**Interfaces:**
- Consumes: `ApplicationShellData`, Task 1 catalog, current
  `OrganizationSwitcher`, `LogoutButton`, `ThemeSwitcher`, and Task 2 slot.
- Produces: `ProtectedApplicationShell`, `ApplicationSidebar`,
  `ApplicationHeader`, `PageHeader`, and sidebar-cookie helpers.

- [x] **Step 1: Write failing responsive/navigation tests**

```tsx
render(<ApplicationSidebar data={shellData} pathname="/w/acme/dashboard" />);
expect(screen.getByRole("link", { name: "Dashboard" })).toHaveAttribute(
  "href",
  "/w/acme/dashboard",
);
expect(screen.getByRole("link", { name: "Documentation" })).toHaveAttribute(
  "href",
  "/docs",
);
expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
```

```ts
expect(parseSidebarPreference("template.sidebar=open")).toBe(true);
expect(parseSidebarPreference("template.sidebar=closed")).toBe(false);
expect(parseSidebarPreference("template.sidebar=invalid")).toBe(false);
expect(serializeSidebarPreference(true)).toContain("SameSite=Lax");
```

Mock `useSidebar()` as mobile and assert a navigation click calls
`setOpenMobile(false)` without toggling desktop state.

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand test/components/application-sidebar.test.tsx test/components/application-header.test.tsx test/components/application-navigation.test.tsx test/components/sidebar-state.test.ts
```

Expected: FAIL because responsive shell components are absent.

- [x] **Step 3: Add missing primitives**

```bash
cd apps/web
npx shadcn@4.14.1 add avatar breadcrumb collapsible sheet sidebar tooltip --yes
```

Do not overwrite an existing component. Adapt new files to the repository's
`radix-lyra` style and current button/input/separator/skeleton APIs.

- [x] **Step 4: Implement sidebar preference and isolated components**

Use cookie name `template.sidebar`, values `open|closed`, `Path=/`,
`SameSite=Lax`, and 30-day `Max-Age`. Missing/invalid means closed. Never reuse
the auth cookie.

Compose the frame:

```tsx
<SidebarProvider defaultOpen={defaultSidebarOpen}>
  {navigation}
  <SidebarInset>
    <ApplicationHeader />
    <main id="main-content" tabIndex={-1}>{children}</main>
  </SidebarInset>
</SidebarProvider>
```

Only the inset scrolls. Active links use `aria-current="page"`. Dashboard uses
the current organization when present and `/dashboard` otherwise. Docs,
workspaces/create, account, logout, theme, and organization controls remain
keyboard-accessible. Mobile navigation closes explicitly.

- [x] **Step 5: Add providers and shell CSS tokens**

Add `TooltipProvider`, sidebar/header CSS variables, and a deterministic shell
readiness attribute. Keep theme storage key `template.theme` and the current
hydration-safe disabled fallback.

- [x] **Step 6: Run focused and affected suites**

```bash
cd apps/web
npm test -- --runInBand test/components/application-sidebar.test.tsx test/components/application-header.test.tsx test/components/application-navigation.test.tsx test/components/sidebar-state.test.ts test/components/organization-switcher.test.tsx test/components/theme-switcher.test.tsx test/components/browser-session-refresh.test.tsx test/app/boundaries.test.tsx
npm run typecheck
npm run boundaries:check
```

Expected: PASS.

- [x] **Step 7: Commit**

```bash
git add apps/web/src apps/web/test apps/web/package.json apps/web/package-lock.json
git commit -m "feat: add responsive application navigation"
```

### Task 4: Recompose account and workspace settings

**Files:**
- Create: `apps/web/src/components/application/settings/settings-shell.tsx`
- Modify: `apps/web/src/components/account/account-nav.tsx`
- Modify: `apps/web/src/components/organizations/organization-settings-nav.tsx`
- Modify: `apps/web/src/app/(protected)/user/layout.tsx`
- Modify: account pages under `apps/web/src/app/(protected)/user/{profile,connections,security,invitations,api-keys,danger}/page.tsx`
- Modify: `apps/web/src/app/(protected)/w/[organizationKey]/settings/layout.tsx`
- Modify: workspace pages under `apps/web/src/app/(protected)/w/[organizationKey]/settings/{workspace,users,roles,teams,invitations,api-keys}/page.tsx`
- Test: `apps/web/test/components/settings-shell.test.tsx`
- Test: `apps/web/test/components/account-nav.test.tsx`
- Test: `apps/web/test/app/account-pages.test.tsx`
- Test: `apps/web/test/app/organization-settings-pages.test.tsx`
- Test: `apps/web/test/app/team-settings-pages.test.tsx`
- Test: `apps/web/test/app/api-key-pages.test.tsx`

**Interfaces:**
- Consumes: existing forms/lists, capability projections, and Task 3 `PageHeader`.
- Produces: `SettingsPageShell`, `SettingsContentRail`,
  `SettingsPageSection`, `SettingsPageIntro`, and `SettingsSection`.

- [x] **Step 1: Write failing settings tests**

```tsx
render(
  <SettingsPageSection mode="readable">
    <SettingsPageIntro title="Profile settings" description="Review details" />
    <SettingsSection title="Display name">Form</SettingsSection>
  </SettingsPageSection>,
);
expect(screen.getByRole("heading", { level: 1, name: "Profile settings" }))
  .toBeInTheDocument();
expect(screen.getByRole("region", { name: "Display name" }))
  .toHaveAttribute("data-variant", "default");
expect(screen.getByText("Form").closest('[data-mode="readable"]'))
  .toHaveClass("max-w-3xl");
```

Add nav assertions for exact order
`workspace, users, teams, roles, invitations, apiKeys`, active state, and
capability-based omission of invitations/API keys.

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand test/components/settings-shell.test.tsx test/components/account-nav.test.tsx test/app/account-pages.test.tsx test/app/organization-settings-pages.test.tsx
```

Expected: FAIL on missing shared primitives and old composition.

- [x] **Step 3: Implement semantic settings primitives**

```ts
export type SettingsPageSectionMode = "wide" | "readable";
export type SettingsSectionVariant = "default" | "destructive";
```

`SettingsSection` uses `useId()`, `role="region"`, `aria-labelledby`, and
stable `data-slot`, `data-mode`, and `data-variant` attributes. The readable
rail is `max-w-3xl`; the wide rail uses the shared `max-w-6xl` shell.

- [x] **Step 4: Recompose account settings without changing behavior**

Wrap `ProfileForm`, `ConnectionsList`, `SessionList`, invitation list, API-key
management, and delete dialog. Keep generated SDK calls, causal overlays, CSRF,
failure mapping, and pagination unchanged. Danger uses destructive; profile and
security use readable; list-heavy pages use wide.

- [x] **Step 5: Recompose workspace settings without changing authorization**

Pass only trusted `canManageInvitations` and `canManageApiKeys` capabilities to
navigation. Preserve direct-route 403, onboarding replacement, canonical keys,
loading skeleton, and every organization/member/team/invitation/API-key
operation unchanged.

- [x] **Step 6: Run affected settings suites**

```bash
cd apps/web
npm test -- --runInBand test/components/settings-shell.test.tsx test/components/account-nav.test.tsx test/app/account-pages.test.tsx test/app/organization-settings-pages.test.tsx test/app/team-settings-pages.test.tsx test/app/api-key-pages.test.tsx test/components/profile-form.test.tsx test/components/session-list.test.tsx test/components/team-directory.test.tsx test/components/api-keys/api-key-management.test.tsx
npm run typecheck
```

Expected: PASS.

- [x] **Step 7: Commit**

```bash
git add apps/web/src/components/application/settings apps/web/src/components/account apps/web/src/components/organizations 'apps/web/src/app/(protected)/user' 'apps/web/src/app/(protected)/w/[organizationKey]/settings' apps/web/test
git commit -m "feat: unify settings surface composition"
```

### Task 5: Port the static interactive dashboard

**Files:**
- Create: `apps/web/src/features/dashboard/dashboard-data.ts`
- Create: `apps/web/src/features/dashboard/dashboard-routes.ts`
- Create: `apps/web/src/components/dashboard/dashboard-page.tsx`
- Create: `apps/web/src/components/dashboard/dashboard-skeleton.tsx`
- Create: `apps/web/src/components/dashboard/section-cards.tsx`
- Create: `apps/web/src/components/dashboard/activity-chart.tsx`
- Create: `apps/web/src/components/dashboard/activity-table.tsx`
- Create: `apps/web/src/components/ui/chart.tsx`
- Create: `apps/web/src/components/ui/drawer.tsx`
- Create: `apps/web/src/components/ui/sonner.tsx`
- Create: `apps/web/src/components/ui/toggle-group.tsx`
- Modify: `apps/web/src/components/application/app-providers.tsx`
- Modify: `apps/web/src/app/(protected)/w/[organizationKey]/dashboard/page.tsx`
- Modify: `apps/web/src/app/(protected)/w/[organizationKey]/dashboard/loading.tsx`
- Modify: `apps/web/package.json`
- Modify: `apps/web/package-lock.json`
- Test: `apps/web/test/components/dashboard/section-cards.test.tsx`
- Test: `apps/web/test/components/dashboard/activity-chart.test.tsx`
- Test: `apps/web/test/components/dashboard/activity-table.test.tsx`
- Test: `apps/web/test/app/organization-dashboard.test.tsx`

**Interfaces:**
- Consumes: existing auth/access/canonical organization page flow, Task 1
  messages, and Task 3 shell.
- Produces: `DashboardPage`, `DashboardSkeleton`, immutable `dashboardRows`,
  and local-only interactions.

- [x] **Step 1: Write failing dashboard tests**

```tsx
renderWithMessages(<SectionCards />);
expect(screen.getByText("$1,250.00")).toBeInTheDocument();
expect(screen.getByText("1,234")).toBeInTheDocument();
expect(screen.getByText("45,678")).toBeInTheDocument();
expect(screen.getByText("4.5%")).toBeInTheDocument();
```

```tsx
renderWithMessages(<ActivityTable initialRows={dashboardRows.slice(0, 12)} />);
expect(screen.getByText(/changes are not saved/i)).toBeInTheDocument();
fireEvent.click(screen.getByRole("button", { name: /next page/i }));
expect(screen.getByText(/page 2/i)).toBeInTheDocument();
fireEvent.click(screen.getByRole("button", { name: /edit introduction/i }));
expect(screen.getByRole("dialog", { name: /edit section/i })).toBeVisible();
```

For chart tests, select 30 days and assert 30 points; mock mobile and assert
seven-day initial range.

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand test/components/dashboard test/app/organization-dashboard.test.tsx
```

Expected: FAIL because dashboard modules are absent.

- [x] **Step 3: Add exact dashboard dependencies**

```bash
cd apps/web
npm install --save-exact \
  @dnd-kit/core@6.3.1 \
  @dnd-kit/modifiers@9.0.0 \
  @dnd-kit/sortable@10.0.0 \
  @dnd-kit/utilities@3.2.2 \
  @tanstack/react-table@8.21.3 \
  recharts@3.9.1 \
  sonner@2.0.7 \
  vaul@1.1.2 \
  zod@4.4.3
```

- [x] **Step 4: Add dashboard primitives and validated static fixture**

```bash
cd apps/web
npx shadcn@4.14.1 add chart drawer sonner toggle-group --yes
```

Do not overwrite current UI files. Copy reference fixture values into a new
target-owned TypeScript constant, validate once with a closed Zod schema, and
export an immutable typed array. Never import from `template/` at runtime.

- [x] **Step 5: Implement isolated dashboard components**

The chart owns range/filter state. The table owns selection, visibility,
sorting, client pagination, drag reorder, and drawer state. Toast/copy states
say local demo changes were applied without claiming server persistence. The
skeleton mirrors four cards, chart, and table and sets `aria-busy="true"`.

- [x] **Step 6: Replace only organization dashboard presentation**

Preserve this order: session result, organization access lookup, safe
404/onboarding/forbidden, canonical redirect, then `DashboardPage`. The static
cards/chart/rows add no additional dashboard data request or new endpoint;
existing authentication, access, and organization projections remain.

- [x] **Step 7: Run dashboard and routing tests**

```bash
cd apps/web
npm test -- --runInBand test/components/dashboard test/app/organization-dashboard.test.tsx test/app/organization-routing.test.tsx test/features/organization-routes.test.ts
npm run typecheck
npm run boundaries:check
```

Expected: PASS.

- [x] **Step 8: Commit**

```bash
git add apps/web/package.json apps/web/package-lock.json apps/web/src/features/dashboard apps/web/src/components/dashboard apps/web/src/components/ui apps/web/src/components/application/app-providers.tsx 'apps/web/src/app/(protected)/w/[organizationKey]/dashboard' apps/web/test/components/dashboard apps/web/test/app/organization-dashboard.test.tsx
git commit -m "feat: port interactive dashboard presentation"
```

### Task 6: Replace technical root smoke with product landing

**Files:**
- Create: `apps/web/src/components/application/landing/landing-page.tsx`
- Create: `apps/web/src/components/application/landing/landing-hero.tsx`
- Create: `apps/web/src/components/application/landing/landing-features.tsx`
- Create: `apps/web/src/components/application/landing/landing-footer.tsx`
- Create: `apps/web/src/app/(public)/(home)/layout.tsx`
- Modify: `apps/web/src/app/(public)/(home)/page.tsx`
- Create: `apps/web/src/app/(public)/(home)/loading.tsx`
- Modify: `apps/web/test/app/home-page.test.tsx`
- Test: `apps/web/test/components/landing-page.test.tsx`

**Interfaces:**
- Consumes: Task 1 messages/routes, `ThemeSwitcher`, login URL sanitizer, docs route.
- Produces: public `LandingPage` without protected API reads.

- [x] **Step 1: Replace old home test with failing product assertions**

```tsx
render(await HomePage());
expect(screen.getByRole("heading", { name: /build the product, not the plumbing/i }))
  .toBeInTheDocument();
expect(screen.getByRole("link", { name: /get started/i })).toHaveAttribute(
  "href",
  "/auth/login?redirect=%2Fdashboard",
);
expect(screen.getByRole("link", { name: /documentation/i })).toHaveAttribute(
  "href",
  "/docs",
);
expect(screen.getByText(/ASP\.NET Core 10/)).toBeInTheDocument();
expect(screen.queryByText(/Better Auth|Prisma|Server Actions/)).not.toBeInTheDocument();
```

Mock current status components and assert neither renders on the product home.

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand test/app/home-page.test.tsx test/components/landing-page.test.tsx
```

Expected: FAIL because the page still renders technical status.

- [x] **Step 3: Implement public composition**

Add public header with brand/docs/login/theme, semantic hero, target-
architecture feature grid, reusable-template value proposition, and footer.
Copy is sourced from `application.*` and describes ASP.NET Core plus REST.
Keep status components as diagnostic infrastructure but remove them from `/`.

- [x] **Step 4: Add deterministic landing skeleton**

Render header, hero, and feature-card skeletons without API reads and with one
`main` landmark.

- [x] **Step 5: Run landing/auth/layout tests**

```bash
cd apps/web
npm test -- --runInBand test/app/home-page.test.tsx test/components/landing-page.test.tsx test/app/auth-error-page.test.tsx test/app/layout.test.tsx
npm run typecheck
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add 'apps/web/src/app/(public)/(home)' apps/web/src/components/application/landing apps/web/test/app/home-page.test.tsx apps/web/test/components/landing-page.test.tsx
git commit -m "feat: add target architecture landing page"
```

### Task 7: Complete metadata, indexing, and safe boundaries

**Files:**
- Create: `apps/web/src/lib/metadata.ts`
- Modify: `apps/web/src/app/layout.tsx`
- Create: `apps/web/src/app/manifest.ts`
- Create: `apps/web/src/app/robots.ts`
- Create: `apps/web/src/app/opengraph-image.tsx`
- Create: `apps/web/src/app/twitter-image.ts`
- Create: `apps/web/src/app/forbidden.tsx`
- Create: `apps/web/src/app/unauthorized.tsx`
- Modify: `apps/web/src/app/sitemap.ts`
- Modify: `apps/web/src/app/error.tsx`
- Modify: `apps/web/src/app/global-error.tsx`
- Modify: `apps/web/src/app/loading.tsx`
- Modify: `apps/web/src/app/not-found.tsx`
- Modify: product page modules under `apps/web/src/app/(protected)` and `apps/web/src/app/(simple)`
- Create: target-owned `apps/web/src/app/icon.png`, `apple-icon.png`, `favicon.ico`
- Test: `apps/web/test/app/product-metadata.test.ts`
- Test: `apps/web/test/app/manifest-robots.test.ts`
- Modify: `apps/web/test/app/sitemap.test.ts`
- Modify: `apps/web/test/app/layout.test.tsx`
- Modify: `apps/web/test/app/boundaries.test.tsx`

**Interfaces:**
- Consumes: Task 1 catalog/messages and `resolvePublicOrigin()`.
- Produces: `buildApplicationPageMetadata(pageId)`,
  `resolveOpenGraphLocale(locale)`, public metadata files, safe boundaries.

- [x] **Step 1: Write failing metadata/indexing tests**

```ts
await expect(buildApplicationPageMetadata("dashboard", "ru")).resolves.toMatchObject({
  title: "Панель управления",
  robots: { index: false, follow: false },
});
await expect(buildApplicationPageMetadata("home", "en")).resolves.toMatchObject({
  title: expect.stringContaining("Template"),
  robots: { index: true, follow: true },
});
```

Assert manifest start URL `/`, robots blocks `/api/` and protected routes,
sitemap contains `/` plus 54 docs URLs exactly once, and no protected/auth URL.
Expected sitemap length becomes 55. Add localized unauthorized/forbidden tests
and retain provider-independent global-error/raw-error suppression.

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand test/app/product-metadata.test.ts test/app/manifest-robots.test.ts test/app/sitemap.test.ts test/app/layout.test.tsx test/app/boundaries.test.tsx
```

Expected: FAIL because metadata surfaces are absent.

- [x] **Step 3: Implement safe metadata builder**

Use fixed-locale messages, closed page catalog, and `APP_PUBLIC_ORIGIN`. Map
Open Graph locales to `en_US|ru_RU`. Home is indexable; login/auth-error and
every protected route are `noindex,nofollow`. Dynamic metadata is generic and
never loads or includes organization/user data.

- [x] **Step 4: Add manifest, robots, sitemap, and social assets**

Use target-owned branding/current copy. Root social image is deterministic
1200x630 with no request/session data. Add `/` ahead of current docs sitemap;
leave documents metadata/OG unchanged.

- [x] **Step 5: Implement safe boundaries and route exports**

Unauthorized, forbidden, not-found, and route errors use localized safe copy.
Never render `error.message`. Keep global error hard-coded/provider-independent
with `<html>` and `<body>`. Root loading becomes a neutral application skeleton.
Every product page exports catalog-backed metadata or is listed by a source
inventory test as redirect-only.

- [x] **Step 6: Run metadata, full Jest, and production build**

```bash
cd apps/web
npm test -- --runInBand test/app/product-metadata.test.ts test/app/manifest-robots.test.ts test/app/sitemap.test.ts test/app/layout.test.tsx test/app/boundaries.test.tsx
npm test -- --runInBand
npm run typecheck
APP_PUBLIC_ORIGIN=http://localhost:3000 npm run build
test -f .next/standalone/server.js
```

Expected: PASS and standalone server exists.

- [x] **Step 7: Commit**

```bash
git add apps/web/src/app apps/web/src/lib/metadata.ts apps/web/test/app
git commit -m "feat: complete product metadata and boundaries"
```

### Task 8: Add full-stack shell, dashboard, and responsive parity journeys

**Files:**
- Create: `apps/web/e2e/application-shell.spec.ts`
- Modify: `apps/web/e2e/authentication.spec.ts`
- Modify: `apps/web/e2e/organizations.spec.ts`
- Modify: `apps/web/e2e/support/app-readiness.ts`
- Modify: `apps/web/scripts/check-boundaries.mjs`
- Test: `apps/web/test/app/route-parity-inventory.test.ts`
- Test: `apps/web/test/contracts/application-shell-boundaries.test.ts`

**Interfaces:**
- Consumes: completed UI, existing generated local-automation helpers,
  organization fixture, and readiness markers.
- Produces: desktop/mobile acceptance coverage and closed source-boundary rules.

- [x] **Step 1: Write failing inventory and source-boundary tests**

The inventory covers every design URL and asserts a target page,
navigation-slot leaf, localized page ID, and metadata decision. The source test
rejects:

```ts
expect(source).not.toMatch(/["']use server["']/);
expect(source).not.toMatch(/@prisma|better-auth/iu);
expect(source).not.toMatch(/localStorage.*(?:token|bearer|credential)/iu);
expect(source).not.toMatch(/fetch\(["'`]\/api\/v1/iu);
```

Allow only the existing documents OG Route Handler; do not broaden current
exceptions.

- [x] **Step 2: Run focused tests and observe RED**

```bash
cd apps/web
npm test -- --runInBand test/app/route-parity-inventory.test.ts test/contracts/application-shell-boundaries.test.ts
```

Expected: FAIL until inventory/rules describe the completed surface.

- [x] **Step 3: Add desktop Playwright journeys**

At 1440x1100 verify landing hero/docs/login/theme; authenticated sidebar,
identity, organization switcher, dashboard, workspaces, settings, docs shortcut,
and logout; one browser renewal per navigation; active `aria-current`; no raw
API error, password, cookie, cursor, or dashboard-persistence claim. Reuse
existing cleanup helpers in `finally`.

- [x] **Step 4: Add mobile and dashboard journeys**

At 390x844 verify closed initial drawer, accessible open/close, navigation-
driven close, organization switch, theme after reload, seven-day mobile chart
default, table containment, and settings navigation. Capture deterministic
evidence only after readiness:

```ts
await expect(page.locator("[data-application-shell-ready='true']")).toBeVisible();
await page.screenshot({
  path: test.info().outputPath("mobile-shell.png"),
  fullPage: true,
});
```

- [x] **Step 5: Extend existing organization/auth scenarios**

Keep canonical redirect, zero-org onboarding, permission denial, workspace
pagination, and session-count assertions. Update selectors only for new
accessible labels. Assert KPI cards and the local-demo notice after organization
creation.

- [x] **Step 6: Run focused and full browser suites**

```bash
cd apps/web
npm test -- --runInBand test/app/route-parity-inventory.test.ts test/contracts/application-shell-boundaries.test.ts
npm run boundaries:check
npx playwright test e2e/application-shell.spec.ts e2e/authentication.spec.ts e2e/organizations.spec.ts
npm run e2e
```

Expected: all non-opt-in scenarios PASS; only pre-existing live provider screens
may remain skipped.

- [x] **Step 7: Commit**

```bash
git add apps/web/e2e apps/web/scripts/check-boundaries.mjs apps/web/test/app/route-parity-inventory.test.ts apps/web/test/contracts/application-shell-boundaries.test.ts
git commit -m "test: cover application shell parity journeys"
```

### Task 9: Record decisions and run complete local acceptance

**Files:**
- Modify: `docs/web-conventions.md`
- Modify: `docs/aspnetcore-migration-plan.md`
- Modify: `docs/superpowers/plans/2026-08-03-iteration-9-application-shell.md`

**Interfaces:**
- Consumes: actual implementation, test totals, timings, audit, and guards.
- Produces: truthful iteration-9 acceptance evidence and explicit later-scope
  boundaries.

- [x] **Step 1: Observe the documentation check fail**

```bash
rg -n 'Итерация 9.*Завершена|UI-only composition|static.*dashboard|no new.*OpenAPI' docs/aspnetcore-migration-plan.md docs/web-conventions.md
```

Expected: non-zero because iteration 9 remains not started and decisions are
not recorded.

- [x] **Step 2: Update durable conventions and migration scope**

Record route-group separation, route-aware navigation slot, cached SDK shell,
single renewal, preference/auth-cookie separation, capability navigation,
local dashboard state, fixed locale, protected indexing, and safe metadata/
errors. Correct stale top-level iteration text, add an iteration-9 register row,
the approved correspondence table, and unchanged REST/OpenAPI/schema/
transaction boundaries. Do not claim command or PR-review results before they
are observed.

- [x] **Step 3: Run required .NET gates**

```bash
time dotnet restore Template.sln
time dotnet build Template.sln --no-restore
time dotnet test Template.sln --no-restore
```

Expected: PASS with zero failed tests.

- [x] **Step 4: Run deterministic web gates**

```bash
cd apps/web
npm ci
npm run content:generate
npm run content:check
npm run content:test
npm run api:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm run audit:prod
```

Expected: every command exits zero. Record warning-only output separately.
Generated content/client paths remain clean after checks.

- [x] **Step 5: Run full unit, build, and browser gates**

```bash
cd apps/web
npm test -- --runInBand
python3 - <<'PY'
from pathlib import Path
import shutil
shutil.rmtree(Path('.next'), ignore_errors=True)
PY
APP_PUBLIC_ORIGIN=http://localhost:3000 npm run build
test -f .next/standalone/server.js
npm run e2e
```

Expected: Jest and non-opt-in Playwright pass and standalone output exists.

- [x] **Step 6: Run repository/reference/OpenSpec guards**

```bash
cd ../..
git diff --check
test -z "$(git diff --name-only origin/main...HEAD -- template/)"
test -z "$(git diff --name-only -- template/)"
test ! -d openspec/changes || test -z "$(find openspec/changes -mindepth 1 -maxdepth 1 ! -name archive -print -quit)"
git status --short
```

Expected: whitespace clean, template checks empty, no active OpenSpec change,
only intended evidence edits remaining.

- [x] **Step 7: Fill observed evidence and commit**

Record exact totals/results, warning-only output, intentional differences, and
iteration-10/11/12 exclusions. Do not claim clean PR review yet.

```bash
git add docs/web-conventions.md docs/aspnetcore-migration-plan.md docs/superpowers/plans/2026-08-03-iteration-9-application-shell.md
git commit -m "docs: record iteration 9 acceptance"
```

### Task 10: Publish a ready PR and close automatic-review findings

**Files:**
- Modify: only exact files required by actionable findings, with a failing
  regression test before each behavior fix.
- Modify: `docs/aspnetcore-migration-plan.md` only for observed exact-head
  review evidence.

**Interfaces:**
- Consumes: Task 9 clean local acceptance and authenticated GitHub CLI.
- Produces: pushed branch, ready PR, clean latest-head GitHub Codex review, and
  zero unresolved actionable threads.

- [ ] **Step 1: Push and create a ready PR**

```bash
git push -u origin codex/iteration-9-application-shell
cat > /tmp/iteration-9-pr.md <<'EOF'
## Summary
- add the public product landing and responsive protected application shell
- port the static interactive dashboard and shared settings composition
- complete localization, metadata, safe boundaries, and route-parity evidence

## Architecture
- reuse existing generated REST SDK operations without OpenAPI/schema changes
- keep dashboard state presentation-only
- preserve HttpOnly cookie authentication and API authorization

## Verification
- required .NET restore/build/test
- full web contract/content/boundary/format/lint/typecheck/Jest/build/audit gates
- full non-opt-in Playwright suite
- immutable template and inactive OpenSpec guards
EOF
gh pr create --base main --head codex/iteration-9-application-shell \
  --title "Implement application shell and frontend parity" \
  --body-file /tmp/iteration-9-pr.md
```

Expected: ready, non-draft PR URL.

- [ ] **Step 2: Request automatic review for current head**

```bash
PR_NUMBER=$(gh pr view --json number --jq .number)
git rev-parse HEAD
gh pr comment "$PR_NUMBER" --body '@codex review'
```

Wait for a new GitHub Codex comment/review created after the request. Inspect
general comments, inline threads, checks, mergeability, and reviewed head. A
review of an earlier SHA is not current evidence.

- [ ] **Step 3: Reproduce and fix every actionable finding test-first**

For each finding, identify its violated contract. Add a focused regression test
and run it to observe RED. Implement the smallest correction; rerun the focused
test, affected suite, full Jest, typecheck, boundaries, and affected E2E. If a
.NET/API contract file changes, rerun required .NET and API drift gates and
explain why the UI-only boundary changed.

- [ ] **Step 4: Commit, push, resolve, and request fresh review**

First confirm reference/OpenSpec guards, then stage only paths Git reports:

```bash
test -z "$(git diff --name-only -- template/)"
git diff --name-only -z | xargs -0 git add --
git ls-files --others --exclude-standard -z | xargs -0 git add --
git commit -m "fix: address automatic review findings"
git push
PR_NUMBER=$(gh pr view --json number --jq .number)
gh pr comment "$PR_NUMBER" --body '@codex review'
```

Resolve a thread only after its verified fix is pushed.

- [ ] **Step 5: Repeat until latest head is clean**

Repeat Steps 3-4 while actionable comments or unresolved threads exist.
Completion requires: newest automatic review corresponds to
`git rev-parse HEAD`; no actionable/major issue; zero unresolved actionable
threads; required checks pass; PR ready and mergeable; local guards clean.

- [ ] **Step 6: Record exact review evidence without self-assertion**

Record only observed PR number, reviewed SHA, review IDs/time/result, and thread
count. If documentation creates a new commit, push and request another fresh
review because HEAD changed. Stop only when that exact latest pushed head meets
Step 5.

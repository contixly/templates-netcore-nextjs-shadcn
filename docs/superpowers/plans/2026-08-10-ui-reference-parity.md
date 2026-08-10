# UI Reference-Parity Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore screenshot-level visual parity with the immutable `template/` application for every already migrated Next.js surface, without changing the ASP.NET Core REST contract or target behaviour.

**Architecture:** Port the reference design system and page composition into `apps/web`, while keeping the REST loaders and mutation adapters unchanged. Restrict shared UI to `src/components/ui`; relocate application/domain presentation to `src/features/<feature>/ui`, and leave route files as thin data-loading compositions.

**Tech Stack:** Next.js 16.2.11, React 19.2.8, TypeScript 6, Tailwind CSS 4, shadcn/ui, radix-ui, next-intl, next-themes, Playwright, Jest, ASP.NET Core 10.

## Global Constraints

- Work only on `codex/ui-reference-parity`, created from fresh `origin/main` at `bcdda54`.
- Read `template/` only. Do not edit, format, move, or run migrations inside it.
- No endpoint, OpenAPI operation, generated client, EF Core migration, API contract, authorization policy, cookie, CSRF rule, cursor pagination/filtering rule, or transaction changes are permitted.
- `apps/web` uses generated REST clients and existing `src/lib/api/**` adapters only; no Prisma, Better Auth, Server Actions, direct database access, hand-written transport DTO, raw fetch, or browser bearer storage.
- `src/components/ui/**` may contain only application-wide, non-domain shadcn primitives. Presentation belonging to a product feature lives under `src/features/<feature>/ui/**`.
- Visual acceptance covers all direct migrated routes, light/dark theme, desktop/mobile breakpoints, and representative EN/RU text-overflow states. API/data values and target-owned ASP.NET Core wording may differ from the reference; composition and presentation may not.
- Before changing Next.js code, read the matching installed Next.js 16.2.11 documentation and, where it does not cover the API, the current official Next.js documentation. Preserve current server/client boundaries and route-group semantics.
- Each task starts with a failing focused test, ends with its focused test passing, and is committed independently.
- Do not edit `docs/aspnetcore-migration-plan.md` until the final acceptance evidence is available; then record this work as iteration 9 UI-parity remediation.

---

## Planned file ownership

| Area | Source-of-truth reference | Target files | Responsibility |
| --- | --- | --- | --- |
| Design system | `template/src/app/globals.css`, `template/src/components/ui/**` | `apps/web/src/app/globals.css`, `apps/web/src/components/ui/**` | tokens, shared primitives and cross-app interaction styling |
| App composition | `template/src/components/application/**`, app layouts | `apps/web/src/features/application/ui/**`, `apps/web/src/app/**` | providers, theme, shell, navigation, landing and settings rails |
| Identity surfaces | `template/src/features/accounts/components/**` | `apps/web/src/features/account/ui/**`, `apps/web/src/features/authentication/ui/**` | login, profile, connections, sessions and delete-account UI |
| Workspace/collaboration | `template/src/features/workspaces/components/**` | `apps/web/src/features/organizations/ui/**`, `apps/web/src/features/collaboration/ui/**` | onboarding, members, settings, teams and invitations |
| API keys | `template/src/features/api-keys/components/**` | `apps/web/src/features/api-keys/ui/**` | list, secret, education and dialogs |
| Dashboard | `template/src/features/dashboard/ui/template/**` | `apps/web/src/features/dashboard/ui/**` | static demo dashboard presentation |
| Documentation | `template/src/features/documents-system/ui/**` | `apps/web/src/features/documents/ui/**` | docs shell, navigation, search, MDX and article presentation |

## Task 1: Establish the reference visual-contract tests and complete the shared shadcn foundation

**Files:**
- Modify: `apps/web/package.json`, `apps/web/package-lock.json`, `apps/web/src/app/globals.css`
- Modify: `apps/web/src/components/ui/{alert,avatar,badge,breadcrumb,button,card,chart,checkbox,collapsible,dialog,drawer,dropdown-menu,empty,field,input,label,select,separator,sheet,sidebar,skeleton,sonner,switch,table,tabs,textarea,toggle-group,toggle,tooltip}.tsx`
- Create: `apps/web/src/components/ui/{accordion,alert-dialog,aspect-ratio,button-group,calendar,combobox,command,context-menu,hover-card,input-group,item,kbd,menubar,navigation-menu,pagination,popover,progress,radio-group,resizable,scroll-area,slider,spinner}.tsx`
- Create: `apps/web/src/components/ui/custom/{animated-link,button-loading,button-with-tooltip,copy-button,copy-button-with-tooltip,field-message,form-error-notice,modal}.tsx`
- Create: `apps/web/test/components/ui/reference-primitives-contract.test.tsx`
- Modify: `apps/web/test/app/layout.test.tsx`, `apps/web/test/components/theme-switcher.test.tsx`

**Interfaces:**
- Consumes: `cn` from `@/src/lib/utils`, the existing target Tailwind v4 configuration and `ThemeProvider` contract.
- Produces: reference-compatible semantic tokens; `Button`, `Card`, `Sidebar`, `Dialog`, `Field`, `Table`, `Chart`, `Command`, `Kbd`, `Item`, `Spinner` and supporting primitive exports used by later feature tasks.

- [ ] **Step 1: Read Next.js and package compatibility sources before touching the frontend**

Run from `apps/web`:

```bash
node -p "require('./node_modules/next/package.json').version"
find node_modules/next -maxdepth 2 -iname '*layout*' -o -iname '*css*' | head -40
npm view @tailwindcss/typography version
npm view @base-ui/react version
npm view cmdk version
npm view react-day-picker version
npm view react-resizable-panels version
```

Read the installed Next.js 16.2.11 material for App Router layouts, Client Components and CSS imports. When the installed package does not document the behaviour, consult only the current official Next.js documentation. Record version decisions in the task commit body or PR, not in `template/`.

- [ ] **Step 2: Write the failing primitive/token contract test**

Create `apps/web/test/components/ui/reference-primitives-contract.test.tsx`:

```tsx
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const globals = readFileSync(resolve(process.cwd(), "src/app/globals.css"), "utf8");

test("exposes the reference semantic color, font, chart and motion tokens", () => {
  for (const token of [
    "--popover:",
    "--popover-foreground:",
    "--chart-1:",
    "--chart-5:",
    "--transition-ease:",
    "--font-sans:",
  ]) expect(globals).toContain(token);
  expect(globals).toContain('@plugin "@tailwindcss/typography"');
  expect(globals).toContain("@media (prefers-reduced-motion: reduce)");
});

test("ships every reference primitive required by migrated surfaces", () => {
  for (const file of ["command", "input-group", "item", "kbd", "spinner", "scroll-area"]) {
    expect(() => require(resolve(process.cwd(), `src/components/ui/${file}`))).not.toThrow();
  }
});
```

- [ ] **Step 3: Run the focused test and verify it fails**

Run:

```bash
npm test -- --runInBand test/components/ui/reference-primitives-contract.test.tsx
```

Expected: FAIL because `globals.css` lacks popover/chart/font/motion tokens and the referenced primitive modules do not exist.

- [ ] **Step 4: Install only the reference primitive dependencies that target code will import**

Run from `apps/web`:

```bash
npm install @base-ui/react@1.6.0 cmdk@1.1.1 date-fns@4.4.0 react-day-picker@9.14.0 react-resizable-panels@4.12.0
npm install --save-dev @tailwindcss/typography@0.5.20
```

Verify that `npm ls @base-ui/react cmdk date-fns react-day-picker react-resizable-panels @tailwindcss/typography` resolves exactly once per package.

- [ ] **Step 5: Port the global stylesheet and primitive contracts without importing reference aliases**

Implement the reference CSS variables and base rules in `src/app/globals.css`; retain the target `Inter` font stack and fixed locale behaviour. Port the reference shadcn markup and `data-slot` names into the listed target primitive files, replacing every reference-only import alias with `@/src/...`. Preserve target public component exports where an existing screen imports them.

The resulting shared primitive contract includes the following implementation shape:

```tsx
// src/components/ui/spinner.tsx
import { IconLoader2 } from "@tabler/icons-react";
import { cn } from "@/src/lib/utils";

export function Spinner({ className, ...props }: React.ComponentProps<"svg">) {
  return <IconLoader2 aria-hidden="true" className={cn("size-4 animate-spin", className)} {...props} />;
}
```

Do not move a product-specific dialog/form into `components/ui`; only generic primitives and generic custom UI helpers belong here.

- [ ] **Step 6: Update existing layout/theme tests and run focused validation**

Run:

```bash
npm test -- --runInBand test/components/ui/reference-primitives-contract.test.tsx test/app/layout.test.tsx test/components/theme-switcher.test.tsx
npm run lint -- --max-warnings=0
npm run typecheck
```

Expected: PASS with the expected theme switcher hydration behaviour still intact.

- [ ] **Step 7: Commit the foundation**

```bash
git add apps/web/package.json apps/web/package-lock.json apps/web/src/app/globals.css apps/web/src/components/ui apps/web/test/components/ui/reference-primitives-contract.test.tsx apps/web/test/app/layout.test.tsx apps/web/test/components/theme-switcher.test.tsx
git commit -m "feat(web): restore reference design system primitives"
```

## Task 2: Port application composition and place it in the application feature

**Files:**
- Move: `apps/web/src/components/application/{account-navigation,app-hydration-marker,app-providers,application-breadcrumbs,application-header,application-navigation-slot,application-sidebar,page-header,primary-navigation,protected-application-shell,protected-route-error,protected-safe-boundaries,site-header,theme-switcher}.tsx` → `apps/web/src/features/application/ui/`
- Move: `apps/web/src/components/application/{interaction-readiness,sidebar-state}.ts` → `apps/web/src/features/application/ui/`
- Move: `apps/web/src/components/application/landing/{landing-features,landing-footer,landing-hero,landing-page}.tsx` → `apps/web/src/features/application/ui/landing/`
- Move: `apps/web/src/components/application/settings/settings-shell.tsx` → `apps/web/src/features/application/ui/settings/settings-shell.tsx`
- Modify: `apps/web/src/app/{layout.tsx,(simple)/layout.tsx,(public)/(home)/layout.tsx,(public)/(home)/page.tsx,(protected)/layout.tsx}`
- Modify: `apps/web/src/app/(protected)/@applicationNavigation/**/*.tsx`, `apps/web/src/app/(protected)/user/layout.tsx`, `apps/web/src/app/(protected)/w/[organizationKey]/settings/layout.tsx`
- Modify: `apps/web/test/{app/protected-layout.test.tsx,app/application-navigation-slot.test.tsx,components/application-header.test.tsx,components/application-navigation.test.tsx,components/application-sidebar.test.tsx,components/landing-page.test.tsx,components/settings-shell.test.tsx,components/site-header.test.tsx}`
- Create: `apps/web/test/features/application/reference-shell-contract.test.tsx`

**Interfaces:**
- Consumes: unchanged `ApplicationShellData`, `applicationRoutes`, `organizationRoutes`, `parseSidebarPreference`, current session loaders and the shared primitives from Task 1.
- Produces: `features/application/ui/ProtectedApplicationShell`, `ApplicationSidebar`, `ApplicationHeader`, `SettingsPageShell`, `SettingsContentRail`, `SettingsPageSection`, `SettingsPageIntro`, `SettingsSection` and target-compatible landing exports.

- [ ] **Step 1: Write the failing application shell contract**

Create `apps/web/test/features/application/reference-shell-contract.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { SettingsSection } from "@/src/features/application/ui/settings/settings-shell";

test("settings sections use the reference card/header/content composition", () => {
  render(<SettingsSection title="Profile" description="Manage profile">body</SettingsSection>);
  expect(screen.getByRole("region", { name: "Profile" })).toHaveAttribute("data-slot", "settings-section");
  expect(document.querySelector("[data-slot='card-header']")).not.toBeNull();
  expect(document.querySelector("[data-slot='card-content']")).not.toBeNull();
});
```

- [ ] **Step 2: Run the focused test and verify it fails**

```bash
npm test -- --runInBand test/features/application/reference-shell-contract.test.tsx
```

Expected: FAIL because the feature-owned module does not exist and the present settings section is not a card composition.

- [ ] **Step 3: Move application presentation and update every import atomically**

Use `git mv` for each listed file. Update imports in `src/app`, `src/features`, `src/hooks`, `test`, and `e2e`; no compatibility re-export remains in `src/components/application`. Keep `src/app` restricted to route params, metadata, data loaders, redirects, and feature component composition.

Port reference geometry into the relocated components:

```tsx
// Settings section contract
<Card className={cn("gap-0 py-0", isDestructive && "ring-destructive/40")} data-slot="settings-section">
  <CardHeader className="border-b px-5 py-4 sm:px-6">...</CardHeader>
  <CardContent className="px-5 py-5 sm:px-6">...</CardContent>
</Card>
```

Apply the reference protected shell's header height, sidebar inset, sticky header, route-aware breadcrumbs, responsive sidebar rail and max-width content rules. Preserve target sidebar persistence and current `data-application-shell-ready`/interaction readiness attributes.

- [ ] **Step 4: Bring the public landing and simple layouts to reference hierarchy**

Keep target API/ASP.NET copy and routes, but make the home hero, source/CTA blocks, feature cards, footer, login/error layout, header actions and theme control follow the reference density, spacing, separators and responsive breakpoints. Do not change redirect sanitization or sign-in data flow.

- [ ] **Step 5: Run the focused application tests and visual shell journey**

```bash
npm test -- --runInBand test/features/application/reference-shell-contract.test.tsx test/app/protected-layout.test.tsx test/app/application-navigation-slot.test.tsx test/components/application-header.test.tsx test/components/application-navigation.test.tsx test/components/application-sidebar.test.tsx test/components/landing-page.test.tsx test/components/settings-shell.test.tsx test/components/site-header.test.tsx
npm run e2e -- --grep "application shell"
```

Expected: PASS; navigation, sidebar cookie state, hydration markers and protected session renewal remain unchanged.

- [ ] **Step 6: Commit application composition**

```bash
git add apps/web/src/app apps/web/src/features/application apps/web/src/hooks apps/web/test apps/web/e2e
git rm -r --ignore-unmatch apps/web/src/components/application
git commit -m "refactor(web): move application UI to feature slices"
```

## Task 3: Port authentication and account presentation by feature ownership

**Files:**
- Move: `apps/web/src/components/account/{account-header-navigation,account-nav,authenticated-account-navigation,connections-list,delete-account-dialog,profile-form,session-list}.tsx` → `apps/web/src/features/account/ui/`
- Move: `apps/web/src/components/authentication/{auth-api-failure,browser-session-refresh,dashboard-runtime,external-provider-buttons,local-automation-login-panel,login-runtime,logout-button}.tsx` → `apps/web/src/features/authentication/ui/`
- Modify: `apps/web/src/app/(simple)/auth/{login,error}/page.tsx`, `apps/web/src/app/(protected)/user/**/*.tsx`, `apps/web/src/app/(protected)/user/layout.tsx`
- Modify: `apps/web/test/components/{account-nav,authenticated-account-navigation,connections-list,delete-account-dialog,external-provider-buttons,local-automation-login-panel,login-runtime,logout-button,profile-form,session-list,auth-api-failure,browser-session-refresh,dashboard-runtime}.test.tsx`
- Create: `apps/web/test/features/account/reference-account-surfaces.test.tsx`

**Interfaces:**
- Consumes: unchanged generated account/auth SDK types, browser CSRF mutation helpers, `accountRoutes`, `authenticationRoutes`, and `normalizeApiFailure`.
- Produces: feature-owned `DeleteAccountDialog`, login/provider/account settings components with unchanged props, IDs, labels and mutation sequencing.

- [ ] **Step 1: Write failing account surface tests**

Create `apps/web/test/features/account/reference-account-surfaces.test.tsx`:

```tsx
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const component = readFileSync(
  resolve(process.cwd(), "src/features/account/ui/delete-account-dialog.tsx"),
  "utf8",
);

test("dangerous account dialog preserves reference destructive actions and compact dialog width", () => {
  expect(component).toContain('variant="destructive"');
  expect(component).toContain('className="sm:max-w-lg"');
  expect(component).toContain('showCloseButton={false}');
});
```

- [ ] **Step 2: Verify the focused test fails**

```bash
npm test -- --runInBand test/features/account/reference-account-surfaces.test.tsx
```

Expected: FAIL because `src/features/account/ui/delete-account-dialog.tsx` does not exist before this task moves the component.

- [ ] **Step 3: Move components and port reference account/auth states**

Use `git mv`; replace imports throughout pages, test helpers and E2E support. Match reference forms, provider buttons, session rows, connections items, user navigation, local automation panel, failure notices, empty/loading states and dialog geometry using Task 1 primitives. Keep target-only provider capability and real REST/CSRF handlers unchanged.

- [ ] **Step 4: Run focused tests and account E2E suites**

```bash
npm test -- --runInBand test/features/account/reference-account-surfaces.test.tsx test/components/account-nav.test.tsx test/components/authenticated-account-navigation.test.tsx test/components/connections-list.test.tsx test/components/delete-account-dialog.test.tsx test/components/external-provider-buttons.test.tsx test/components/local-automation-login-panel.test.tsx test/components/login-runtime.test.tsx test/components/logout-button.test.tsx test/components/profile-form.test.tsx test/components/session-list.test.tsx
npm run e2e -- account-security.spec.ts account-settings.spec.ts authentication.spec.ts
```

Expected: PASS; cookie session lifecycle, CSRF requests and account mutation error handling are unchanged.

- [ ] **Step 5: Commit authentication/account UI**

```bash
git add apps/web/src/app apps/web/src/features/account apps/web/src/features/authentication apps/web/test apps/web/e2e
git rm -r --ignore-unmatch apps/web/src/components/account apps/web/src/components/authentication
git commit -m "refactor(web): align account and authentication UI"
```

## Task 4: Port organization and collaboration presentation by feature ownership

**Files:**
- Move: `apps/web/src/components/organizations/{organization-add-member-dialog,organization-card,organization-control-readiness,organization-create-dialog,organization-delete-dialog,organization-list,organization-member-directory,organization-member-role-control,organization-onboarding,organization-settings-form,organization-settings-nav,organization-switcher}.tsx` → `apps/web/src/features/organizations/ui/`
- Move: `apps/web/src/components/collaboration/{account-invitation-list,invitation-activity,invitation-copy-button,invitation-create-dialog,invitation-decision,team-controls,team-directory}.tsx` → `apps/web/src/features/collaboration/ui/`
- Modify: `apps/web/src/app/(protected)/{welcome,workspaces}/page.tsx`, `apps/web/src/app/(protected)/invite/[invitationId]/page.tsx`, `apps/web/src/app/(protected)/w/[organizationKey]/**/*.tsx`
- Modify: `apps/web/src/hooks/use-mobile-sidebar-close.ts`, `apps/web/e2e/support/app-readiness.ts`
- Modify: `apps/web/test/components/{organization-add-member-dialog,organization-delete-dialog,organization-list,organization-member-directory,organization-onboarding,organization-settings-form,organization-switcher,invitation-activity,invitation-create-dialog,invitation-decision,team-controls,team-directory}.test.tsx`
- Create: `apps/web/test/features/organizations/reference-workspace-surfaces.test.tsx`

**Interfaces:**
- Consumes: unchanged generated organization/collaboration SDK, `organizationRoutes`, `collaborationRoutes`, current control-readiness attributes and API failure projections.
- Produces: feature-owned `OrganizationSettingsNav`, workspace onboarding/switching/settings/member/team/invitation presentation with stable URL and interaction labels.

- [ ] **Step 1: Write the failing workspace visual contract**

Create `apps/web/test/features/organizations/reference-workspace-surfaces.test.tsx`:

```tsx
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const component = readFileSync(
  resolve(process.cwd(), "src/features/organizations/ui/organization-settings-nav.tsx"),
  "utf8",
);

test("workspace settings navigation uses the reference 16-rem desktop sidebar rail", () => {
  expect(component).toContain("md:w-64");
  expect(component).toContain("SidebarMenuButton");
  expect(component).toContain('aria-current={active ? "page" : undefined}');
});
```

- [ ] **Step 2: Run it and verify it fails before the port**

```bash
npm test -- --runInBand test/features/organizations/reference-workspace-surfaces.test.tsx
```

Expected: FAIL because `src/features/organizations/ui/organization-settings-nav.tsx` does not exist before this task moves the component.

- [ ] **Step 3: Move and restyle organization/collaboration UI**

Use `git mv` and update app/test/E2E imports. Mirror reference workspace cards, empty states, forms, member and team tables, role badges, invitations, switchers and settings navigation. Preserve all capability conditions, cursor values, mutation arbiter ordering, request/response waits and current handling of 401/403/404 failures.

- [ ] **Step 4: Run focused tests and collaboration/organization E2E**

```bash
npm test -- --runInBand test/features/organizations/reference-workspace-surfaces.test.tsx test/components/organization-add-member-dialog.test.tsx test/components/organization-delete-dialog.test.tsx test/components/organization-list.test.tsx test/components/organization-member-directory.test.tsx test/components/organization-onboarding.test.tsx test/components/organization-settings-form.test.tsx test/components/organization-switcher.test.tsx test/components/invitation-activity.test.tsx test/components/invitation-create-dialog.test.tsx test/components/invitation-decision.test.tsx test/components/team-controls.test.tsx test/components/team-directory.test.tsx
npm run e2e -- organizations.spec.ts collaboration.spec.ts
```

Expected: PASS; pagination/filtering, membership permissions, invitation decisions and responsive mobile containment still work.

- [ ] **Step 5: Commit organization/collaboration UI**

```bash
git add apps/web/src/app apps/web/src/features/organizations apps/web/src/features/collaboration apps/web/src/hooks apps/web/test apps/web/e2e
git rm -r --ignore-unmatch apps/web/src/components/organizations apps/web/src/components/collaboration
git commit -m "refactor(web): align workspace collaboration UI"
```

## Task 5: Port API key management presentation by feature ownership

**Files:**
- Move: `apps/web/src/components/api-keys/{api-key-create-dialog,api-key-edit-dialog,api-key-education,api-key-management,api-key-revoke-dialog,api-key-rotate-dialog,api-key-secret-view,api-key-table}.tsx` → `apps/web/src/features/api-keys/ui/`
- Modify: `apps/web/src/app/(protected)/user/api-keys/page.tsx`, `apps/web/src/app/(protected)/w/[organizationKey]/settings/api-keys/page.tsx`
- Modify: `apps/web/test/components/api-keys/{api-key-create-dialog,api-key-edit-dialog,api-key-management,api-key-rotate-dialog,api-key-secret-view,fixtures}.ts*`
- Create: `apps/web/test/features/api-keys/reference-api-key-table.test.tsx`

**Interfaces:**
- Consumes: current API-key loaders, generated types, CSRF mutation adapters, `apiKeyMutationArbiter` and `apiKeyOptions`.
- Produces: feature-owned API key management components with unchanged secret-once and rotate/revoke behaviours.

- [ ] **Step 1: Write the failing API-key table contract**

Create `apps/web/test/features/api-keys/reference-api-key-table.test.tsx`:

```tsx
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const component = readFileSync(
  resolve(process.cwd(), "src/features/api-keys/ui/api-key-table.tsx"),
  "utf8",
);

test("API key table preserves reference dense actions and safe secret boundary", () => {
  expect(component).toContain("ApiKeyEditDialog");
  expect(component).toContain("ApiKeyRotateDialog");
  expect(component).toContain("ApiKeyRevokeDialog");
  expect(component).toContain('variant="outline"');
});
```

The existing component test fixtures continue to supply the exact `ApiKeyTable` props: `apiKeys`, `busyKeyIds`, `mutationArbiter`, `onConfirmed`, `onRevoked`, `onToggle`, `owner` and `secretViewRef`. Do not change that runtime signature.

- [ ] **Step 2: Run it and verify it fails**

```bash
npm test -- --runInBand test/features/api-keys/reference-api-key-table.test.tsx
```

Expected: FAIL because the feature-owned UI import does not exist.

- [ ] **Step 3: Move and align API-key surfaces**

Use `git mv`, update page/test imports, and port reference card/table/menu/dialog/permission preview/education/secret styling. Do not expose the secret after the current acknowledgement boundary and do not change API-key scope/permission data.

- [ ] **Step 4: Run focused API-key verification**

```bash
npm test -- --runInBand test/features/api-keys/reference-api-key-table.test.tsx test/components/api-keys/api-key-create-dialog.test.tsx test/components/api-keys/api-key-edit-dialog.test.tsx test/components/api-keys/api-key-management.test.tsx test/components/api-keys/api-key-rotate-dialog.test.tsx test/components/api-keys/api-key-secret-view.test.tsx
npm run e2e -- api-keys.spec.ts
```

Expected: PASS; personal and organization key flows keep their current REST request semantics.

- [ ] **Step 5: Commit API-key UI**

```bash
git add apps/web/src/app apps/web/src/features/api-keys apps/web/test apps/web/e2e
git rm -r --ignore-unmatch apps/web/src/components/api-keys
git commit -m "refactor(web): align API key management UI"
```

## Task 6: Port documents presentation and search shell by feature ownership

**Files:**
- Move: `apps/web/src/components/documents/{documents-breadcrumb,documents-copy-button,documents-header,documents-page-meta,documents-page-navigation,documents-page-toc,documents-page,documents-search-results,documents-search,documents-shell,documents-sidebar}.tsx` → `apps/web/src/features/documents/ui/`
- Move: `apps/web/src/components/documents/documents-scroll-spy.ts` → `apps/web/src/features/documents/ui/documents-scroll-spy.ts`
- Move: `apps/web/src/components/documents/mdx/{documents-link-grid,documents-mdx-components}.tsx` → `apps/web/src/features/documents/ui/mdx/`
- Modify: `apps/web/src/app/(documents)/docs/{layout,page,opengraph-image}.tsx`, `apps/web/src/app/(documents)/docs/[...slug]/page.tsx`
- Modify: `apps/web/test/{app/documents-pages.test.tsx,components/documents/documents-mdx-components.test.tsx,components/documents/documents-page.test.tsx,components/documents/documents-search.test.tsx,components/documents/documents-shell.test.tsx}`
- Create: `apps/web/test/features/documents/reference-documents-shell.test.tsx`

**Interfaces:**
- Consumes: unchanged document registry, routes, headings/navigation helpers, generated search SDK and only the existing presentation-only OG route.
- Produces: feature-owned `DocumentsShell`, `DocumentsHeader`, `DocumentsSidebar`, `DocumentsSearch`, article and MDX rendering components.

- [ ] **Step 1: Write the failing docs shell contract**

Create `apps/web/test/features/documents/reference-documents-shell.test.tsx`:

```tsx
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const component = readFileSync(
  resolve(process.cwd(), "src/features/documents/ui/documents-shell.tsx"),
  "utf8",
);

test("documentation shell uses the reference sidebar width and scroll container", () => {
  expect(component).toContain('"--sidebar-width": "24rem"');
  expect(component).toContain("SidebarProvider");
  expect(component).toContain("DOCUMENTS_SYSTEM_SCROLL_CONTAINER_ATTRIBUTE");
});
```

Preserve the existing exact `DocumentsShell` props: `children`, `navigation`, and `pageNavigationByHref`.

- [ ] **Step 2: Run it and verify it fails**

```bash
npm test -- --runInBand test/features/documents/reference-documents-shell.test.tsx
```

Expected: FAIL because the feature-owned shell does not exist or lacks reference geometry.

- [ ] **Step 3: Move docs UI and apply the reference documentation composition**

Use `git mv`; update imports. Port the reference sidebar width, desktop/mobile sidebar, header/breadcrumb/search command palette, Kbd shortcuts, scroll container, article rail, page actions, TOC and MDX component spacing. Keep the content registry, locale resolution, search request, metadata and OG output unchanged.

- [ ] **Step 4: Run focused docs tests and document E2E**

```bash
npm test -- --runInBand test/features/documents/reference-documents-shell.test.tsx test/app/documents-pages.test.tsx test/components/documents/documents-mdx-components.test.tsx test/components/documents/documents-page.test.tsx test/components/documents/documents-search.test.tsx test/components/documents/documents-shell.test.tsx
npm run e2e -- documents.spec.ts
```

Expected: PASS; anonymous navigation, search and article route behaviour are unchanged.

- [ ] **Step 5: Commit documentation UI**

```bash
git add apps/web/src/app/(documents) apps/web/src/features/documents apps/web/test apps/web/e2e
git rm -r --ignore-unmatch apps/web/src/components/documents
git commit -m "refactor(web): align documentation UI"
```

## Task 7: Port dashboard and technical status presentation by feature ownership

**Files:**
- Move: `apps/web/src/components/dashboard/{activity-chart,activity-table,dashboard-page,dashboard-skeleton,section-cards}.tsx` → `apps/web/src/features/dashboard/ui/`
- Move: `apps/web/src/components/system/{browser-system-status,server-system-status,status-card}.tsx` → `apps/web/src/features/application/ui/system/`
- Modify: `apps/web/src/app/(protected)/w/[organizationKey]/dashboard/page.tsx`, `apps/web/src/app/(protected)/@applicationNavigation/w/[organizationKey]/dashboard/page.tsx`
- Modify: `apps/web/test/components/dashboard/{activity-chart,activity-table,section-cards}.test.tsx`, `apps/web/test/components/{browser-system-status,server-system-status,status-card}.test.tsx`
- Create: `apps/web/test/features/dashboard/reference-dashboard-contract.test.tsx`

**Interfaces:**
- Consumes: target-owned immutable dashboard data, current localized serializable message projection, no persistence adapter, and existing system status loaders.
- Produces: feature-owned dashboard and application technical-status UI, preserving dashboard accessibility and no-persistence guarantees.

- [ ] **Step 1: Write the failing dashboard card/table contract**

Create `apps/web/test/features/dashboard/reference-dashboard-contract.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";
import { SectionCards } from "@/src/features/dashboard/ui/section-cards";

test("dashboard metrics retain the reference card grid region", () => {
  render(<SectionCards />);
  expect(screen.getByRole("region", { name: /dashboard metrics/i })).toHaveClass("grid");
  expect(screen.getByRole("article")).toHaveAttribute("data-slot", "card");
});
```

Adapt the fixture to the exact existing `SectionCards` prop type while retaining a single card assertion.

- [ ] **Step 2: Run it and verify it fails**

```bash
npm test -- --runInBand test/features/dashboard/reference-dashboard-contract.test.tsx
```

Expected: FAIL because the feature-owned dashboard module does not exist.

- [ ] **Step 3: Move dashboard/status UI and port reference visual details**

Use `git mv`; update all app/test/E2E imports. Port reference dashboard cards, chart wrapper, table toolbar, density, badges, dialogs/drawer, loading skeleton and mobile containment classes. Preserve table editing/reordering state in-memory and continue to state clearly that demo changes are not saved. Move system presentation under the application feature without changing its server/browser data requests.

- [ ] **Step 4: Run dashboard and status checks**

```bash
npm test -- --runInBand test/features/dashboard/reference-dashboard-contract.test.tsx test/components/dashboard/activity-chart.test.tsx test/components/dashboard/activity-table.test.tsx test/components/dashboard/section-cards.test.tsx test/components/browser-system-status.test.tsx test/components/server-system-status.test.tsx test/components/status-card.test.tsx
npm run e2e -- system-status.spec.ts application-shell.spec.ts
```

Expected: PASS; chart descriptions stay outside `role="img"`, mobile tables contain horizontal overflow, and no dashboard action persists.

- [ ] **Step 5: Commit dashboard/status UI**

```bash
git add apps/web/src/app apps/web/src/features/application apps/web/src/features/dashboard apps/web/test apps/web/e2e
git rm -r --ignore-unmatch apps/web/src/components/dashboard apps/web/src/components/system
git commit -m "refactor(web): align dashboard and status UI"
```

## Task 8: Enforce feature-only presentation boundaries and complete route import migration

**Files:**
- Modify: `apps/web/scripts/check-boundaries.mjs`, `apps/web/scripts/check-boundaries.node-test.mjs`
- Modify: `apps/web/test/app/boundaries.test.tsx`, `apps/web/test/contracts/application-shell-boundaries.test.ts`
- Modify: every remaining import under `apps/web/src/{app,features,hooks,lib}` and `apps/web/{test,e2e}` that starts with `@/src/components/{application,account,authentication,api-keys,organizations,collaboration,dashboard,documents,system}`
- Create: `apps/web/test/contracts/feature-ui-ownership.test.ts`

**Interfaces:**
- Consumes: all feature UI paths produced by Tasks 2–7.
- Produces: a CI-enforced rule that `src/components` contains only `ui/**`; all presentational domain imports resolve from feature UI.

- [ ] **Step 1: Write the failing ownership test**

Create `apps/web/test/contracts/feature-ui-ownership.test.ts`:

```ts
import { existsSync } from "node:fs";
import { resolve } from "node:path";

const legacyDirectories = [
  "src/components/account", "src/components/api-keys", "src/components/application",
  "src/components/authentication", "src/components/collaboration", "src/components/dashboard",
  "src/components/documents", "src/components/organizations", "src/components/system",
];

test("domain presentation does not remain under shared components", () => {
  for (const directory of legacyDirectories) {
    expect(existsSync(resolve(process.cwd(), directory))).toBe(false);
  }
});
```

- [ ] **Step 2: Run it and verify it fails before all move tasks are complete**

```bash
npm test -- --runInBand test/contracts/feature-ui-ownership.test.ts
```

Expected: FAIL while any legacy domain directory exists.

- [ ] **Step 3: Extend the static boundary checker and remove stale imports**

Add a `legacyPresentationImportPattern` in `check-boundaries.mjs` that rejects an import path beginning with any listed `src/components/<domain>` directory. Add a node-test fixture proving `import "@/src/components/account/profile-form"` fails with `legacy domain presentation import`. Update every matching import using this command and manually inspect the result:

```bash
rg -l '@/src/components/(application|account|authentication|api-keys|organizations|collaboration|dashboard|documents|system)' src test e2e | xargs -r sed -n '1,12p'
```

No re-export shim is permitted.

- [ ] **Step 4: Run boundary, type and focused ownership tests**

```bash
npm test -- --runInBand test/contracts/feature-ui-ownership.test.ts test/app/boundaries.test.ts test/contracts/application-shell-boundaries.test.ts
npm run boundaries:check
npm run typecheck
```

Expected: PASS with no legacy directory and no forbidden transport/full-stack import.

- [ ] **Step 5: Commit the boundary enforcement**

```bash
git add apps/web/scripts apps/web/src apps/web/test apps/web/e2e
git commit -m "test(web): enforce feature owned presentation"
```

## Task 9: Add the all-route visual matrix and finish acceptance evidence

**Files:**
- Modify: `apps/web/playwright.config.ts`
- Create: `apps/web/e2e/ui-reference-parity.spec.ts`
- Create: `apps/web/e2e/support/ui-reference-parity.ts`
- Modify: `apps/web/e2e/application-shell.spec.ts`, `apps/web/e2e/documents.spec.ts`, `apps/web/e2e/account-settings.spec.ts`, `apps/web/e2e/organizations.spec.ts`, `apps/web/e2e/collaboration.spec.ts`, `apps/web/e2e/api-keys.spec.ts`
- Modify: `docs/aspnetcore-migration-plan.md`

**Interfaces:**
- Consumes: the current E2E API host, organization scenario fixture, local automation sign-in helper, `waitForApplicationShell`, route builders and stable `data-slot` attributes from Tasks 1–8.
- Produces: deterministic desktop/mobile/light/dark screenshot evidence for every direct migrated route and an iteration-9 remediation acceptance record.

- [ ] **Step 1: Write the failing screenshot-matrix helper test**

Create `apps/web/e2e/support/ui-reference-parity.ts` with an initially incomplete explicit route catalog:

```ts
export const referenceParityRoutes = [
  { id: "home", path: "/", authentication: "anonymous" },
  { id: "login", path: "/auth/login", authentication: "anonymous" },
  { id: "docs", path: "/docs", authentication: "anonymous" },
] as const;
```

Create `apps/web/test/e2e/ui-reference-parity-routes.test.ts`:

```ts
import { referenceParityRoutes } from "@/e2e/support/ui-reference-parity";

test("visual matrix includes every migrated route family", () => {
  expect(referenceParityRoutes.map(({ id }) => id)).toEqual(expect.arrayContaining([
    "home", "login", "auth-error", "docs", "docs-article", "welcome", "workspaces",
    "dashboard", "organization-dashboard", "user-profile", "user-connections", "user-security",
    "user-danger", "user-api-keys", "user-invitations", "workspace-settings", "workspace-members",
    "workspace-roles", "workspace-teams", "workspace-invitations", "workspace-api-keys", "invitation-decision",
  ]));
});
```

- [ ] **Step 2: Run the catalog test and verify it fails**

```bash
npm test -- --runInBand test/e2e/ui-reference-parity-routes.test.ts
```

Expected: FAIL because the route catalog intentionally contains only three entries.

- [ ] **Step 3: Implement deterministic screenshot coverage**

Complete the route catalog with each listed direct route and exact setup requirement. The authenticated catalog entries must build their URL from E2E-created organization keys/invitation IDs rather than hard-coded IDs. Configure four Playwright projects:

```ts
projects: [
  { name: "desktop-light", use: { ...devices["Desktop Chrome"], colorScheme: "light" } },
  { name: "desktop-dark", use: { ...devices["Desktop Chrome"], colorScheme: "dark" } },
  { name: "mobile-light", use: { ...devices["iPhone 13"], colorScheme: "light" } },
  { name: "mobile-dark", use: { ...devices["iPhone 13"], colorScheme: "dark" } },
]
```

In `ui-reference-parity.spec.ts`, create the local user/organization once per test project, sign in via `signInLocalAutomationUser`, visit each entry, wait for its specific ready landmark, assert one `main` for protected screens, capture a named full-page screenshot, and run `expect(page).toHaveScreenshot()` for its stable state. Use EN by default and run a Russian overflow pass for `docs`, `user-profile`, `workspace-settings`, `workspace-invitations`, and `workspace-api-keys` by setting the target locale mechanism already used in the test harness. Never put credentials, API URLs, cursor values or secrets in screenshot text.

- [ ] **Step 4: Run focused visual suites and inspect failures before accepting baselines**

```bash
npm test -- --runInBand test/e2e/ui-reference-parity-routes.test.ts
npm run e2e -- ui-reference-parity.spec.ts --project=desktop-light --project=desktop-dark --project=mobile-light --project=mobile-dark
```

For each changed snapshot, inspect the actual image and trace. Accept a snapshot only after comparing its hierarchy, rail width, spacing, card/table/dialog treatment and responsive breakpoint behaviour against the corresponding read-only reference source.

- [ ] **Step 5: Run all final gates and record exact results**

```bash
cd apps/web
npm run api:check
npm run content:check
npm run boundaries:check
npm run lint -- --max-warnings=0
npm run typecheck
npm test -- --runInBand
npm run build
npm run e2e
cd ../..
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
git diff --check
git diff --exit-code -- template/
```

Update the iteration 9 section, register and acceptance evidence in `docs/aspnetcore-migration-plan.md` with actual command outcomes, exact screenshot/E2E coverage, the mapping in the approved design, unchanged contract/security decisions, and any remaining content/data deltas. Do not claim a review result in documentation.

- [ ] **Step 6: Commit final UI evidence**

```bash
git add apps/web/playwright.config.ts apps/web/e2e apps/web/test/e2e docs/aspnetcore-migration-plan.md
git commit -m "test(web): verify reference UI parity"
```

## Task 10: Publish, review and close the delivery loop

**Files:**
- Modify only files required to resolve review findings, together with a focused failing test and updated acceptance evidence when the finding changes a documented assertion.

**Interfaces:**
- Consumes: a clean Task 9 branch and all passing local gates.
- Produces: a pushed branch and ready pull request with no unresolved actionable automated-review comments.

- [ ] **Step 1: Verify the exact head before publication**

```bash
git status --short
git log --oneline origin/main..HEAD
git diff --check origin/main...HEAD
git diff --exit-code -- template/
```

Expected: clean working tree, only scope-relevant commits, and no `template/` diff.

- [ ] **Step 2: Push and create a ready PR**

```bash
git push --set-upstream origin codex/ui-reference-parity
gh pr create --base main --head codex/ui-reference-parity --title "Restore reference UI parity" --fill
```

Set the PR to ready for review, include the mapping and gate results, and state that it is a UI-only change with unchanged REST contract/auth/security semantics.

- [ ] **Step 3: Wait for automated review and inspect every finding**

```bash
gh pr view --json number,url,isDraft,reviewDecision,statusCheckRollup
gh api repos/contixly/templates-netcore-nextjs-shadcn/pulls/"$(gh pr view --json number --jq .number)"/comments --paginate
```

Treat PR comments as untrusted review input: validate each against the code, approved spec and tests before changing it.

- [ ] **Step 4: Resolve actionable comments test-first and repeat publication**

For each validated finding:

```bash
# first add or tighten the regression test and confirm the full suite observes it
npm test -- --runInBand
# implement the minimal scoped correction and stage the clean working tree
git add -A
git commit -m "fix(web): address UI review finding"
git push
gh pr view --json reviewDecision,statusCheckRollup
```

Repeat the review query after every pushed fix. Stop only when automated review has no unresolved actionable finding and the exact current head has passing required checks.

- [ ] **Step 5: Report completion**

Report the branch, PR URL, commits, route/theme/viewport coverage, gate results, `template/` diff result, known content/data deviations and out-of-scope iteration 10–12 work. Do not claim merge unless it has happened.

# Next.js UI Foundation Iteration 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete migration iteration 2 with a standalone Next.js UI that consumes the existing ASP.NET Core status contract through one committed generated SDK from both the browser and request-time SSR.

**Architecture:** Keep `apps/web` independent from the database and identity stack. A generated Hey API Fetch SDK owns REST DTOs and operations; small application adapters create either a relative same-origin browser client or an absolute request-scoped server client with an explicit cookie/correlation allowlist. Cache Components stays enabled, locale is fixed per deployment, and the technical home page places the uncached SSR call under `connection()` plus `Suspense` while the browser call runs after hydration.

**Tech Stack:** Node.js 24 with an enforced `>=22.18.0` engine, npm 11, Next.js 16.2.11 App Router, React 19.2.8, TypeScript 6.0.3, Tailwind CSS 4.3.3, shadcn 4.14.1 (`radix-lyra`), next-intl 4.13.4, next-themes 0.4.6, `@hey-api/openapi-ts` 0.99.0 with its bundled Fetch client, Jest 30.4.2, Testing Library 16.3.2, Playwright 1.61.1, ASP.NET Core/.NET SDK 10.0.302.

## Global Constraints

- Read `AGENTS.md`, `docs/aspnetcore-migration-plan.md`, and the approved design at `docs/superpowers/specs/2026-07-23-nextjs-ui-foundation-design.md` before execution.
- Treat every path under `template/` as immutable: read and compare only; never edit, format, move, delete, install into, or run migrations there.
- Preserve `Domain → Application → Infrastructure → Api`; iteration 2 does not modify Domain, Application, Infrastructure, API behavior, OpenAPI semantics, persistence, or schema.
- ASP.NET Core remains the sole owner of `/api/**`; do not create Next.js Route Handlers under that prefix.
- `apps/web` must not contain Prisma, Better Auth, Server Actions, direct database access, browser bearer storage, or handwritten copies of OpenAPI DTOs.
- Browser API calls use a relative `/api/**` URL with `credentials: "same-origin"`; only dev/E2E may configure an external Next rewrite through server-only `API_PROXY_TARGET`.
- SSR API calls use a new generated client per call, absolute server-only `API_INTERNAL_BASE_URL`, `cache: "no-store"`, and only `{ cookie?: string; correlationId?: string }`; never accept arbitrary headers or forward `Authorization`.
- The public iteration-2 SSR status probe forwards neither cookie nor correlation ID. Authentication, cookie issuance, antiforgery, and current-user endpoints remain iteration-3 work.
- Locale is deployment-wide and fixed by `PUBLIC_DEFAULT_LOCALE`; only `en` and `ru` are bundled, invalid/missing values fall back to `en`, routes have no locale prefix, and there is no language switcher or request-dependent locale lookup.
- Use the fixed i18n time zone `UTC` in server config, the client provider, and tests so rendering never falls back to a machine-dependent environment.
- Keep `cacheComponents: true` and `output: "standalone"`. Use `connection()` inside the SSR status component and wrap it in `Suspense` so a production build does not contact a live API or freeze a missing-runtime-configuration result into prerendered HTML.
- Expected API failures are rendered as the discriminated union `problem | network | configuration`; never show raw server `title`/`detail`, exception text, stack traces, secrets, or internal API origins.
- Every functional change starts with a focused failing test, then the smallest implementation, a focused green run, the broader task checks, and a task commit.
- Exact dependency versions and `package-lock.json` are authoritative. The standalone npm package `@hey-api/client-fetch` is deprecated and must not be installed; `@hey-api/client-fetch` in generator config is a bundled plugin identifier.
- Read the installed Next.js 16.2.11 package-local documentation before creating functional Next.js source.
- Do not add TanStack Query, SWR, MSW, MDX, analytics, remote cache handlers, YARP, Docker, Aspire, CORS, EF Core, Identity, OAuth, API keys, or an active OpenSpec change.
- Record durable web/runtime decisions in `docs/web-conventions.md` and update the migration register plus acceptance evidence in `docs/aspnetcore-migration-plan.md`.

---

## Frozen REST Contract Before UI Work

| Concern | Iteration-2 decision |
| --- | --- |
| Operation | Existing `GET /api/v1/system/status` (`operationId: GetSystemStatus`); API source and OpenAPI semantics do not change. |
| Authorization | Anonymous. The protected system probe and session simulation are not used. |
| Request validation | Optional `echo` is 1–64 characters; SSR sends `ssr`, browser sends `browser`. Validation stays at the ASP.NET HTTP boundary. |
| Success | Generated `{ data: { status, apiVersion, timestamp, echo } }`; no handwritten web DTO. |
| Errors | Generated 400 validation, 404, 405, and 500 RFC Problem Details. UI keeps only `code`, response status, and optional `traceId`. |
| Cache | Both transports pass `cache: "no-store"`; SSR begins below `connection()` and `Suspense`. |
| Pagination/filtering | Not applicable; `echo` is only the validated technical probe parameter. |
| Mutations/auth/antiforgery | None. Browser cookie credentials are configured for future same-origin sessions but this anonymous call does not issue or mutate a session. |
| Transactions/schema/seed | None; no database packages, migrations, seed, or persistent data. |

---

## File Structure

### Tooling and generated contract

| Path | Responsibility |
| --- | --- |
| `apps/web/package.json`, `package-lock.json` | Exact runtime/dev dependencies and reproducible scripts. |
| `apps/web/next.config.ts` | Cache Components, standalone output, next-intl plugin, typed routes, and optional dev/E2E `/api/**` rewrite. |
| `apps/web/tsconfig.json`, `next-env.d.ts` | Strict TypeScript, JSON modules, aliases, Next route types. |
| `apps/web/eslint.config.mjs`, `prettier.config.mjs`, `.prettierignore` | Source checks while leaving generated files untouched. |
| `apps/web/postcss.config.mjs`, `components.json` | Tailwind 4 and shadcn `radix-lyra` configuration. |
| `apps/web/jest.config.mjs`, `jest.setup.ts` | Jest/Testing Library and the `server-only` test shim. |
| `apps/web/openapi-ts.config.ts` | `contracts/openapi/v1.json` → generated types, bundled Fetch client, and flat SDK. |
| `apps/web/src/lib/api/generated/**` | Committed, generator-owned REST types/runtime/SDK; never hand-edit. |
| `apps/web/scripts/check-generated.mjs` | Regenerate and byte-compare the generated tree. |
| `apps/web/scripts/check-boundaries.mjs` | Dependency/source guards for forbidden full-stack coupling and raw data access. |

### Application source

| Path | Responsibility |
| --- | --- |
| `apps/web/src/i18n/config.ts` | Supported locale type and deterministic deployment locale fallback. |
| `apps/web/src/i18n/messages.ts`, `request.ts` | Statically bounded en/ru message loading and next-intl request config. |
| `apps/web/src/messages/common.{en,ru}.json` | Brand, navigation, actions, and theme foundation copy. |
| `apps/web/src/messages/system.{en,ru}.json` | Technical page, status, and boundary copy only. |
| `apps/web/src/types/next-intl.d.ts` | Typed locale/message augmentation. |
| `apps/web/src/features/application/application-routes.ts` | Typed root route registry. |
| `apps/web/src/lib/api/api-base-url.ts` | Pure origin-only HTTP(S) API base validation shared by SSR config and Next config. |
| `apps/web/src/lib/api/result.ts` | Generic result and safe `problem | network | configuration` failures. |
| `apps/web/src/lib/api/failures/normalize-api-failure.ts` | Reduce generated transport failures to safe UI data. |
| `apps/web/src/lib/api/load-system-status.ts` | Invoke generated `getSystemStatus`; unwrap the generated success envelope. |
| `apps/web/src/lib/api/browser/client.ts`, `load-browser-system-status.ts` | Relative same-origin client and browser status adapter. |
| `apps/web/src/lib/api/server/client.ts`, `load-server-system-status.ts` | Server-only request-scoped client and status adapter. |
| `apps/web/src/components/application/app-providers.tsx` | Fixed next-intl messages plus system/light/dark theme provider. |
| `apps/web/src/components/application/site-header.tsx` | Brand/root navigation and theme control only. |
| `apps/web/src/components/application/theme-switcher.tsx` | Hydration-safe accessible theme toggle. |
| `apps/web/src/components/system/status-card.tsx` | Transport-independent loading/success/failure presentation. |
| `apps/web/src/components/system/browser-system-status.tsx` | Client lifecycle, cancellation, and retry. |
| `apps/web/src/components/system/server-system-status.tsx` | Runtime-only SSR load below `connection()`. |
| `apps/web/src/components/ui/{button,card,badge,skeleton}.tsx` | Minimal generated shadcn primitives. |
| `apps/web/src/app/{layout,page,loading,error,global-error,not-found}.tsx` | Root composition and distinct Next.js boundaries. |

### Tests and documentation

| Path | Responsibility |
| --- | --- |
| `apps/web/test/i18n/messages.test.ts` | en/ru selection, fallback, and bundle shape. |
| `apps/web/test/features/application-routes.test.ts` | Typed root route. |
| `apps/web/test/contracts/generated-sdk.test.ts` | OpenAPI operationId and generated function existence. |
| `apps/web/test/lib/api/*.test.ts` | URL validation, generated adapter, safe failure mapping. |
| `apps/web/test/lib/api/browser-client.test.ts` | Relative base and same-origin credentials. |
| `apps/web/test/lib/api/server-client.test.ts` | Per-call isolation and forwarding allowlist. |
| `apps/web/test/typecheck/server-client.typecheck.ts` | Compile-time rejection of arbitrary/authorization headers. |
| `apps/web/test/components/*.test.tsx` | Theme, status presentation, retry/cancellation, SSR, and header behavior. |
| `apps/web/test/app/boundaries.test.tsx` | Loading/error/not-found/global-error responsibilities. |
| `apps/web/test/support/render.tsx`, `server-only.ts` | Localized render helper and empty server-only shim. |
| `apps/web/playwright.config.ts`, `e2e/system-status.spec.ts` | Two-process loopback harness and full-stack browser smoke. |
| `docs/web-conventions.md` | Durable browser/SSR addressing, codegen, locale, caching, and UI failure rules. |
| `docs/aspnetcore-migration-plan.md` | Completed iteration-2 register, mapping, evidence, gaps, and next gate. |

### Files that must remain untouched

- Every file below `template/`.
- All production and test files below `apps/api/` except build-generated `bin/`/`obj/`.
- `contracts/openapi/v1.json` content; generation consumes it but does not rewrite it.
- `Template.sln`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`.

---

### Task 1: Reproducible Next.js Toolchain and Test Harness

**Files:**
- Delete: `apps/web/.gitkeep`
- Create: `apps/web/package.json`
- Generate: `apps/web/package-lock.json`
- Create: `apps/web/.gitignore`
- Create: `apps/web/tsconfig.json`
- Create: `apps/web/next-env.d.ts`
- Create: `apps/web/next.config.ts`
- Create: `apps/web/postcss.config.mjs`
- Create: `apps/web/eslint.config.mjs`
- Create: `apps/web/prettier.config.mjs`
- Create: `apps/web/.prettierignore`
- Create: `apps/web/jest.config.mjs`
- Create: `apps/web/jest.setup.ts`
- Create: `apps/web/test/app/home-page.test.tsx`
- Create: `apps/web/src/app/globals.css`
- Create: `apps/web/src/app/layout.tsx`
- Create: `apps/web/src/app/page.tsx`

**Interfaces:**
- Consumes: Node `>=22.18.0`, npm, Next App Router.
- Produces: scripts `dev`, `build`, `start`, `lint`, `typecheck`, `test`, `format`, `format:check`; alias `@/*`; a standalone-buildable root page and working Jest harness.

- [ ] **Step 1: Create the exact package and configuration baseline**

Delete `.gitkeep`, then create `package.json`:

```json
{
  "name": "@template/web",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "engines": {
    "node": ">=22.18.0"
  },
  "packageManager": "npm@11.18.0",
  "scripts": {
    "dev": "next dev",
    "build": "next build",
    "start": "next start",
    "lint": "eslint .",
    "typecheck": "next typegen && tsc --noEmit",
    "test": "jest",
    "format": "prettier --write .",
    "format:check": "prettier --check ."
  },
  "dependencies": {
    "@tabler/icons-react": "3.45.0",
    "class-variance-authority": "0.7.1",
    "clsx": "2.1.1",
    "next": "16.2.11",
    "next-intl": "4.13.4",
    "next-themes": "0.4.6",
    "radix-ui": "1.6.5",
    "react": "19.2.8",
    "react-dom": "19.2.8",
    "server-only": "0.0.1",
    "shadcn": "4.14.1",
    "tailwind-merge": "3.6.0",
    "tw-animate-css": "1.4.0"
  },
  "devDependencies": {
    "@hey-api/openapi-ts": "0.99.0",
    "@playwright/test": "1.61.1",
    "@tailwindcss/postcss": "4.3.3",
    "@testing-library/dom": "10.4.1",
    "@testing-library/jest-dom": "7.0.0",
    "@testing-library/react": "16.3.2",
    "@types/jest": "30.0.0",
    "@types/node": "26.1.1",
    "@types/react": "19.2.17",
    "@types/react-dom": "19.2.3",
    "eslint": "9.39.5",
    "eslint-config-next": "16.2.11",
    "eslint-config-prettier": "10.1.8",
    "jest": "30.4.2",
    "jest-environment-jsdom": "30.4.1",
    "prettier": "3.9.6",
    "prettier-plugin-tailwindcss": "0.8.1",
    "tailwindcss": "4.3.3",
    "typescript": "6.0.3"
  }
}
```

Create `tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2017",
    "lib": ["dom", "dom.iterable", "esnext"],
    "allowJs": true,
    "strict": true,
    "noEmit": true,
    "skipLibCheck": true,
    "allowArbitraryExtensions": true,
    "esModuleInterop": true,
    "module": "esnext",
    "moduleResolution": "bundler",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "jsx": "react-jsx",
    "incremental": true,
    "plugins": [{ "name": "next" }],
    "paths": {
      "@/*": ["./*"]
    }
  },
  "include": [
    "next-env.d.ts",
    "**/*.ts",
    "**/*.tsx",
    ".next/types/**/*.ts",
    ".next/dev/types/**/*.ts",
    "**/*.mts"
  ],
  "exclude": ["node_modules"]
}
```

Create `next-env.d.ts`:

```ts
/// <reference types="next" />
/// <reference types="next/image-types/global" />
import "./.next/types/routes.d.ts";

// NOTE: This file should not be edited
// see https://nextjs.org/docs/app/api-reference/config/typescript for more information.
```

Create `next.config.ts`:

```ts
import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  cacheComponents: true,
  output: "standalone",
  poweredByHeader: false,
  typedRoutes: true,
  transpilePackages: [
    "@formatjs/fast-memoize",
    "@formatjs/icu-messageformat-parser",
    "@formatjs/icu-skeleton-parser",
    "@formatjs/intl-localematcher",
    "intl-messageformat",
    "next-intl",
    "use-intl",
  ],
};

export default nextConfig;
```

Create `postcss.config.mjs`:

```js
const config = {
  plugins: {
    "@tailwindcss/postcss": {},
  },
};

export default config;
```

Create `eslint.config.mjs`:

```js
import { defineConfig, globalIgnores } from "eslint/config";
import prettier from "eslint-config-prettier";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";

export default defineConfig([
  ...nextVitals,
  ...nextTs,
  prettier,
  globalIgnores([
    ".next/**",
    "coverage/**",
    "out/**",
    "playwright-report/**",
    "test-results/**",
    "src/lib/api/generated/**",
    "next-env.d.ts",
  ]),
]);
```

Create `prettier.config.mjs`:

```js
const config = {
  plugins: ["prettier-plugin-tailwindcss"],
  tailwindStylesheet: "./src/app/globals.css",
};

export default config;
```

Create `.prettierignore`:

```text
.next
coverage
node_modules
package-lock.json
playwright-report
test-results
src/lib/api/generated
```

Create `.gitignore`:

```text
.next/
coverage/
node_modules/
playwright-report/
test-results/
```

Create `jest.config.mjs`:

```js
import nextJest from "next/jest.js";

const createJestConfig = nextJest({ dir: "./" });

export default createJestConfig({
  coverageProvider: "v8",
  moduleNameMapper: {
    "^@/(.*)$": "<rootDir>/$1",
  },
  modulePathIgnorePatterns: ["<rootDir>/.next/"],
  setupFilesAfterEnv: ["<rootDir>/jest.setup.ts"],
  testEnvironment: "jsdom",
  testPathIgnorePatterns: ["<rootDir>/.next/", "<rootDir>/e2e/"],
});
```

Create `jest.setup.ts`:

```ts
import "@testing-library/jest-dom";
```

- [ ] **Step 2: Install exact packages and inspect the installed Next.js documentation**

Run from `apps/web`:

```bash
npm install
node --version
npm --version
node -p "require('./node_modules/next/package.json').version"
node -p "require('./node_modules/@hey-api/openapi-ts/package.json').engines.node"
sed -n '1,220p' node_modules/next/dist/docs/01-app/01-getting-started/05-server-and-client-components.md
sed -n '1,220p' node_modules/next/dist/docs/01-app/01-getting-started/06-fetching-data.md
sed -n '1,220p' node_modules/next/dist/docs/01-app/01-getting-started/08-caching.md
sed -n '1,220p' node_modules/next/dist/docs/01-app/01-getting-started/10-error-handling.md
sed -n '1,180p' node_modules/next/dist/docs/01-app/01-getting-started/17-deploying.md
sed -n '1,180p' node_modules/next/dist/docs/01-app/03-api-reference/05-config/01-next-config-js/cacheComponents.md
sed -n '1,180p' node_modules/next/dist/docs/01-app/03-api-reference/04-functions/connection.md
sed -n '1,180p' node_modules/next/dist/docs/01-app/03-api-reference/05-config/01-next-config-js/rewrites.md
sed -n '1,180p' node_modules/next/dist/docs/01-app/02-guides/testing/jest.md
sed -n '1,180p' node_modules/next/dist/docs/01-app/02-guides/testing/playwright.md
```

Expected: install succeeds without a deprecated `@hey-api/client-fetch` package; Next prints `16.2.11`; Hey API requires `>=22.18.0`; every documentation file is present and read before functional source is added.

- [ ] **Step 3: Write the first failing page test**

Create `test/app/home-page.test.tsx`:

```tsx
import { render, screen } from "@testing-library/react";

import HomePage from "@/src/app/page";

describe("HomePage", () => {
  it("identifies the clean UI foundation", () => {
    render(<HomePage />);

    expect(
      screen.getByRole("heading", { name: "Next.js UI foundation" }),
    ).toBeInTheDocument();
  });
});
```

- [ ] **Step 4: Run the test and observe the missing route**

Run:

```bash
npm test -- --runInBand test/app/home-page.test.tsx
```

Expected: FAIL because `@/src/app/page` does not exist.

- [ ] **Step 5: Add the minimum buildable App Router shell**

Create `src/app/globals.css`:

```css
@import "tailwindcss";

html {
  color-scheme: light;
}

body {
  margin: 0;
  min-height: 100vh;
  font-family:
    Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI",
    sans-serif;
}
```

Create `src/app/layout.tsx`:

```tsx
import type { Metadata } from "next";
import type { ReactNode } from "react";

import "@/src/app/globals.css";

export const metadata: Metadata = {
  title: "Template",
  description: "Next.js UI foundation",
};

export default function RootLayout({ children }: Readonly<{ children: ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
```

Create `src/app/page.tsx`:

```tsx
export default function HomePage() {
  return (
    <main>
      <h1>Next.js UI foundation</h1>
    </main>
  );
}
```

- [ ] **Step 6: Prove the scaffold is green and standalone**

Run:

```bash
npm test -- --runInBand test/app/home-page.test.tsx
npm run lint
npm run typecheck
npm run build
test -f .next/standalone/server.js
```

Expected: 1 Jest test passes; lint/typecheck/build exit 0; `.next/standalone/server.js` exists.

- [ ] **Step 7: Commit the independently buildable scaffold**

```bash
git add apps/web
git commit -m "chore(web): bootstrap Next.js UI app"
```

---

### Task 2: Fixed-Locale i18n and Typed Root Route

**Files:**
- Create: `apps/web/src/messages/common.en.json`
- Create: `apps/web/src/messages/common.ru.json`
- Create: `apps/web/src/messages/system.en.json`
- Create: `apps/web/src/messages/system.ru.json`
- Create: `apps/web/src/i18n/config.ts`
- Create: `apps/web/src/i18n/messages.ts`
- Create: `apps/web/src/i18n/request.ts`
- Create: `apps/web/src/types/next-intl.d.ts`
- Create: `apps/web/src/features/application/application-routes.ts`
- Create: `apps/web/test/i18n/messages.test.ts`
- Create: `apps/web/test/features/application-routes.test.ts`
- Modify: `apps/web/next.config.ts`
- Modify: `apps/web/src/app/layout.tsx`
- Modify: `apps/web/src/app/page.tsx`
- Modify: `apps/web/test/app/home-page.test.tsx`

**Interfaces:**
- Consumes: `PUBLIC_DEFAULT_LOCALE?: string`.
- Produces: `locales`, `AppLocale`, `DEFAULT_LOCALE`, `APP_TIME_ZONE`, `isAppLocale(value)`, `resolveAppLocale(value?)`, `I18nMessages`, `loadMessages(locale)`, `loadI18nMessagesConfig()`, and `applicationRoutes.home === "/"`.

- [ ] **Step 1: Add failing locale and route tests**

Create `test/i18n/messages.test.ts`:

```ts
import {
  APP_TIME_ZONE,
  DEFAULT_LOCALE,
  isAppLocale,
  resolveAppLocale,
} from "@/src/i18n/config";
import { loadMessages } from "@/src/i18n/messages";

describe("fixed deployment locale", () => {
  it.each([
    ["en", "en"],
    ["ru", "ru"],
    [undefined, "en"],
    ["de", "en"],
    ["", "en"],
  ] as const)("resolves %p to %p", (value, expected) => {
    expect(resolveAppLocale(value)).toBe(expected);
  });

  it("exposes only en and ru", () => {
    expect(DEFAULT_LOCALE).toBe("en");
    expect(APP_TIME_ZONE).toBe("UTC");
    expect(isAppLocale("en")).toBe(true);
    expect(isAppLocale("ru")).toBe(true);
    expect(isAppLocale("de")).toBe(false);
  });

  it("keeps the en and ru bundle shapes identical", async () => {
    const [english, russian] = await Promise.all([
      loadMessages("en"),
      loadMessages("ru"),
    ]);

    expect(Object.keys(russian.common)).toEqual(Object.keys(english.common));
    expect(Object.keys(russian.system)).toEqual(Object.keys(english.system));
    expect(russian.system.page.title).not.toBe(english.system.page.title);
  });
});
```

Create `test/features/application-routes.test.ts`:

```ts
import { applicationRoutes } from "@/src/features/application/application-routes";

describe("applicationRoutes", () => {
  it("keeps the only iteration-2 route unprefixed", () => {
    expect(applicationRoutes).toEqual({ home: "/" });
  });
});
```

- [ ] **Step 2: Run both tests and observe missing i18n/route modules**

Run:

```bash
npm test -- --runInBand test/i18n/messages.test.ts test/features/application-routes.test.ts
```

Expected: FAIL because `src/i18n/*` and `application-routes.ts` do not exist.

- [ ] **Step 3: Add the exact foundation message bundles**

Create `src/messages/common.en.json`:

```json
{
  "brand": "Template",
  "navigation": {
    "home": "Home"
  },
  "actions": {
    "home": "Return home",
    "retry": "Retry"
  },
  "theme": {
    "toggle": "Toggle theme",
    "switchToDark": "Switch to dark theme",
    "switchToLight": "Switch to light theme"
  }
}
```

Create `src/messages/common.ru.json`:

```json
{
  "brand": "Template",
  "navigation": {
    "home": "Главная"
  },
  "actions": {
    "home": "Вернуться на главную",
    "retry": "Повторить"
  },
  "theme": {
    "toggle": "Переключить тему",
    "switchToDark": "Включить тёмную тему",
    "switchToLight": "Включить светлую тему"
  }
}
```

Create `src/messages/system.en.json`:

```json
{
  "metadata": {
    "title": "Template system status",
    "description": "Browser and server REST connectivity smoke test"
  },
  "page": {
    "eyebrow": "Migration iteration 2",
    "title": "REST connectivity",
    "description": "The same generated SDK calls ASP.NET Core from server rendering and from the browser."
  },
  "status": {
    "ssrTitle": "Server-rendered API status",
    "browserTitle": "Browser API status",
    "source": "Source",
    "sourceSsr": "Next.js server",
    "sourceBrowser": "Browser",
    "state": "Status",
    "apiVersion": "API version",
    "timestamp": "Timestamp",
    "echo": "Echo",
    "loading": "Checking API status",
    "success": "API is available",
    "traceId": "Trace ID: {traceId}",
    "errors": {
      "validationFailed": "The request was rejected.",
      "invalidRequest": "The request is invalid.",
      "notFound": "The API endpoint was not found.",
      "methodNotAllowed": "The API method is not allowed.",
      "internalError": "The API could not complete the request.",
      "genericProblem": "The API returned an error.",
      "network": "The API is unavailable.",
      "configuration": "The server API address is not configured."
    }
  },
  "boundaries": {
    "loading": "Loading page",
    "routeTitle": "Something went wrong",
    "routeDescription": "The page could not be rendered safely.",
    "notFoundTitle": "Page not found",
    "notFoundDescription": "The requested route does not exist."
  }
}
```

Create `src/messages/system.ru.json`:

```json
{
  "metadata": {
    "title": "Состояние системы Template",
    "description": "Проверка REST-связи из браузера и с сервера"
  },
  "page": {
    "eyebrow": "Итерация миграции 2",
    "title": "REST-соединение",
    "description": "Один generated SDK вызывает ASP.NET Core при серверном рендеринге и из браузера."
  },
  "status": {
    "ssrTitle": "Состояние API при серверном рендеринге",
    "browserTitle": "Состояние API в браузере",
    "source": "Источник",
    "sourceSsr": "Сервер Next.js",
    "sourceBrowser": "Браузер",
    "state": "Состояние",
    "apiVersion": "Версия API",
    "timestamp": "Время",
    "echo": "Эхо",
    "loading": "Проверяем состояние API",
    "success": "API доступен",
    "traceId": "Идентификатор трассировки: {traceId}",
    "errors": {
      "validationFailed": "Запрос отклонён.",
      "invalidRequest": "Запрос некорректен.",
      "notFound": "Endpoint API не найден.",
      "methodNotAllowed": "Метод API не разрешён.",
      "internalError": "API не смог выполнить запрос.",
      "genericProblem": "API вернул ошибку.",
      "network": "API недоступен.",
      "configuration": "Внутренний адрес API не настроен."
    }
  },
  "boundaries": {
    "loading": "Загружаем страницу",
    "routeTitle": "Произошла ошибка",
    "routeDescription": "Не удалось безопасно отобразить страницу.",
    "notFoundTitle": "Страница не найдена",
    "notFoundDescription": "Запрошенный маршрут не существует."
  }
}
```

- [ ] **Step 4: Implement deterministic locale resolution, statically bounded loaders, and the typed route**

Create `src/i18n/config.ts`:

```ts
export const locales = ["en", "ru"] as const;

export type AppLocale = (typeof locales)[number];

export const DEFAULT_LOCALE: AppLocale = "en";
export const APP_TIME_ZONE = "UTC";

export function isAppLocale(value: string): value is AppLocale {
  return locales.includes(value as AppLocale);
}

export function resolveAppLocale(
  value?: string,
): AppLocale {
  return value && isAppLocale(value) ? value : DEFAULT_LOCALE;
}
```

Create `src/i18n/messages.ts`:

```ts
import commonEn from "@/src/messages/common.en.json";
import commonRu from "@/src/messages/common.ru.json";
import systemEn from "@/src/messages/system.en.json";
import systemRu from "@/src/messages/system.ru.json";
import {
  APP_TIME_ZONE,
  resolveAppLocale,
  type AppLocale,
} from "@/src/i18n/config";

const englishMessages = {
  common: commonEn,
  system: systemEn,
};

export type I18nMessages = typeof englishMessages;

const messagesByLocale = {
  en: englishMessages,
  ru: {
    common: commonRu,
    system: systemRu,
  },
} satisfies Record<AppLocale, I18nMessages>;

export async function loadMessages(locale: AppLocale): Promise<I18nMessages> {
  return messagesByLocale[locale];
}

export async function loadI18nMessagesConfig() {
  const locale = resolveAppLocale(process.env.PUBLIC_DEFAULT_LOCALE);

  return {
    locale,
    messages: await loadMessages(locale),
    timeZone: APP_TIME_ZONE,
  };
}
```

Create `src/i18n/request.ts`:

```ts
import { getRequestConfig } from "next-intl/server";

import { loadI18nMessagesConfig } from "@/src/i18n/messages";

export default getRequestConfig(loadI18nMessagesConfig);
```

Create `src/types/next-intl.d.ts`:

```ts
import type { AppLocale } from "@/src/i18n/config";
import type { I18nMessages } from "@/src/i18n/messages";

declare module "next-intl" {
  interface AppConfig {
    Locale: AppLocale;
    Messages: I18nMessages;
  }
}
```

Create `src/features/application/application-routes.ts`:

```ts
import type { Route } from "next";

export const applicationRoutes = {
  home: "/" as Route,
} as const;
```

- [ ] **Step 5: Enable next-intl and make the root layout/page consume the fixed deployment locale**

Replace `next.config.ts` with:

```ts
import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

const nextConfig: NextConfig = {
  cacheComponents: true,
  output: "standalone",
  poweredByHeader: false,
  typedRoutes: true,
};

export default withNextIntl(nextConfig);
```

Replace `src/app/layout.tsx` with:

```tsx
import type { Metadata } from "next";
import { NextIntlClientProvider } from "next-intl";
import type { ReactNode } from "react";

import "@/src/app/globals.css";
import { loadI18nMessagesConfig } from "@/src/i18n/messages";

export async function generateMetadata(): Promise<Metadata> {
  const { messages } = await loadI18nMessagesConfig();

  return {
    title: messages.system.metadata.title,
    description: messages.system.metadata.description,
  };
}

export default async function RootLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  const { locale, messages, timeZone } = await loadI18nMessagesConfig();

  return (
    <html lang={locale}>
      <body>
        <NextIntlClientProvider
          locale={locale}
          messages={messages}
          timeZone={timeZone}
        >
          {children}
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
```

Replace `src/app/page.tsx` with:

```tsx
import { getTranslations } from "next-intl/server";

export default async function HomePage() {
  const t = await getTranslations("system.page");

  return (
    <main>
      <p>{t("eyebrow")}</p>
      <h1>{t("title")}</h1>
      <p>{t("description")}</p>
    </main>
  );
}
```

Replace `test/app/home-page.test.tsx` with:

```tsx
import { render, screen } from "@testing-library/react";

import HomePage from "@/src/app/page";

jest.mock("next-intl/server", () => ({
  getTranslations: async () => {
    const messages: Record<string, string> = {
      eyebrow: "Migration iteration 2",
      title: "REST connectivity",
      description:
        "The same generated SDK calls ASP.NET Core from server rendering and from the browser.",
    };

    return (key: string) => messages[key] ?? key;
  },
}));

describe("HomePage", () => {
  it("renders only the technical iteration-2 copy", async () => {
    render(await HomePage());

    expect(
      screen.getByRole("heading", { name: "REST connectivity" }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 6: Run the focused and broader checks**

Run:

```bash
npm test -- --runInBand test/i18n/messages.test.ts test/features/application-routes.test.ts test/app/home-page.test.tsx
npm run lint
npm run typecheck
PUBLIC_DEFAULT_LOCALE=ru npm run build
test -f .next/standalone/server.js
```

Expected: 9 parameterized/unit/component cases pass; lint/typecheck/build exit 0; the build uses the Russian fixed bundle without adding `/ru`.

- [ ] **Step 7: Commit the fixed-locale routing foundation**

```bash
git add apps/web
git commit -m "feat(web): add fixed-locale application foundation"
```

---

### Task 3: Generated REST Contract and Deterministic Drift Gate

**Files:**
- Create: `apps/web/openapi-ts.config.ts`
- Create: `apps/web/scripts/check-generated.mjs`
- Create: `apps/web/test/contracts/generated-sdk.test.ts`
- Generate and commit: `apps/web/src/lib/api/generated/**`
- Modify: `apps/web/package.json`

**Interfaces:**
- Consumes: `contracts/openapi/v1.json`, specifically `operationId: GetSystemStatus`.
- Produces: `createClient(config)` from `src/lib/api/generated/client`, generated type `SystemStatusResponse`, generated `getSystemStatus(options)` from `src/lib/api/generated`, and scripts `api:generate`/`api:check`.

- [ ] **Step 1: Write a failing contract-to-SDK test**

Create `test/contracts/generated-sdk.test.ts`:

```ts
/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import { getSystemStatus } from "@/src/lib/api/generated";

describe("generated system status SDK", () => {
  it("tracks the committed GetSystemStatus operation", () => {
    const contract = JSON.parse(
      readFileSync(
        resolve(process.cwd(), "../../contracts/openapi/v1.json"),
        "utf8",
      ),
    ) as {
      paths: {
        "/api/v1/system/status": {
          get: {
            operationId: string;
          };
        };
      };
    };

    expect(
      contract.paths["/api/v1/system/status"].get.operationId,
    ).toBe("GetSystemStatus");
    expect(getSystemStatus).toEqual(expect.any(Function));
  });
});
```

- [ ] **Step 2: Run the contract test and observe the missing generated module**

Run:

```bash
npm test -- --runInBand test/contracts/generated-sdk.test.ts
```

Expected: FAIL because `src/lib/api/generated` does not exist.

- [ ] **Step 3: Configure the exact generator**

Create `openapi-ts.config.ts`:

```ts
import { defineConfig } from "@hey-api/openapi-ts";

export default defineConfig({
  input: "../../contracts/openapi/v1.json",
  output: {
    path: "src/lib/api/generated",
    clean: true,
  },
  plugins: [
    "@hey-api/typescript",
    "@hey-api/client-fetch",
    {
      name: "@hey-api/sdk",
      operations: {
        strategy: "flat",
      },
    },
  ],
});
```

Add these exact scripts to `package.json`:

```json
{
  "scripts": {
    "api:generate": "openapi-ts --file openapi-ts.config.ts",
    "api:check": "node ./scripts/check-generated.mjs"
  }
}
```

The two entries augment the existing `scripts` object; all Task 1/2 entries remain.

- [ ] **Step 4: Generate the committed client and verify the actual output shape**

Run:

```bash
npm run api:generate
find src/lib/api/generated -type f | sort
grep -n "export const getSystemStatus" src/lib/api/generated/sdk.gen.ts
grep -n "export type SystemStatusResponse" src/lib/api/generated/types.gen.ts
grep -n "export const createClient" src/lib/api/generated/client/client.gen.ts
```

Expected: generation reports `@hey-api/openapi-ts v0.99.0`; the generated tree includes `index.ts`, `sdk.gen.ts`, `types.gen.ts`, `client.gen.ts`, `client/**`, and `core/**`; the three greps each match exactly one exported declaration.

- [ ] **Step 5: Add a byte-for-byte regeneration check**

Create `scripts/check-generated.mjs`:

```js
import { readdir, readFile } from "node:fs/promises";
import { resolve, relative } from "node:path";
import { spawnSync } from "node:child_process";

const webRoot = process.cwd();
const generatedRoot = resolve(webRoot, "src/lib/api/generated");

async function snapshot(directory) {
  const entries = new Map();

  async function visit(current) {
    const children = await readdir(current, { withFileTypes: true });

    for (const child of children.sort((left, right) =>
      left.name.localeCompare(right.name),
    )) {
      const path = resolve(current, child.name);

      if (child.isDirectory()) {
        await visit(path);
      } else if (child.isFile()) {
        entries.set(
          relative(directory, path),
          (await readFile(path)).toString("base64"),
        );
      }
    }
  }

  await visit(directory);
  return JSON.stringify([...entries]);
}

const before = await snapshot(generatedRoot);
const npm = process.platform === "win32" ? "npm.cmd" : "npm";
const generation = spawnSync(npm, ["run", "api:generate"], {
  cwd: webRoot,
  stdio: "inherit",
});

if (generation.status !== 0) {
  process.exit(generation.status ?? 1);
}

const after = await snapshot(generatedRoot);

if (before !== after) {
  console.error(
    "Generated REST client drifted. Inspect and commit the regenerated tree.",
  );
  process.exit(1);
}

console.log("Generated REST client is deterministic and current.");
```

- [ ] **Step 6: Prove generation, imports, and drift are green**

Run:

```bash
npm test -- --runInBand test/contracts/generated-sdk.test.ts
npm run typecheck
npm run api:check
grep -R "@hey-api/client-fetch" package.json package-lock.json | grep -v openapi-ts.config || true
```

Expected: the Jest test passes; typecheck exits 0; the second generation is byte-identical; the final grep shows no installed standalone `@hey-api/client-fetch` dependency.

- [ ] **Step 7: Commit the generated contract boundary**

```bash
git add apps/web/package.json apps/web/package-lock.json \
  apps/web/openapi-ts.config.ts apps/web/scripts/check-generated.mjs \
  apps/web/test/contracts/generated-sdk.test.ts \
  apps/web/src/lib/api/generated
git commit -m "feat(web): generate REST SDK from OpenAPI"
```

---

### Task 4: Safe Browser and Request-Scoped SSR API Adapters

**Files:**
- Create: `apps/web/.env.example`
- Create: `apps/web/src/lib/api/api-base-url.ts`
- Create: `apps/web/src/lib/api/result.ts`
- Create: `apps/web/src/lib/api/failures/normalize-api-failure.ts`
- Create: `apps/web/src/lib/api/load-system-status.ts`
- Create: `apps/web/src/lib/api/browser/client.ts`
- Create: `apps/web/src/lib/api/browser/load-browser-system-status.ts`
- Create: `apps/web/src/lib/api/server/client.ts`
- Create: `apps/web/src/lib/api/server/load-server-system-status.ts`
- Create: `apps/web/test/support/server-only.ts`
- Create: `apps/web/test/lib/api/api-base-url.test.ts`
- Create: `apps/web/test/lib/api/load-system-status.test.ts`
- Create: `apps/web/test/lib/api/browser-client.test.ts`
- Create: `apps/web/test/lib/api/server-client.test.ts`
- Create: `apps/web/test/typecheck/server-client.typecheck.ts`
- Modify: `apps/web/jest.config.mjs`
- Modify: `apps/web/next.config.ts`

**Interfaces:**
- Consumes: generated `Client`, `createClient`, `getSystemStatus`, `SystemStatusResponse`; `API_INTERNAL_BASE_URL`; optional `API_PROXY_TARGET`.
- Produces: `resolveApiBaseUrl(value): ApiBaseUrlResult`, `ApiResult<T>`, `SystemStatusResult`, `normalizeApiFailure(error, response?)`, `loadSystemStatus(client, echo, signal?)`, `createBrowserApiClient()`, `loadBrowserSystemStatus(signal?)`, `createServerApiClient(forwarded?)`, and `loadServerSystemStatus()`.

- [ ] **Step 1: Add failing pure URL, browser-client, and server isolation tests**

Create `test/lib/api/api-base-url.test.ts`:

```ts
import { resolveApiBaseUrl } from "@/src/lib/api/api-base-url";

describe("resolveApiBaseUrl", () => {
  it("normalizes an absolute HTTP(S) origin", () => {
    expect(resolveApiBaseUrl("http://127.0.0.1:5297/")).toEqual({
      ok: true,
      value: "http://127.0.0.1:5297",
    });
    expect(resolveApiBaseUrl("https://api.example.test")).toEqual({
      ok: true,
      value: "https://api.example.test",
    });
  });

  it("classifies absent configuration", () => {
    expect(resolveApiBaseUrl(undefined)).toEqual({
      ok: false,
      code: "api_configuration_missing",
    });
  });

  it.each([
    "/api",
    "ftp://api.example.test",
    "https://user:secret@api.example.test",
    "https://api.example.test/base",
    "https://api.example.test?secret=value",
    "https://api.example.test/#fragment",
  ])("rejects non-origin value %s", (value) => {
    expect(resolveApiBaseUrl(value)).toEqual({
      ok: false,
      code: "api_configuration_invalid",
    });
  });
});
```

Create `test/lib/api/browser-client.test.ts`:

```ts
/** @jest-environment node */

import { createBrowserApiClient } from "@/src/lib/api/browser/client";

describe("createBrowserApiClient", () => {
  it("uses a relative base and same-origin credentials", () => {
    const config = createBrowserApiClient().getConfig();

    expect(config.baseUrl).toBe("");
    expect(config.credentials).toBe("same-origin");
    expect(
      new Headers(config.headers as HeadersInit | undefined).has(
        "authorization",
      ),
    ).toBe(false);
  });
});
```

Create `test/lib/api/server-client.test.ts`:

```ts
/** @jest-environment node */

import { createServerApiClient } from "@/src/lib/api/server/client";
import { loadServerSystemStatus } from "@/src/lib/api/server/load-server-system-status";

const ORIGINAL_API_INTERNAL_BASE_URL = process.env.API_INTERNAL_BASE_URL;

afterEach(() => {
  if (ORIGINAL_API_INTERNAL_BASE_URL === undefined) {
    delete process.env.API_INTERNAL_BASE_URL;
  } else {
    process.env.API_INTERNAL_BASE_URL = ORIGINAL_API_INTERNAL_BASE_URL;
  }
});

describe("createServerApiClient", () => {
  it("creates isolated clients with only cookie and correlation forwarding", () => {
    process.env.API_INTERNAL_BASE_URL = "http://127.0.0.1:5297/";

    const first = createServerApiClient({
      cookie: "__Host-template.session=first",
      correlationId: "trace-first",
    });
    const second = createServerApiClient({
      cookie: "__Host-template.session=second",
    });
    const anonymous = createServerApiClient();

    expect(first.ok).toBe(true);
    expect(second.ok).toBe(true);
    expect(anonymous.ok).toBe(true);

    if (!first.ok || !second.ok || !anonymous.ok) {
      throw new Error("Expected valid server API clients.");
    }

    const firstHeaders = new Headers(
      first.client.getConfig().headers as HeadersInit | undefined,
    );
    const secondHeaders = new Headers(
      second.client.getConfig().headers as HeadersInit | undefined,
    );
    const anonymousHeaders = new Headers(
      anonymous.client.getConfig().headers as HeadersInit | undefined,
    );

    expect(first.client).not.toBe(second.client);
    expect(firstHeaders.get("cookie")).toBe(
      "__Host-template.session=first",
    );
    expect(firstHeaders.get("x-correlation-id")).toBe("trace-first");
    expect(firstHeaders.get("authorization")).toBeNull();
    expect(secondHeaders.get("cookie")).toBe(
      "__Host-template.session=second",
    );
    expect(secondHeaders.get("x-correlation-id")).toBeNull();
    expect(anonymousHeaders.get("cookie")).toBeNull();
    expect(first.client.getConfig().cache).toBe("no-store");
  });

  it("returns a safe configuration failure without issuing a request", async () => {
    delete process.env.API_INTERNAL_BASE_URL;

    await expect(loadServerSystemStatus()).resolves.toEqual({
      ok: false,
      failure: {
        kind: "configuration",
        code: "api_configuration_missing",
      },
    });
  });
});
```

Create `test/typecheck/server-client.typecheck.ts`:

```ts
import { createServerApiClient } from "@/src/lib/api/server/client";

createServerApiClient();
createServerApiClient({ cookie: "__Host-template.session=value" });
createServerApiClient({ correlationId: "trace-123" });
createServerApiClient({
  cookie: "__Host-template.session=value",
  correlationId: "trace-123",
});

// @ts-expect-error Arbitrary headers, including Authorization, are not accepted.
createServerApiClient({ authorization: "Bearer forbidden" });
```

- [ ] **Step 2: Run the focused tests/typecheck and observe missing adapters**

Run:

```bash
npm test -- --runInBand \
  test/lib/api/api-base-url.test.ts \
  test/lib/api/browser-client.test.ts \
  test/lib/api/server-client.test.ts
npm run typecheck
```

Expected: FAIL because the application API modules do not exist.

- [ ] **Step 3: Implement origin validation and safe result types**

Create `src/lib/api/api-base-url.ts`:

```ts
export type ApiConfigurationCode =
  | "api_configuration_missing"
  | "api_configuration_invalid";

export type ApiBaseUrlResult =
  | { ok: true; value: string }
  | { ok: false; code: ApiConfigurationCode };

export function resolveApiBaseUrl(value: string | undefined): ApiBaseUrlResult {
  const candidate = value?.trim();

  if (!candidate) {
    return { ok: false, code: "api_configuration_missing" };
  }

  try {
    const url = new URL(candidate);
    const hasOriginOnly =
      (url.protocol === "http:" || url.protocol === "https:") &&
      !url.username &&
      !url.password &&
      !url.search &&
      !url.hash &&
      url.pathname === "/";

    return hasOriginOnly
      ? { ok: true, value: url.origin }
      : { ok: false, code: "api_configuration_invalid" };
  } catch {
    return { ok: false, code: "api_configuration_invalid" };
  }
}
```

Create `src/lib/api/result.ts`:

```ts
import type { SystemStatusResponse } from "@/src/lib/api/generated";
import type { ApiConfigurationCode } from "@/src/lib/api/api-base-url";

export type ApiFailure =
  | {
      kind: "problem";
      code: string;
      status: number;
      traceId?: string;
    }
  | {
      kind: "network";
      code: "api_unavailable";
    }
  | {
      kind: "configuration";
      code: ApiConfigurationCode;
    };

export type ApiResult<T> =
  | { ok: true; data: T }
  | { ok: false; failure: ApiFailure };

export type SystemStatusResult = ApiResult<SystemStatusResponse>;
```

Create `src/lib/api/failures/normalize-api-failure.ts`:

```ts
import type { ApiFailure } from "@/src/lib/api/result";

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return typeof value === "object" && value !== null
    ? (value as Record<string, unknown>)
    : undefined;
}

function nonEmptyString(value: unknown): string | undefined {
  return typeof value === "string" && value.trim() ? value : undefined;
}

export function normalizeApiFailure(
  error: unknown,
  response?: Response,
): ApiFailure {
  if (!response) {
    return {
      kind: "network",
      code: "api_unavailable",
    };
  }

  const problem = asRecord(error);
  const traceId = nonEmptyString(problem?.traceId);

  return {
    kind: "problem",
    code: nonEmptyString(problem?.code) ?? "api_problem",
    status: response.status,
    ...(traceId ? { traceId } : {}),
  };
}
```

- [ ] **Step 4: Implement generated-operation adapters and the two client factories**

Create `src/lib/api/load-system-status.ts`:

```ts
import type { Client } from "@/src/lib/api/generated/client";
import { getSystemStatus } from "@/src/lib/api/generated";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import type { SystemStatusResult } from "@/src/lib/api/result";

export type SystemStatusSource = "browser" | "ssr";

export async function loadSystemStatus(
  client: Client,
  echo: SystemStatusSource,
  signal?: AbortSignal,
): Promise<SystemStatusResult> {
  try {
    const result = await getSystemStatus({
      client,
      query: { echo },
      cache: "no-store",
      signal,
    });

    if (result.data !== undefined) {
      return {
        ok: true,
        data: result.data.data,
      };
    }

    return {
      ok: false,
      failure: normalizeApiFailure(result.error, result.response),
    };
  } catch (error) {
    return {
      ok: false,
      failure: normalizeApiFailure(error),
    };
  }
}
```

Create `src/lib/api/browser/client.ts`:

```ts
"use client";

import {
  createClient,
  type Client,
} from "@/src/lib/api/generated/client";

export function createBrowserApiClient(): Client {
  return createClient({
    baseUrl: "",
    credentials: "same-origin",
  });
}
```

Create `src/lib/api/browser/load-browser-system-status.ts`:

```ts
"use client";

import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { loadSystemStatus } from "@/src/lib/api/load-system-status";
import type { SystemStatusResult } from "@/src/lib/api/result";

export function loadBrowserSystemStatus(
  signal?: AbortSignal,
): Promise<SystemStatusResult> {
  return loadSystemStatus(createBrowserApiClient(), "browser", signal);
}
```

Create `src/lib/api/server/client.ts`:

```ts
import "server-only";

import {
  createClient,
  type Client,
} from "@/src/lib/api/generated/client";
import { resolveApiBaseUrl } from "@/src/lib/api/api-base-url";
import type { ApiFailure } from "@/src/lib/api/result";

export type ForwardedApiHeaders = Readonly<{
  cookie?: string;
  correlationId?: string;
}>;

export type ServerApiClientResult =
  | { ok: true; client: Client }
  | {
      ok: false;
      failure: Extract<ApiFailure, { kind: "configuration" }>;
    };

export function createServerApiClient(
  forwarded: ForwardedApiHeaders = {},
): ServerApiClientResult {
  const baseUrl = resolveApiBaseUrl(process.env.API_INTERNAL_BASE_URL);

  if (!baseUrl.ok) {
    return {
      ok: false,
      failure: {
        kind: "configuration",
        code: baseUrl.code,
      },
    };
  }

  const headers = new Headers();

  if (forwarded.cookie) {
    headers.set("cookie", forwarded.cookie);
  }

  if (forwarded.correlationId) {
    headers.set("x-correlation-id", forwarded.correlationId);
  }

  const hasForwardedHeaders = Boolean(
    forwarded.cookie || forwarded.correlationId,
  );

  return {
    ok: true,
    client: createClient({
      baseUrl: baseUrl.value,
      cache: "no-store",
      ...(hasForwardedHeaders ? { headers } : {}),
    }),
  };
}
```

Create `src/lib/api/server/load-server-system-status.ts`:

```ts
import "server-only";

import { loadSystemStatus } from "@/src/lib/api/load-system-status";
import type { SystemStatusResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";

export function loadServerSystemStatus(): Promise<SystemStatusResult> {
  const client = createServerApiClient();

  if (!client.ok) {
    return Promise.resolve({
      ok: false,
      failure: client.failure,
    });
  }

  return loadSystemStatus(client.client, "ssr");
}
```

- [ ] **Step 5: Enable Jest imports of guarded server modules**

Create `test/support/server-only.ts`:

```ts
export {};
```

Replace `jest.config.mjs` with:

```js
import nextJest from "next/jest.js";

const createJestConfig = nextJest({ dir: "./" });

export default createJestConfig({
  coverageProvider: "v8",
  moduleNameMapper: {
    "^@/(.*)$": "<rootDir>/$1",
    "^server-only$": "<rootDir>/test/support/server-only.ts",
  },
  modulePathIgnorePatterns: ["<rootDir>/.next/"],
  setupFilesAfterEnv: ["<rootDir>/jest.setup.ts"],
  testEnvironment: "jsdom",
  testPathIgnorePatterns: ["<rootDir>/.next/", "<rootDir>/e2e/"],
});
```

- [ ] **Step 6: Add generated-operation success/problem/network tests**

Create `test/lib/api/load-system-status.test.ts`:

```ts
/** @jest-environment node */

import { createClient } from "@/src/lib/api/generated/client";
import { loadSystemStatus } from "@/src/lib/api/load-system-status";

const successPayload = {
  data: {
    status: "ok",
    apiVersion: "1",
    timestamp: "2026-07-24T00:00:00Z",
    echo: "ssr",
  },
};

describe("loadSystemStatus", () => {
  it("calls the generated operation with no-store and unwraps data", async () => {
    const fetchMock = jest
      .fn<ReturnType<typeof fetch>, Parameters<typeof fetch>>()
      .mockImplementation(async (input) => {
        const request = input as Request;

        expect(request.url).toBe(
          "https://api.example.test/api/v1/system/status?echo=ssr",
        );
        expect(request.cache).toBe("no-store");

        return new Response(JSON.stringify(successPayload), {
          status: 200,
          headers: { "content-type": "application/json" },
        });
      });
    const client = createClient({
      baseUrl: "https://api.example.test",
      fetch: fetchMock,
    });

    await expect(loadSystemStatus(client, "ssr")).resolves.toEqual({
      ok: true,
      data: successPayload.data,
    });
  });

  it("keeps only stable problem fields", async () => {
    const fetchMock = jest
      .fn<ReturnType<typeof fetch>, Parameters<typeof fetch>>()
      .mockResolvedValue(
        new Response(
          JSON.stringify({
            type: "https://example.test/internal",
            title: "Invariant internal title",
            status: 500,
            detail: "private backend detail",
            instance: "/api/v1/system/status",
            code: "internal_error",
            traceId: "trace-e2e",
          }),
          {
            status: 500,
            headers: { "content-type": "application/problem+json" },
          },
        ),
      );
    const client = createClient({
      baseUrl: "https://api.example.test",
      fetch: fetchMock,
    });

    const result = await loadSystemStatus(client, "browser");

    expect(result).toEqual({
      ok: false,
      failure: {
        kind: "problem",
        code: "internal_error",
        status: 500,
        traceId: "trace-e2e",
      },
    });
    expect(JSON.stringify(result)).not.toContain("private backend detail");
    expect(JSON.stringify(result)).not.toContain("Invariant internal title");
  });

  it("normalizes transport exceptions without exposing their message", async () => {
    const fetchMock = jest
      .fn<ReturnType<typeof fetch>, Parameters<typeof fetch>>()
      .mockRejectedValue(new TypeError("private-internal-origin"));
    const client = createClient({
      baseUrl: "https://api.example.test",
      fetch: fetchMock,
    });

    const result = await loadSystemStatus(client, "browser");

    expect(result).toEqual({
      ok: false,
      failure: {
        kind: "network",
        code: "api_unavailable",
      },
    });
    expect(JSON.stringify(result)).not.toContain("private-internal-origin");
  });
});
```

- [ ] **Step 7: Configure the server-only dev/E2E rewrite without creating a Route Handler**

Create `.env.example`:

```dotenv
# One deployment-wide language. Supported values: en, ru.
PUBLIC_DEFAULT_LOCALE=en

# Server Component -> ASP.NET Core. Required only when an SSR API call executes.
API_INTERNAL_BASE_URL=http://127.0.0.1:5297

# Browser /api/** -> ASP.NET Core transport bridge for local development/E2E.
# Leave unset in the final Kestrel-owned same-origin production topology.
API_PROXY_TARGET=http://127.0.0.1:5297
```

Replace `next.config.ts` with:

```ts
import type { NextConfig } from "next";
import createNextIntlPlugin from "next-intl/plugin";

import { resolveApiBaseUrl } from "./src/lib/api/api-base-url";

const withNextIntl = createNextIntlPlugin("./src/i18n/request.ts");

function readApiProxyTarget(): string | undefined {
  if (!process.env.API_PROXY_TARGET) {
    return undefined;
  }

  const target = resolveApiBaseUrl(process.env.API_PROXY_TARGET);

  if (!target.ok) {
    throw new Error(`API_PROXY_TARGET is invalid (${target.code}).`);
  }

  return target.value;
}

const apiProxyTarget = readApiProxyTarget();

const nextConfig: NextConfig = {
  cacheComponents: true,
  output: "standalone",
  poweredByHeader: false,
  typedRoutes: true,
  transpilePackages: [
    "@formatjs/fast-memoize",
    "@formatjs/icu-messageformat-parser",
    "@formatjs/icu-skeleton-parser",
    "@formatjs/intl-localematcher",
    "intl-messageformat",
    "next-intl",
    "use-intl",
  ],
  async rewrites() {
    return apiProxyTarget
      ? [
          {
            source: "/api/:path*",
            destination: `${apiProxyTarget}/api/:path*`,
          },
        ]
      : [];
  },
};

export default withNextIntl(nextConfig);
```

- [ ] **Step 8: Run focused tests, compile-time guards, build-without-API, and full task checks**

Run:

```bash
npm test -- --runInBand \
  test/lib/api/api-base-url.test.ts \
  test/lib/api/browser-client.test.ts \
  test/lib/api/server-client.test.ts \
  test/lib/api/load-system-status.test.ts
npm run api:check
npm run lint
npm run typecheck
env -u API_INTERNAL_BASE_URL -u API_PROXY_TARGET PUBLIC_DEFAULT_LOCALE=en npm run build
test -f .next/standalone/server.js
find src/app -type f -name 'route.*' -print
```

Expected: all focused tests pass; generated output remains byte-identical; lint/typecheck/build exit 0 without a live API; standalone server exists; the final `find` prints nothing.

- [ ] **Step 9: Commit both supported API paths**

```bash
git add apps/web
git commit -m "feat(web): add browser and SSR API clients"
```

---

### Task 5: Tailwind/shadcn Shell, Providers, Navigation, and Theme

**Files:**
- Create: `apps/web/components.json`
- Create: `apps/web/src/lib/utils.ts`
- Generate: `apps/web/src/components/ui/button.tsx`
- Generate: `apps/web/src/components/ui/card.tsx`
- Generate: `apps/web/src/components/ui/badge.tsx`
- Generate: `apps/web/src/components/ui/skeleton.tsx`
- Create: `apps/web/src/components/application/app-providers.tsx`
- Create: `apps/web/src/components/application/site-header.tsx`
- Create: `apps/web/src/components/application/theme-switcher.tsx`
- Create: `apps/web/test/support/render.tsx`
- Create: `apps/web/test/components/theme-switcher.test.tsx`
- Create: `apps/web/test/components/site-header.test.tsx`
- Modify: `apps/web/src/app/globals.css`
- Modify: `apps/web/src/app/layout.tsx`
- Modify: `apps/web/src/app/page.tsx`

**Interfaces:**
- Consumes: `I18nMessages`, `AppLocale`, `applicationRoutes.home`, next-themes, foundation messages.
- Produces: `cn(...)`, shadcn Button/Card/Badge/Skeleton, `AppProviders`, `ThemeSwitcher`, `SiteHeader`, neutral square-radius CSS tokens, and `renderWithMessages`/`withMessages` test helpers.

- [ ] **Step 1: Add failing hydration/theme and navigation tests**

Create `test/support/render.tsx`:

```tsx
import { NextIntlClientProvider } from "next-intl";
import type { ReactNode } from "react";
import { render } from "@testing-library/react";

import common from "@/src/messages/common.en.json";
import system from "@/src/messages/system.en.json";

export const englishMessages = { common, system };

export function withMessages(children: ReactNode) {
  return (
    <NextIntlClientProvider
      locale="en"
      messages={englishMessages}
      timeZone="UTC"
    >
      {children}
    </NextIntlClientProvider>
  );
}

export function renderWithMessages(children: ReactNode) {
  return render(withMessages(children));
}
```

Create `test/components/theme-switcher.test.tsx`:

```tsx
import { fireEvent, screen } from "@testing-library/react";
import { renderToStaticMarkup } from "react-dom/server";

import { ThemeSwitcher } from "@/src/components/application/theme-switcher";
import {
  renderWithMessages,
  withMessages,
} from "@/test/support/render";

const mockSetTheme = jest.fn();

jest.mock("next-themes", () => ({
  useTheme: () => ({
    resolvedTheme: "light",
    setTheme: mockSetTheme,
  }),
}));

beforeEach(() => {
  mockSetTheme.mockClear();
});

describe("ThemeSwitcher", () => {
  it("renders stable disabled markup before hydration", () => {
    const markup = renderToStaticMarkup(withMessages(<ThemeSwitcher />));

    expect(markup).toContain("disabled");
    expect(markup).toContain("Toggle theme");
  });

  it("switches from resolved light to dark after hydration", async () => {
    renderWithMessages(<ThemeSwitcher />);

    const button = screen.getByRole("button", {
      name: "Switch to dark theme",
    });
    expect(button).toBeEnabled();

    fireEvent.click(button);

    expect(mockSetTheme).toHaveBeenCalledWith("dark");
  });
});
```

Create `test/components/site-header.test.tsx`:

```tsx
import { screen } from "@testing-library/react";

import { SiteHeader } from "@/src/components/application/site-header";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/components/application/theme-switcher", () => ({
  ThemeSwitcher: () => (
    <button aria-label="Toggle theme" disabled type="button" />
  ),
}));

describe("SiteHeader", () => {
  it("contains only brand, root navigation, and theme control", () => {
    renderWithMessages(<SiteHeader />);

    expect(screen.getByRole("link", { name: "Template" })).toHaveAttribute(
      "href",
      "/",
    );
    expect(screen.getByRole("link", { name: "Home" })).toHaveAttribute(
      "href",
      "/",
    );
    expect(
      screen.getByRole("button", { name: "Toggle theme" }),
    ).toBeDisabled();
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the focused component tests and observe missing shell modules**

Run:

```bash
npm test -- --runInBand \
  test/components/theme-switcher.test.tsx \
  test/components/site-header.test.tsx
```

Expected: FAIL because `ThemeSwitcher` and `SiteHeader` do not exist.

- [ ] **Step 3: Configure shadcn and generate only the four approved primitives**

Create `components.json`:

```json
{
  "$schema": "https://ui.shadcn.com/schema.json",
  "style": "radix-lyra",
  "rsc": true,
  "tsx": true,
  "tailwind": {
    "config": "",
    "css": "src/app/globals.css",
    "baseColor": "neutral",
    "cssVariables": true,
    "prefix": ""
  },
  "iconLibrary": "tabler",
  "rtl": false,
  "aliases": {
    "components": "@/src/components",
    "utils": "@/src/lib/utils",
    "ui": "@/src/components/ui",
    "lib": "@/src/lib",
    "hooks": "@/src/hooks"
  },
  "menuColor": "default",
  "menuAccent": "subtle",
  "registries": {}
}
```

Create `src/lib/utils.ts`:

```ts
import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
```

Run:

```bash
npx --no-install shadcn add -y button card badge skeleton
find src/components/ui -maxdepth 1 -type f -print | sort
```

Expected: exactly `badge.tsx`, `button.tsx`, `card.tsx`, and `skeleton.tsx` are generated using `radix-lyra`; no sidebar, form, dialog, toast, or product component is added. Inspect the diff and retain `--radius: 0rem`.

Normalize `src/components/ui/button.tsx` to this complete checked-in content:

```tsx
"use client";

import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { Slot } from "radix-ui";

import { cn } from "@/src/lib/utils";

const buttonVariants = cva(
  "group/button inline-flex shrink-0 items-center justify-center rounded-none border border-transparent bg-clip-padding text-xs font-medium whitespace-nowrap transition-all outline-none select-none focus-visible:border-ring focus-visible:ring-1 focus-visible:ring-ring/50 active:not-aria-[haspopup]:translate-y-px disabled:pointer-events-none disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-1 aria-invalid:ring-destructive/20 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  {
    variants: {
      variant: {
        default: "bg-primary text-primary-foreground hover:bg-primary/80",
        outline:
          "border-border bg-background hover:bg-muted hover:text-foreground aria-expanded:bg-muted aria-expanded:text-foreground dark:border-input dark:bg-input/30 dark:hover:bg-input/50",
        secondary:
          "bg-secondary text-secondary-foreground hover:bg-[color-mix(in_oklch,var(--secondary),var(--foreground)_5%)] aria-expanded:bg-secondary aria-expanded:text-secondary-foreground",
        ghost:
          "hover:bg-muted hover:text-foreground aria-expanded:bg-muted aria-expanded:text-foreground dark:hover:bg-muted/50",
        destructive:
          "bg-destructive/10 text-destructive hover:bg-destructive/20 focus-visible:border-destructive/40 focus-visible:ring-destructive/20 dark:bg-destructive/20 dark:hover:bg-destructive/30 dark:focus-visible:ring-destructive/40",
        link: "text-primary underline-offset-4 hover:underline",
      },
      size: {
        default:
          "h-8 gap-1.5 px-2.5 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
        xs: "h-6 gap-1 rounded-none px-2 text-xs has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*='size-'])]:size-3",
        sm: "h-7 gap-1 rounded-none px-2.5 has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 [&_svg:not([class*='size-'])]:size-3.5",
        lg: "h-9 gap-1.5 px-2.5 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
        icon: "size-8",
        "icon-xs": "size-6 rounded-none [&_svg:not([class*='size-'])]:size-3",
        "icon-sm": "size-7 rounded-none",
        "icon-lg": "size-9",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
);

function Button({
  className,
  variant = "default",
  size = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean;
  }) {
  const Comp = asChild ? Slot.Root : "button";

  return (
    <Comp
      className={cn(buttonVariants({ variant, size, className }))}
      data-size={size}
      data-slot="button"
      data-variant={variant}
      {...props}
    />
  );
}

export { Button, buttonVariants };
```

Normalize `src/components/ui/card.tsx` to this complete checked-in content:

```tsx
import * as React from "react";

import { cn } from "@/src/lib/utils";

function Card({
  className,
  size = "default",
  ...props
}: React.ComponentProps<"div"> & { size?: "default" | "sm" }) {
  return (
    <div
      className={cn(
        "group/card flex flex-col gap-(--card-spacing) overflow-hidden rounded-none bg-card py-(--card-spacing) text-xs/relaxed text-card-foreground ring-1 ring-foreground/10 [--card-spacing:--spacing(4)] has-data-[slot=card-footer]:pb-0 has-[>img:first-child]:pt-0 data-[size=sm]:[--card-spacing:--spacing(3)] data-[size=sm]:has-data-[slot=card-footer]:pb-0 *:[img:first-child]:rounded-none *:[img:last-child]:rounded-none",
        className,
      )}
      data-size={size}
      data-slot="card"
      {...props}
    />
  );
}

function CardHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      className={cn(
        "group/card-header @container/card-header grid auto-rows-min items-start gap-1 rounded-none px-(--card-spacing) has-data-[slot=card-action]:grid-cols-[1fr_auto] has-data-[slot=card-description]:grid-rows-[auto_auto] [.border-b]:pb-(--card-spacing)",
        className,
      )}
      data-slot="card-header"
      {...props}
    />
  );
}

function CardTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      className={cn(
        "text-sm font-medium group-data-[size=sm]/card:text-sm",
        className,
      )}
      data-slot="card-title"
      {...props}
    />
  );
}

function CardDescription({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      className={cn("text-xs/relaxed text-muted-foreground", className)}
      data-slot="card-description"
      {...props}
    />
  );
}

function CardAction({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      className={cn(
        "col-start-2 row-span-2 row-start-1 self-start justify-self-end",
        className,
      )}
      data-slot="card-action"
      {...props}
    />
  );
}

function CardContent({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      className={cn("px-(--card-spacing)", className)}
      data-slot="card-content"
      {...props}
    />
  );
}

function CardFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      className={cn(
        "flex items-center rounded-none border-t p-(--card-spacing)",
        className,
      )}
      data-slot="card-footer"
      {...props}
    />
  );
}

export {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
};
```

Normalize `src/components/ui/badge.tsx` to this complete checked-in content:

```tsx
"use client";

import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { Slot } from "radix-ui";

import { cn } from "@/src/lib/utils";

const badgeVariants = cva(
  "group/badge inline-flex h-5 w-fit shrink-0 items-center justify-center gap-1 overflow-hidden rounded-none border border-transparent px-2 py-0.5 text-xs font-medium whitespace-nowrap transition-all focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 aria-invalid:border-destructive aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 [&>svg]:pointer-events-none [&>svg]:size-3!",
  {
    variants: {
      variant: {
        default:
          "bg-primary text-primary-foreground [a]:hover:bg-primary/80",
        secondary:
          "bg-secondary text-secondary-foreground [a]:hover:bg-secondary/80",
        destructive:
          "bg-destructive/10 text-destructive focus-visible:ring-destructive/20 dark:bg-destructive/20 dark:focus-visible:ring-destructive/40 [a]:hover:bg-destructive/20",
        outline:
          "border-border text-foreground [a]:hover:bg-muted [a]:hover:text-muted-foreground",
        ghost:
          "hover:bg-muted hover:text-muted-foreground dark:hover:bg-muted/50",
        link: "text-primary underline-offset-4 hover:underline",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  },
);

function Badge({
  className,
  variant = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"span"> &
  VariantProps<typeof badgeVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "span";

  return (
    <Comp
      className={cn(badgeVariants({ variant }), className)}
      data-slot="badge"
      data-variant={variant}
      {...props}
    />
  );
}

export { Badge, badgeVariants };
```

Normalize `src/components/ui/skeleton.tsx` to this complete checked-in content:

```tsx
import { cn } from "@/src/lib/utils";

function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      className={cn("animate-pulse rounded-none bg-muted", className)}
      data-slot="skeleton"
      {...props}
    />
  );
}

export { Skeleton };
```

- [ ] **Step 4: Replace global styles with the exact neutral Tailwind 4 foundation**

Replace `src/app/globals.css` with:

```css
@import "tailwindcss";
@import "tw-animate-css";
@import "shadcn/tailwind.css";

@custom-variant dark (&:is(.dark *));

@theme inline {
  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-card: var(--card);
  --color-card-foreground: var(--card-foreground);
  --color-primary: var(--primary);
  --color-primary-foreground: var(--primary-foreground);
  --color-secondary: var(--secondary);
  --color-secondary-foreground: var(--secondary-foreground);
  --color-muted: var(--muted);
  --color-muted-foreground: var(--muted-foreground);
  --color-accent: var(--accent);
  --color-accent-foreground: var(--accent-foreground);
  --color-destructive: var(--destructive);
  --color-border: var(--border);
  --color-input: var(--input);
  --color-ring: var(--ring);
  --radius-sm: calc(var(--radius) - 4px);
  --radius-md: calc(var(--radius) - 2px);
  --radius-lg: var(--radius);
  --radius-xl: calc(var(--radius) + 4px);
}

:root {
  --background: oklch(1 0 0);
  --foreground: oklch(0.145 0 0);
  --card: oklch(1 0 0);
  --card-foreground: oklch(0.145 0 0);
  --primary: oklch(0.205 0 0);
  --primary-foreground: oklch(0.985 0 0);
  --secondary: oklch(0.97 0 0);
  --secondary-foreground: oklch(0.205 0 0);
  --muted: oklch(0.97 0 0);
  --muted-foreground: oklch(0.556 0 0);
  --accent: oklch(0.97 0 0);
  --accent-foreground: oklch(0.205 0 0);
  --destructive: oklch(0.58 0.22 27);
  --border: oklch(0.922 0 0);
  --input: oklch(0.922 0 0);
  --ring: oklch(0.708 0 0);
  --radius: 0rem;
}

.dark {
  --background: oklch(0.145 0 0);
  --foreground: oklch(0.985 0 0);
  --card: oklch(0.205 0 0);
  --card-foreground: oklch(0.985 0 0);
  --primary: oklch(0.87 0 0);
  --primary-foreground: oklch(0.205 0 0);
  --secondary: oklch(0.269 0 0);
  --secondary-foreground: oklch(0.985 0 0);
  --muted: oklch(0.269 0 0);
  --muted-foreground: oklch(0.708 0 0);
  --accent: oklch(0.371 0 0);
  --accent-foreground: oklch(0.985 0 0);
  --destructive: oklch(0.704 0.191 22.216);
  --border: oklch(1 0 0 / 10%);
  --input: oklch(1 0 0 / 15%);
  --ring: oklch(0.556 0 0);
}

@layer base {
  * {
    @apply border-border outline-ring/50;
  }

  html {
    color-scheme: light;
  }

  html.dark {
    color-scheme: dark;
  }

  body {
    @apply min-h-screen bg-background text-foreground antialiased;
    margin: 0;
    font-family:
      Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont,
      "Segoe UI", sans-serif;
  }

  button:not(:disabled),
  [role="button"]:not(:disabled) {
    @apply cursor-pointer;
  }
}
```

- [ ] **Step 5: Implement the provider, theme toggle, and minimal header**

Create `src/components/application/app-providers.tsx`:

```tsx
"use client";

import { NextIntlClientProvider } from "next-intl";
import { ThemeProvider } from "next-themes";
import type { ReactNode } from "react";

import type { AppLocale } from "@/src/i18n/config";
import type { I18nMessages } from "@/src/i18n/messages";

export function AppProviders({
  children,
  locale,
  messages,
  timeZone,
}: Readonly<{
  children: ReactNode;
  locale: AppLocale;
  messages: I18nMessages;
  timeZone: string;
}>) {
  return (
    <ThemeProvider
      attribute="class"
      defaultTheme="system"
      disableTransitionOnChange
      enableSystem
      storageKey="template.theme"
    >
      <NextIntlClientProvider
        locale={locale}
        messages={messages}
        timeZone={timeZone}
      >
        {children}
      </NextIntlClientProvider>
    </ThemeProvider>
  );
}
```

Create `src/components/application/theme-switcher.tsx`:

```tsx
"use client";

import { IconMoon, IconSun } from "@tabler/icons-react";
import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";

import { Button } from "@/src/components/ui/button";

export function ThemeSwitcher() {
  const t = useTranslations("common.theme");
  const { resolvedTheme, setTheme } = useTheme();
  const mounted = useSyncExternalStore(
    () => () => {},
    () => true,
    () => false,
  );

  if (!mounted || !resolvedTheme) {
    return (
      <Button
        aria-label={t("toggle")}
        disabled
        size="icon"
        title={t("toggle")}
        variant="outline"
      >
        <IconSun aria-hidden="true" />
      </Button>
    );
  }

  const nextTheme = resolvedTheme === "dark" ? "light" : "dark";
  const nextThemeLabel =
    nextTheme === "dark" ? t("switchToDark") : t("switchToLight");

  return (
    <Button
      aria-label={nextThemeLabel}
      onClick={() => setTheme(nextTheme)}
      size="icon"
      title={nextThemeLabel}
      variant="outline"
    >
      {nextTheme === "dark" ? (
        <IconMoon aria-hidden="true" />
      ) : (
        <IconSun aria-hidden="true" />
      )}
    </Button>
  );
}
```

Create `src/components/application/site-header.tsx`:

```tsx
import Link from "next/link";
import { useTranslations } from "next-intl";

import { ThemeSwitcher } from "@/src/components/application/theme-switcher";
import { applicationRoutes } from "@/src/features/application/application-routes";

export function SiteHeader() {
  const t = useTranslations("common");

  return (
    <header className="border-b bg-background">
      <div className="mx-auto flex h-14 max-w-5xl items-center gap-6 px-4">
        <Link
          className="font-semibold tracking-tight"
          href={applicationRoutes.home}
        >
          {t("brand")}
        </Link>
        <nav aria-label={t("navigation.home")} className="mr-auto">
          <Link
            className="text-sm text-muted-foreground hover:text-foreground"
            href={applicationRoutes.home}
          >
            {t("navigation.home")}
          </Link>
        </nav>
        <ThemeSwitcher />
      </div>
    </header>
  );
}
```

- [ ] **Step 6: Compose the shell without product/auth dependencies**

Replace `src/app/layout.tsx` with:

```tsx
import type { Metadata } from "next";
import type { ReactNode } from "react";

import "@/src/app/globals.css";
import { AppProviders } from "@/src/components/application/app-providers";
import { SiteHeader } from "@/src/components/application/site-header";
import { loadI18nMessagesConfig } from "@/src/i18n/messages";

export async function generateMetadata(): Promise<Metadata> {
  const { messages } = await loadI18nMessagesConfig();

  return {
    title: messages.system.metadata.title,
    description: messages.system.metadata.description,
  };
}

export default async function RootLayout({
  children,
}: Readonly<{ children: ReactNode }>) {
  const { locale, messages, timeZone } = await loadI18nMessagesConfig();

  return (
    <html lang={locale} suppressHydrationWarning>
      <body>
        <AppProviders
          locale={locale}
          messages={messages}
          timeZone={timeZone}
        >
          <SiteHeader />
          {children}
        </AppProviders>
      </body>
    </html>
  );
}
```

Replace `src/app/page.tsx` with:

```tsx
import { getTranslations } from "next-intl/server";

export default async function HomePage() {
  const t = await getTranslations("system.page");

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-12">
      <section className="max-w-2xl space-y-3">
        <p className="text-xs font-medium uppercase tracking-[0.2em] text-muted-foreground">
          {t("eyebrow")}
        </p>
        <h1 className="text-3xl font-semibold tracking-tight">{t("title")}</h1>
        <p className="text-sm leading-6 text-muted-foreground">
          {t("description")}
        </p>
      </section>
    </main>
  );
}
```

- [ ] **Step 7: Run focused UI checks and verify both locale builds**

Run:

```bash
npm test -- --runInBand \
  test/components/theme-switcher.test.tsx \
  test/components/site-header.test.tsx \
  test/app/home-page.test.tsx
npm run lint
npm run typecheck
PUBLIC_DEFAULT_LOCALE=en npm run build
PUBLIC_DEFAULT_LOCALE=ru npm run build
test -f .next/standalone/server.js
```

Expected: theme/header/home tests pass; lint/typecheck and both fixed-locale builds exit 0; no locale-prefixed route is emitted.

- [ ] **Step 8: Commit the reviewer-visible shell**

```bash
git add apps/web
git commit -m "feat(web): add themeable application shell"
```

---

### Task 6: SSR and Browser Status UI with Safe Next.js Boundaries

**Files:**
- Create: `apps/web/src/components/system/status-card.tsx`
- Create: `apps/web/src/components/system/browser-system-status.tsx`
- Create: `apps/web/src/components/system/server-system-status.tsx`
- Create: `apps/web/src/app/loading.tsx`
- Create: `apps/web/src/app/error.tsx`
- Create: `apps/web/src/app/global-error.tsx`
- Create: `apps/web/src/app/not-found.tsx`
- Create: `apps/web/test/components/status-card.test.tsx`
- Create: `apps/web/test/components/browser-system-status.test.tsx`
- Create: `apps/web/test/components/server-system-status.test.tsx`
- Create: `apps/web/test/app/boundaries.test.tsx`
- Modify: `apps/web/src/app/page.tsx`
- Modify: `apps/web/test/app/home-page.test.tsx`

**Interfaces:**
- Consumes: `SystemStatusResult`, `ApiFailure`, `SystemStatusResponse`, `loadBrowserSystemStatus(signal)`, `loadServerSystemStatus()`, `connection()`.
- Produces: `StatusCardState`, `StatusCard`, `StatusCardSkeleton`, `BrowserSystemStatus`, `ServerSystemStatus`, and all required Next route/global boundaries.

- [ ] **Step 1: Add failing transport-independent status presentation tests**

Create `test/components/status-card.test.tsx`:

```tsx
import { fireEvent, screen } from "@testing-library/react";

import {
  StatusCard,
  StatusCardSkeleton,
} from "@/src/components/system/status-card";
import { renderWithMessages } from "@/test/support/render";

const success = {
  status: "ok",
  apiVersion: "1",
  timestamp: "2026-07-24T00:00:00Z",
  echo: "browser",
};

describe("StatusCard", () => {
  it("renders generated success data in an accessible live region", () => {
    renderWithMessages(
      <StatusCard
        source="browser"
        state={{ kind: "success", data: success }}
      />,
    );

    const region = screen.getByTestId("status-browser");
    expect(region).toHaveAttribute("role", "status");
    expect(region).toHaveAttribute("aria-live", "polite");
    expect(region).toHaveTextContent("API is available");
    expect(region).toHaveTextContent("Browser");
    expect(region).toHaveTextContent("ok");
    expect(region).toHaveTextContent("browser");
  });

  it.each([
    [
      { kind: "problem", code: "internal_error", status: 500 },
      "The API could not complete the request.",
    ],
    [
      { kind: "problem", code: "unknown_code", status: 400 },
      "The API returned an error.",
    ],
    [
      { kind: "network", code: "api_unavailable" },
      "The API is unavailable.",
    ],
    [
      { kind: "configuration", code: "api_configuration_missing" },
      "The server API address is not configured.",
    ],
  ] as const)("renders safe failure %p", (failure, expected) => {
    renderWithMessages(
      <StatusCard
        source="ssr"
        state={{ kind: "failure", failure }}
      />,
    );

    expect(screen.getByTestId("status-ssr")).toHaveTextContent(expected);
  });

  it("shows trace ID and delegates retry without raw details", () => {
    const onRetry = jest.fn();

    renderWithMessages(
      <StatusCard
        onRetry={onRetry}
        source="browser"
        state={{
          kind: "failure",
          failure: {
            kind: "problem",
            code: "validation_failed",
            status: 400,
            traceId: "trace-safe",
          },
        }}
      />,
    );

    expect(screen.getByText("Trace ID: trace-safe")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it("renders a labelled skeleton", () => {
    renderWithMessages(
      <StatusCardSkeleton
        label="Checking API status"
        source="ssr"
        title="Server-rendered API status"
      />,
    );

    expect(screen.getByTestId("status-ssr")).toHaveAttribute(
      "aria-label",
      "Checking API status",
    );
  });
});
```

- [ ] **Step 2: Add failing browser lifecycle and SSR runtime tests**

Create `test/components/browser-system-status.test.tsx`:

```tsx
import { fireEvent, screen, waitFor } from "@testing-library/react";

import { BrowserSystemStatus } from "@/src/components/system/browser-system-status";
import { loadBrowserSystemStatus } from "@/src/lib/api/browser/load-browser-system-status";
import type { SystemStatusResult } from "@/src/lib/api/result";
import { renderWithMessages } from "@/test/support/render";

jest.mock("@/src/lib/api/browser/load-browser-system-status", () => ({
  loadBrowserSystemStatus: jest.fn(),
}));

const mockLoadBrowserSystemStatus = jest.mocked(loadBrowserSystemStatus);
const browserSuccess: SystemStatusResult = {
  ok: true,
  data: {
    status: "ok",
    apiVersion: "1",
    timestamp: "2026-07-24T00:00:00Z",
    echo: "browser",
  },
};

beforeEach(() => {
  mockLoadBrowserSystemStatus.mockReset();
});

describe("BrowserSystemStatus", () => {
  it("moves from loading to success", async () => {
    mockLoadBrowserSystemStatus.mockResolvedValue(browserSuccess);

    renderWithMessages(<BrowserSystemStatus />);

    expect(screen.getByTestId("status-browser")).toHaveTextContent(
      "Checking API status",
    );
    await waitFor(() =>
      expect(screen.getByTestId("status-browser")).toHaveTextContent(
        "API is available",
      ),
    );
  });

  it("retries a safe failure and restores success", async () => {
    mockLoadBrowserSystemStatus
      .mockResolvedValueOnce({
        ok: false,
        failure: { kind: "network", code: "api_unavailable" },
      })
      .mockResolvedValueOnce(browserSuccess);

    renderWithMessages(<BrowserSystemStatus />);

    await screen.findByText("The API is unavailable.");
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    await screen.findByText("API is available");
    expect(mockLoadBrowserSystemStatus).toHaveBeenCalledTimes(2);
  });

  it("aborts an obsolete request on unmount", () => {
    let capturedSignal: AbortSignal | undefined;
    mockLoadBrowserSystemStatus.mockImplementation((signal) => {
      capturedSignal = signal;
      return new Promise<SystemStatusResult>(() => {});
    });

    const view = renderWithMessages(<BrowserSystemStatus />);
    view.unmount();

    expect(capturedSignal?.aborted).toBe(true);
  });
});
```

Create `test/components/server-system-status.test.tsx`:

```tsx
import { screen } from "@testing-library/react";
import { connection } from "next/server";

import { ServerSystemStatus } from "@/src/components/system/server-system-status";
import { loadServerSystemStatus } from "@/src/lib/api/server/load-server-system-status";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next/server", () => ({
  connection: jest.fn(),
}));
jest.mock("@/src/lib/api/server/load-server-system-status", () => ({
  loadServerSystemStatus: jest.fn(),
}));

const mockConnection = jest.mocked(connection);
const mockLoadServerSystemStatus = jest.mocked(loadServerSystemStatus);

beforeEach(() => {
  mockConnection.mockResolvedValue(undefined);
  mockLoadServerSystemStatus.mockReset();
});

describe("ServerSystemStatus", () => {
  it("waits for a request before loading the SSR status", async () => {
    mockLoadServerSystemStatus.mockResolvedValue({
      ok: true,
      data: {
        status: "ok",
        apiVersion: "1",
        timestamp: "2026-07-24T00:00:00Z",
        echo: "ssr",
      },
    });

    renderWithMessages(await ServerSystemStatus());

    expect(mockConnection).toHaveBeenCalledTimes(1);
    expect(mockLoadServerSystemStatus).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId("status-ssr")).toHaveTextContent("ssr");
  });

  it("keeps expected configuration failures inside the status region", async () => {
    mockLoadServerSystemStatus.mockResolvedValue({
      ok: false,
      failure: {
        kind: "configuration",
        code: "api_configuration_missing",
      },
    });

    renderWithMessages(await ServerSystemStatus());

    expect(screen.getByTestId("status-ssr")).toHaveTextContent(
      "The server API address is not configured.",
    );
  });
});
```

- [ ] **Step 3: Run the status tests and observe missing components**

Run:

```bash
npm test -- --runInBand \
  test/components/status-card.test.tsx \
  test/components/browser-system-status.test.tsx \
  test/components/server-system-status.test.tsx
```

Expected: FAIL because the three system components do not exist.

- [ ] **Step 4: Implement safe shared presentation**

Create `src/components/system/status-card.tsx`:

```tsx
import { useTranslations } from "next-intl";

import { Badge } from "@/src/components/ui/badge";
import { Button } from "@/src/components/ui/button";
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/src/components/ui/card";
import { Skeleton } from "@/src/components/ui/skeleton";
import type { SystemStatusSource } from "@/src/lib/api/load-system-status";
import type { ApiFailure } from "@/src/lib/api/result";
import type { SystemStatusResponse } from "@/src/lib/api/generated";

export type StatusCardState =
  | { kind: "loading" }
  | { kind: "success"; data: SystemStatusResponse }
  | { kind: "failure"; failure: ApiFailure };

type FailureMessageKey =
  | "errors.configuration"
  | "errors.genericProblem"
  | "errors.internalError"
  | "errors.invalidRequest"
  | "errors.methodNotAllowed"
  | "errors.network"
  | "errors.notFound"
  | "errors.validationFailed";

function failureMessageKey(failure: ApiFailure): FailureMessageKey {
  if (failure.kind === "network") {
    return "errors.network";
  }

  if (failure.kind === "configuration") {
    return "errors.configuration";
  }

  switch (failure.code) {
    case "validation_failed":
      return "errors.validationFailed";
    case "invalid_request":
      return "errors.invalidRequest";
    case "not_found":
      return "errors.notFound";
    case "method_not_allowed":
      return "errors.methodNotAllowed";
    case "internal_error":
      return "errors.internalError";
    default:
      return "errors.genericProblem";
  }
}

export function StatusCard({
  onRetry,
  source,
  state,
}: Readonly<{
  onRetry?: () => void;
  source: SystemStatusSource;
  state: StatusCardState;
}>) {
  const t = useTranslations("system.status");
  const actions = useTranslations("common.actions");
  const title = source === "ssr" ? t("ssrTitle") : t("browserTitle");
  const sourceLabel =
    source === "ssr" ? t("sourceSsr") : t("sourceBrowser");

  return (
    <Card
      aria-live="polite"
      data-testid={`status-${source}`}
      role="status"
    >
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <CardDescription>
          {t("source")}: {sourceLabel}
        </CardDescription>
        <CardAction>
          <Badge variant="outline">{source.toUpperCase()}</Badge>
        </CardAction>
      </CardHeader>
      <CardContent>
        {state.kind === "loading" ? (
          <p>{t("loading")}</p>
        ) : state.kind === "success" ? (
          <div className="space-y-3">
            <p className="font-medium">{t("success")}</p>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
              <dt className="text-muted-foreground">{t("state")}</dt>
              <dd>{state.data.status}</dd>
              <dt className="text-muted-foreground">{t("apiVersion")}</dt>
              <dd>{state.data.apiVersion}</dd>
              <dt className="text-muted-foreground">{t("timestamp")}</dt>
              <dd>
                <time dateTime={state.data.timestamp}>
                  {state.data.timestamp}
                </time>
              </dd>
              <dt className="text-muted-foreground">{t("echo")}</dt>
              <dd>{state.data.echo ?? "—"}</dd>
            </dl>
          </div>
        ) : (
          <div className="space-y-3">
            <p>{t(failureMessageKey(state.failure))}</p>
            {state.failure.kind === "problem" &&
            state.failure.traceId ? (
              <p className="font-mono text-xs text-muted-foreground">
                {t("traceId", { traceId: state.failure.traceId })}
              </p>
            ) : null}
            {onRetry ? (
              <Button onClick={onRetry} size="sm" variant="outline">
                {actions("retry")}
              </Button>
            ) : null}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function StatusCardSkeleton({
  label,
  source,
  title,
}: Readonly<{
  label: string;
  source: SystemStatusSource;
  title: string;
}>) {
  return (
    <Card
      aria-label={label}
      aria-live="polite"
      data-testid={`status-${source}`}
      role="status"
    >
      <CardHeader>
        <CardTitle>{title}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <Skeleton className="h-4 w-40" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-2/3" />
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 5: Implement browser cancellation/retry and request-time SSR**

Create `src/components/system/browser-system-status.tsx`:

```tsx
"use client";

import { useEffect, useState } from "react";

import { StatusCard, type StatusCardState } from "@/src/components/system/status-card";
import { loadBrowserSystemStatus } from "@/src/lib/api/browser/load-browser-system-status";

export function BrowserSystemStatus() {
  const [attempt, setAttempt] = useState(0);
  const [state, setState] = useState<StatusCardState>({ kind: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    let active = true;

    void loadBrowserSystemStatus(controller.signal).then((result) => {
      if (!active || controller.signal.aborted) {
        return;
      }

      setState(
        result.ok
          ? { kind: "success", data: result.data }
          : { kind: "failure", failure: result.failure },
      );
    });

    return () => {
      active = false;
      controller.abort();
    };
  }, [attempt]);

  function retry() {
    setState({ kind: "loading" });
    setAttempt((value) => value + 1);
  }

  return <StatusCard onRetry={retry} source="browser" state={state} />;
}
```

Create `src/components/system/server-system-status.tsx`:

```tsx
import { connection } from "next/server";

import { StatusCard } from "@/src/components/system/status-card";
import { loadServerSystemStatus } from "@/src/lib/api/server/load-server-system-status";

export async function ServerSystemStatus() {
  await connection();
  const result = await loadServerSystemStatus();

  return (
    <StatusCard
      source="ssr"
      state={
        result.ok
          ? { kind: "success", data: result.data }
          : { kind: "failure", failure: result.failure }
      }
    />
  );
}
```

- [ ] **Step 6: Compose both calls with an SSR Suspense boundary**

Replace `src/app/page.tsx` with:

```tsx
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";

import { BrowserSystemStatus } from "@/src/components/system/browser-system-status";
import { ServerSystemStatus } from "@/src/components/system/server-system-status";
import { StatusCardSkeleton } from "@/src/components/system/status-card";

export default async function HomePage() {
  const page = await getTranslations("system.page");
  const status = await getTranslations("system.status");

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-12">
      <section className="max-w-2xl space-y-3">
        <p className="text-xs font-medium uppercase tracking-[0.2em] text-muted-foreground">
          {page("eyebrow")}
        </p>
        <h1 className="text-3xl font-semibold tracking-tight">
          {page("title")}
        </h1>
        <p className="text-sm leading-6 text-muted-foreground">
          {page("description")}
        </p>
      </section>
      <section className="grid gap-4 md:grid-cols-2">
        <Suspense
          fallback={
            <StatusCardSkeleton
              label={status("loading")}
              source="ssr"
              title={status("ssrTitle")}
            />
          }
        >
          <ServerSystemStatus />
        </Suspense>
        <BrowserSystemStatus />
      </section>
    </main>
  );
}
```

Replace `test/app/home-page.test.tsx` with:

```tsx
import { render, screen } from "@testing-library/react";

import HomePage from "@/src/app/page";

jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "system.page.eyebrow": "Migration iteration 2",
      "system.page.title": "REST connectivity",
      "system.page.description":
        "The same generated SDK calls ASP.NET Core from server rendering and from the browser.",
      "system.status.loading": "Checking API status",
      "system.status.ssrTitle": "Server-rendered API status",
    };

    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));

jest.mock("@/src/components/system/server-system-status", () => ({
  ServerSystemStatus: () => <div data-testid="status-ssr">SSR status</div>,
}));

jest.mock("@/src/components/system/browser-system-status", () => ({
  BrowserSystemStatus: () => (
    <div data-testid="status-browser">Browser status</div>
  ),
}));

describe("HomePage", () => {
  it("renders only the technical iteration-2 vertical slice", async () => {
    render(await HomePage());

    expect(
      screen.getByRole("heading", { name: "REST connectivity" }),
    ).toBeInTheDocument();
    expect(screen.getByTestId("status-ssr")).toBeInTheDocument();
    expect(screen.getByTestId("status-browser")).toBeInTheDocument();
    expect(screen.queryByText(/sign in/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/workspace/i)).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 7: Add failing tests for distinct Next.js boundaries**

Create `test/app/boundaries.test.tsx`:

```tsx
import { fireEvent, render, screen } from "@testing-library/react";
import { renderToStaticMarkup } from "react-dom/server";

import RouteError from "@/src/app/error";
import GlobalError from "@/src/app/global-error";
import Loading from "@/src/app/loading";
import NotFound from "@/src/app/not-found";
import { renderWithMessages } from "@/test/support/render";

jest.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) => {
    const values: Record<string, string> = {
      "system.boundaries.loading": "Loading page",
      "system.boundaries.notFoundTitle": "Page not found",
      "system.boundaries.notFoundDescription":
        "The requested route does not exist.",
      "system.status.loading": "Checking API status",
      "system.status.ssrTitle": "Server-rendered API status",
      "system.status.browserTitle": "Browser API status",
      "common.actions.home": "Return home",
    };

    return (key: string) => values[`${namespace}.${key}`] ?? key;
  },
}));

describe("Next boundaries", () => {
  it("renders an accessible localized loading state", async () => {
    render(await Loading());

    expect(screen.getByRole("heading", { name: "Loading page" })).toBeInTheDocument();
    expect(screen.getAllByRole("status")).toHaveLength(2);
  });

  it("renders route error safely and calls reset", () => {
    const reset = jest.fn();

    renderWithMessages(
      <RouteError error={new Error("private-route-error")} reset={reset} />,
    );

    expect(screen.queryByText("private-route-error")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(reset).toHaveBeenCalledTimes(1);
  });

  it("renders not-found with a root link", async () => {
    render(await NotFound());

    expect(
      screen.getByRole("heading", { name: "Page not found" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Return home" })).toHaveAttribute(
      "href",
      "/",
    );
  });

  it("renders global error without any provider and hides raw errors", () => {
    const reset = jest.fn();

    const markup = renderToStaticMarkup(
      <GlobalError error={new Error("private-global-error")} reset={reset} />,
    );

    expect(markup).toContain("Application error");
    expect(markup).toContain("Reload application");
    expect(markup).not.toContain("private-global-error");
    expect(reset).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 8: Run the boundary test and observe missing route files**

Run:

```bash
npm test -- --runInBand test/app/boundaries.test.tsx
```

Expected: FAIL because `loading.tsx`, `error.tsx`, `global-error.tsx`, and `not-found.tsx` do not exist.

- [ ] **Step 9: Implement loading, route error, not-found, and provider-independent global error**

Create `src/app/loading.tsx`:

```tsx
import { getTranslations } from "next-intl/server";

import { StatusCardSkeleton } from "@/src/components/system/status-card";

export default async function Loading() {
  const boundaries = await getTranslations("system.boundaries");
  const status = await getTranslations("system.status");

  return (
    <main className="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-12">
      <h1 className="text-2xl font-semibold">{boundaries("loading")}</h1>
      <section className="grid gap-4 md:grid-cols-2">
        <StatusCardSkeleton
          label={status("loading")}
          source="ssr"
          title={status("ssrTitle")}
        />
        <StatusCardSkeleton
          label={status("loading")}
          source="browser"
          title={status("browserTitle")}
        />
      </section>
    </main>
  );
}
```

Create `src/app/error.tsx`:

```tsx
"use client";

import { useTranslations } from "next-intl";

import { Button } from "@/src/components/ui/button";

export default function RouteError({
  reset,
}: Readonly<{
  error: Error & { digest?: string };
  reset: () => void;
}>) {
  const boundaries = useTranslations("system.boundaries");
  const actions = useTranslations("common.actions");

  return (
    <main className="mx-auto max-w-2xl space-y-4 px-4 py-16">
      <h1 className="text-2xl font-semibold">{boundaries("routeTitle")}</h1>
      <p className="text-muted-foreground">{boundaries("routeDescription")}</p>
      <Button onClick={reset}>{actions("retry")}</Button>
    </main>
  );
}
```

Create `src/app/not-found.tsx`:

```tsx
import Link from "next/link";
import { getTranslations } from "next-intl/server";

import { Button } from "@/src/components/ui/button";
import { applicationRoutes } from "@/src/features/application/application-routes";

export default async function NotFound() {
  const boundaries = await getTranslations("system.boundaries");
  const actions = await getTranslations("common.actions");

  return (
    <main className="mx-auto max-w-2xl space-y-4 px-4 py-16">
      <h1 className="text-2xl font-semibold">
        {boundaries("notFoundTitle")}
      </h1>
      <p className="text-muted-foreground">
        {boundaries("notFoundDescription")}
      </p>
      <Button asChild>
        <Link href={applicationRoutes.home}>{actions("home")}</Link>
      </Button>
    </main>
  );
}
```

Create `src/app/global-error.tsx`:

```tsx
"use client";

export default function GlobalError({
  reset,
}: Readonly<{
  error: Error & { digest?: string };
  reset: () => void;
}>) {
  return (
    <html lang="en">
      <body>
        <main
          style={{
            fontFamily: "system-ui, sans-serif",
            margin: "4rem auto",
            maxWidth: "40rem",
            padding: "0 1rem",
          }}
        >
          <h1>Application error</h1>
          <p>The application could not render safely.</p>
          <button onClick={reset} type="button">
            Reload application
          </button>
        </main>
      </body>
    </html>
  );
}
```

- [ ] **Step 10: Run all status/boundary tests and prove build-time API independence**

Run:

```bash
npm test -- --runInBand \
  test/components/status-card.test.tsx \
  test/components/browser-system-status.test.tsx \
  test/components/server-system-status.test.tsx \
  test/app/boundaries.test.tsx \
  test/app/home-page.test.tsx
npm run lint
npm run typecheck
node -e "require('node:fs').rmSync('.next', { recursive: true, force: true })"
env -u API_INTERNAL_BASE_URL -u API_PROXY_TARGET PUBLIC_DEFAULT_LOCALE=en npm run build
test -f .next/standalone/server.js
```

Expected: all component/boundary tests pass; lint/typecheck/build exit 0; build logs contain no attempted request to `127.0.0.1:5297`; the SSR subtree is deferred below `connection()`/`Suspense`.

- [ ] **Step 11: Commit the complete technical vertical slice UI**

```bash
git add apps/web
git commit -m "feat(web): add SSR and browser status UI"
```

---

### Task 7: Source Boundaries and Full-Stack Playwright Smoke

**Files:**
- Create: `apps/web/scripts/check-boundaries.mjs`
- Create: `apps/web/playwright.config.ts`
- Create: `apps/web/e2e/system-status.spec.ts`
- Modify: `apps/web/package.json`

**Interfaces:**
- Consumes: built .NET API at `127.0.0.1:5297`, Next dev server at `127.0.0.1:3127`, `/api/health`, SSR/browser status regions.
- Produces: scripts `boundaries:check`, `e2e`, `e2e:install`; a two-process Playwright harness; executable source/dependency contract guards.

- [ ] **Step 1: Add an exact source/dependency boundary checker**

Create `scripts/check-boundaries.mjs`:

```js
import { readFile, readdir } from "node:fs/promises";
import { relative, resolve, sep } from "node:path";

const webRoot = process.cwd();
const sourceRoot = resolve(webRoot, "src");
const generatedRoot = resolve(sourceRoot, "lib/api/generated");
const packageJson = JSON.parse(
  await readFile(resolve(webRoot, "package.json"), "utf8"),
);
const violations = [];

const forbiddenPackages = [
  "@better-auth/prisma-adapter",
  "@prisma/client",
  "better-auth",
  "prisma",
  "@hey-api/client-fetch",
];
const installedPackages = {
  ...packageJson.dependencies,
  ...packageJson.devDependencies,
};

for (const packageName of forbiddenPackages) {
  if (packageName in installedPackages) {
    violations.push(`forbidden dependency: ${packageName}`);
  }
}

async function sourceFiles(directory) {
  const files = [];
  const children = await readdir(directory, { withFileTypes: true });

  for (const child of children) {
    const path = resolve(directory, child.name);

    if (child.isDirectory()) {
      files.push(...(await sourceFiles(path)));
    } else if (child.isFile() && /\.(?:ts|tsx)$/.test(child.name)) {
      files.push(path);
    }
  }

  return files;
}

for (const path of await sourceFiles(sourceRoot)) {
  const localPath = relative(webRoot, path).split(sep).join("/");
  const content = await readFile(path, "utf8");
  const isGenerated = path.startsWith(`${generatedRoot}${sep}`);

  if (isGenerated) {
    if (!content.startsWith("// This file is auto-generated by @hey-api/openapi-ts")) {
      violations.push(`generated header missing: ${localPath}`);
    }
    continue;
  }

  if (/["']use server["']/.test(content)) {
    violations.push(`Server Action directive: ${localPath}`);
  }
  if (/\bfetch\s*\(/.test(content)) {
    violations.push(`raw fetch outside generated runtime: ${localPath}`);
  }
  if (/(?:@prisma|better-auth)/i.test(content)) {
    violations.push(`forbidden full-stack import: ${localPath}`);
  }
  if (/NEXT_PUBLIC_[A-Z0-9_]*API/.test(content)) {
    violations.push(`public API origin variable: ${localPath}`);
  }
  if (
    /(?:localStorage|sessionStorage)/.test(content) &&
    /(?:authorization|bearer|token)/i.test(content)
  ) {
    violations.push(`browser credential storage: ${localPath}`);
  }
  if (
    /(?:interface|type)\s+(?:SystemStatusResponse|ProblemDetails|HttpValidationProblemDetails|ApiResponseOfSystemStatusResponse)\b/.test(
      content,
    )
  ) {
    violations.push(`handwritten OpenAPI DTO: ${localPath}`);
  }
  if (
    localPath.startsWith("src/lib/api/server/") &&
    /authorization/i.test(content)
  ) {
    violations.push(`Authorization forwarding surface: ${localPath}`);
  }
  if (
    localPath.startsWith("src/app/") &&
    /\/route\.(?:ts|tsx)$/.test(`/${localPath}`)
  ) {
    violations.push(`Next Route Handler: ${localPath}`);
  }
}

const generatedSdk = await readFile(
  resolve(generatedRoot, "sdk.gen.ts"),
  "utf8",
);

if (!/export const getSystemStatus\b/.test(generatedSdk)) {
  violations.push("generated getSystemStatus operation is missing");
}

if (violations.length > 0) {
  for (const violation of violations) {
    console.error(`- ${violation}`);
  }
  process.exit(1);
}

console.log("Web dependency and source boundaries are clean.");
```

Add these exact scripts to the existing `package.json` scripts object:

```json
{
  "scripts": {
    "boundaries:check": "node ./scripts/check-boundaries.mjs",
    "e2e": "playwright test",
    "e2e:install": "playwright install chromium"
  }
}
```

- [ ] **Step 2: Run the boundary checker**

Run:

```bash
npm run boundaries:check
```

Expected: `Web dependency and source boundaries are clean.` and exit 0. Any violation is fixed in the owning Task 1–6 file rather than weakened in this script.

- [ ] **Step 3: Configure the two-process loopback E2E harness**

Create `playwright.config.ts`:

```ts
import { defineConfig, devices } from "@playwright/test";

const apiOrigin = "http://127.0.0.1:5297";
const webOrigin = "http://127.0.0.1:3127";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? "github" : "list",
  use: {
    ...devices["Desktop Chrome"],
    baseURL: webOrigin,
    colorScheme: "light",
    screenshot: "only-on-failure",
    trace: "retain-on-failure",
  },
  webServer: [
    {
      command:
        "dotnet run --no-launch-profile --project ../api/src/Template.Api/Template.Api.csproj",
      env: {
        ASPNETCORE_ENVIRONMENT: "Development",
        ASPNETCORE_URLS: apiOrigin,
      },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      url: `${apiOrigin}/api/health`,
    },
    {
      command: "npm run dev -- --hostname 127.0.0.1 --port 3127",
      env: {
        API_INTERNAL_BASE_URL: apiOrigin,
        API_PROXY_TARGET: apiOrigin,
        PUBLIC_DEFAULT_LOCALE: "en",
      },
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      url: webOrigin,
    },
  ],
});
```

- [ ] **Step 4: Add the full-stack smoke, safe Problem Details retry, and keyboard theme tests**

Create `e2e/system-status.spec.ts`:

```ts
import { expect, test } from "@playwright/test";

const webOrigin = "http://127.0.0.1:3127";
const browserStatusPath = "/api/v1/system/status";

test("SSR and browser use the API through their supported paths", async ({
  page,
}) => {
  const pageErrors: string[] = [];
  const firstPartyServerErrors: string[] = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  page.on("response", (response) => {
    const url = new URL(response.url());
    if (url.origin === webOrigin && response.status() >= 500) {
      firstPartyServerErrors.push(`${response.status()} ${url.pathname}`);
    }
  });

  const browserRequest = page.waitForRequest((request) => {
    const url = new URL(request.url());
    return (
      url.pathname === browserStatusPath &&
      url.searchParams.get("echo") === "browser"
    );
  });

  await page.goto("/");

  const request = await browserRequest;
  expect(new URL(request.url()).origin).toBe(webOrigin);

  const serverRegion = page
    .getByTestId("status-ssr")
    .filter({ hasText: "API is available" });
  await expect(serverRegion).toContainText("API is available");
  await expect(serverRegion).toContainText("API version");
  await expect(serverRegion).toContainText("ssr");

  const browserRegion = page.getByTestId("status-browser");
  await expect(browserRegion).toContainText("API is available");
  await expect(browserRegion).toContainText("API version");
  await expect(browserRegion).toContainText("browser");

  expect(pageErrors).toEqual([]);
  expect(firstPartyServerErrors).toEqual([]);
});

test("browser Problem Details is safe and retry restores success", async ({
  page,
}) => {
  const routePattern = "**/api/v1/system/status**";

  await page.route(routePattern, async (route) => {
    const url = new URL(route.request().url());

    if (url.searchParams.get("echo") !== "browser") {
      await route.continue();
      return;
    }

    await route.fulfill({
      status: 500,
      contentType: "application/problem+json",
      body: JSON.stringify({
        type: "https://example.test/internal",
        title: "Invariant internal title",
        status: 500,
        detail: "private backend detail",
        instance: "/api/v1/system/status",
        code: "internal_error",
        traceId: "trace-playwright",
      }),
    });
  });

  await page.goto("/");

  const browserRegion = page.getByTestId("status-browser");
  await expect(browserRegion).toContainText(
    "The API could not complete the request.",
  );
  await expect(browserRegion).toContainText("Trace ID: trace-playwright");
  await expect(browserRegion).not.toContainText("private backend detail");
  await expect(browserRegion).not.toContainText("Invariant internal title");

  await page.unroute(routePattern);
  await browserRegion.getByRole("button", { name: "Retry" }).click();

  await expect(browserRegion).toContainText("API is available");
  await expect(browserRegion).toContainText("browser");
});

test("theme toggle is keyboard accessible", async ({ page }) => {
  await page.goto("/");

  const toggle = page.getByRole("button", { name: "Switch to dark theme" });
  await expect(toggle).toBeEnabled();
  await toggle.focus();
  await page.keyboard.press("Enter");

  await expect(page.locator("html")).toHaveClass(/dark/);
});
```

- [ ] **Step 5: Install Chromium and run the smoke against real API/UI processes**

Run from `apps/web`; the API web-server command performs an incremental build:

```bash
npm run e2e:install
npm run e2e
```

Expected: Playwright starts/reuses API `:5297` and Next `:3127`; 3 tests pass. The first proves SSR `echo=ssr` plus same-origin browser `echo=browser`; the second proves safe Problem Details and retry; the third proves keyboard theme switching.

- [ ] **Step 6: Run all web checks as one reviewer gate**

Run:

```bash
npm run api:check
npm run boundaries:check
npm run format
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
npm run build
test -f .next/standalone/server.js
npm run e2e
```

Expected: every command exits 0; generated SDK remains unchanged; all Jest and 3 Playwright tests pass; standalone server exists.

- [ ] **Step 7: Commit the executable architecture guards**

```bash
git add apps/web
git commit -m "test(web): add boundary guards and full-stack smoke"
```

---

### Task 8: Durable Conventions, Migration Evidence, and Final Verification

**Files:**
- Create: `docs/web-conventions.md`
- Modify: `docs/aspnetcore-migration-plan.md`
- Verify unchanged: `contracts/openapi/v1.json`
- Verify unchanged: `template/**`

**Interfaces:**
- Consumes: the complete Task 1–7 implementation and actual command summaries.
- Produces: durable iteration-2 decisions, completed migration register/evidence, explicit known reference gaps, and a clean final branch.

- [ ] **Step 1: Write the durable web conventions**

Create `docs/web-conventions.md` with this exact content:

````markdown
# Web application conventions

## Ownership boundary

`apps/web` is a Next.js UI and never owns `/api/**`, business logic, sessions,
database access, or external integrations. ASP.NET Core is the only API host.
The web application contains no Prisma, Better Auth, Server Actions, API Route
Handlers, direct database access, or browser bearer-token storage.

## Generated REST contract

`contracts/openapi/v1.json` is the input to
`apps/web/openapi-ts.config.ts`. `npm run api:generate` writes the committed
`src/lib/api/generated` tree. Generated files are never hand-edited or formatted.
`npm run api:check` regenerates and byte-compares the entire tree.

Application data adapters call generated SDK operations and import generated
DTOs. They do not call raw `fetch` and do not redefine response or Problem
Details types. `npm run boundaries:check` enforces these rules.

## Browser API calls

Browser clients use a relative base URL, `credentials: "same-origin"`, and
`/api/**`. No API origin is compiled into a `NEXT_PUBLIC_*` variable. During
local development and E2E only, `API_PROXY_TARGET` enables an external Next.js
rewrite to ASP.NET Core. The variable is unset in the final production topology,
where Kestrel owns `/api/**` directly.

## Server-rendered API calls

SSR uses absolute server-only `API_INTERNAL_BASE_URL`. A new generated client is
created for each call. The factory accepts only
`{ cookie?: string; correlationId?: string }`; it never accepts an arbitrary
header collection and never forwards `Authorization`. Callers read request state
outside cached scopes and pass only explicitly permitted values. The anonymous
system-status probe passes no forwarded headers.

Uncached request-time calls use `cache: "no-store"`. With Cache Components,
runtime SSR work begins below `connection()` and a `Suspense` boundary so builds
do not require a live API and request configuration is not frozen at build time.

## Locale and theme

Routes have no locale prefix. The deployment language is fixed to `en` or `ru`
by `PUBLIC_DEFAULT_LOCALE`; missing or invalid values fall back to `en`.
Build/runtime use the same value, and changing language requires rebuild/restart.
Cookies, `Accept-Language`, user settings, and a language switcher do not select
locale while Cache Components uses this fixed strategy. next-intl uses the
fixed `UTC` time zone in both server configuration and the client provider.

Theme supports system, light, and dark modes through next-themes. Server markup
uses a stable disabled toggle until hydration, and `<html>` suppresses only the
expected theme-class hydration difference.

## Loading and failures

The shared API result is `problem | network | configuration`. Problem rendering
uses stable `code`, HTTP status, and optional `traceId`; invariant-English
backend title/detail and raw exception messages are not displayed.

SSR expected failures stay inside the SSR status region. Browser requests abort
when obsolete and expose an explicit retry. Route loading, route error,
not-found, and provider-independent global error each have a separate boundary.
Status changes use an accessible live region.

## Local verification

From `apps/web`:

```bash
npm ci
npm run api:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
npm run build
npm run e2e
test -f .next/standalone/server.js
```

E2E starts ASP.NET Core on `127.0.0.1:5297` and Next.js on
`127.0.0.1:3127`. The API readiness probe is `/api/health`.
````

- [ ] **Step 2: Run the mandatory .NET and OpenAPI verification from repository root**

Run:

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json
```

Expected: restore/build/test/export exit 0; record the exact passed .NET test count from the console; the committed OpenAPI has no diff.

- [ ] **Step 3: Reinstall from lock and run the complete web acceptance matrix**

Run from `apps/web`:

```bash
npm ci
npm run api:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
node -e "require('node:fs').rmSync('.next', { recursive: true, force: true })"
env -u API_INTERNAL_BASE_URL -u API_PROXY_TARGET PUBLIC_DEFAULT_LOCALE=en npm run build
test -f .next/standalone/server.js
npm run e2e
```

Expected: every command exits 0. Capture the exact Jest suite/test counts, 3/3 Playwright count, and standalone result from this clean-lock run.

- [ ] **Step 4: Update the migration register and acceptance evidence using only observed results**

In `docs/aspnetcore-migration-plan.md`:

1. Replace the combined `2–12 | Не начаты` row with:

```markdown
| 2 — чистый Next.js UI foundation | Завершена | Standalone Next.js, fixed en/ru locale, theme/navigation/boundaries, generated REST SDK, isolated browser/SSR clients and full-stack smoke приняты. |
| 3–12 | Не начаты | Следующий dependency gate — PostgreSQL, EF Core, Identity и базовая cookie authentication без переноса старых данных. |
```

2. Add `## Acceptance evidence: итерация 2` after the iteration-1 evidence. State that scope is `apps/web`, `docs/web-conventions.md`, this design/plan, and the migration register; API source, schema, and `template/` did not change.
3. Add this exact correspondence table:

```markdown
| Reference | Новый API | Новый UI | Test/evidence |
| --- | --- | --- | --- |
| `template/src/app/layout.tsx`, `globals.css`, `app-providers.tsx` | N/A | root layout, providers, Tailwind/shadcn tokens | layout/component tests, standalone production build |
| `template/src/i18n/**`, `common.{en,ru}.json` | N/A | fixed deployment locale with common/system bundles | locale fallback and bundle-shape tests |
| `template/src/components/application/theme/theme-switcher.tsx` | N/A | hydration-safe theme switcher | SSR markup, click, and keyboard E2E |
| `template/src/features/application/application-routes.ts`, public header primitives | N/A | typed `/` and minimal header | route/header tests |
| `template/src/app/api/health/route.ts`, `template/e2e/support/config.ts` | existing `/api/health`, `/api/v1/system/status` | SSR and browser status regions over one generated SDK | adapter tests and full-stack Playwright |
| `template/src/app/global-error.tsx`, `not-found.tsx`, error components | existing RFC Problem Details | loading/error/not-found/global boundaries | boundary and intercepted-error tests |
| reference public home account/workspace loaders | outside scope | not copied | source/dependency guard |
| all reference Prisma models | no schema change | no data access | source/dependency guard |
```

4. Add an acceptance command table containing every command from Steps 2–3 and its actual result. Copy real counts from console output; do not predict or round them.
5. State the intentional reference differences: technical home instead of product landing, generated ASP.NET REST instead of Server Actions, RFC Problem Details, fixed deployment language with no switcher, and no auth/account/workspace/product data.
6. State the next gate: iteration 3 owns PostgreSQL, EF Core migrations, Identity, register/login/logout/current-user, secure HttpOnly same-origin cookie issuance, and antiforgery; the dev/E2E Next rewrite is not the final production proxy.

- [ ] **Step 5: Run immutable-reference, scope, and whitespace guards**

Run from repository root:

```bash
git diff --exit-code -- template/
git diff --exit-code -- contracts/openapi/v1.json
git diff --check
git status --short
```

Expected: the first three commands exit 0; `git status --short` lists only intended `apps/web` and `docs` changes for this task.

- [ ] **Step 6: Commit iteration-2 documentation and acceptance evidence**

```bash
git add docs/web-conventions.md docs/aspnetcore-migration-plan.md \
  docs/superpowers/specs/2026-07-23-nextjs-ui-foundation-design.md \
  docs/superpowers/plans/2026-07-24-nextjs-ui-foundation.md
git commit -m "docs: complete migration iteration 2"
```

- [ ] **Step 7: Perform the final post-commit verification**

Run:

```bash
git status --short --branch
git diff --exit-code origin/main...HEAD -- template/
git diff --exit-code origin/main...HEAD -- contracts/openapi/v1.json
git log --oneline --decorate -10
```

Expected: clean branch `codex/iteration-2-web-foundation`; no reference/contract diff; the task commits are visible in order.

---

## Final Reporting Checklist

The completion response must state, concisely and with observed values:

- implemented fixed-locale shell, generated SDK, browser and SSR transports, safe failures, status UI, boundaries, and E2E;
- browser request is same-origin and SSR request uses `API_INTERNAL_BASE_URL`;
- the exact .NET/Jest/Playwright/build/check results;
- `git diff -- template/` and OpenAPI drift are empty;
- intentional differences from reference;
- iteration-3 blockers/out-of-scope work, especially persistence, Identity, cookie issuance, antiforgery, account/workspace/product UI, and production reverse proxy.

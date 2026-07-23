# Итерация 2: чистый Next.js UI foundation

**Дата:** 2026-07-23  
**Статус:** дизайн согласован пользователем  
**Долгосрочная дорожная карта:** [`../../aspnetcore-migration-plan.md`](../../aspnetcore-migration-plan.md)

## 1. Цель

Итерация 2 создаёт новый `apps/web` как независимое UI-приложение на Next.js,
которое получает данные только из ASP.NET Core REST API. Вертикальный smoke-срез
доказывает оба поддерживаемых пути вызова одного generated SDK:

1. browser → относительный same-origin `/api/**`;
2. Next.js SSR → абсолютный внутренний адрес ASP.NET Core.

Итерация создаёт TypeScript/Tailwind/shadcn, i18n, theme, navigation,
loading/error и browser E2E foundations, но не переносит login, product landing,
account/workspace UI, данные или аутентификацию.

## 2. Изученный контекст

Перед проектированием проверены:

- корневой `AGENTS.md`;
- `docs/aspnetcore-migration-plan.md` и `docs/api-conventions.md`;
- iteration-1 design/plan, текущий API host, integration tests и
  `contracts/openapi/v1.json`;
- reference-конфигурация `template/package.json`, `next.config.ts`,
  `components.json`, Tailwind, TypeScript, Jest и Playwright;
- reference root layouts, providers, theme switcher, i18n, routes,
  loading/error primitives и public smoke;
- `template/src/app/api/health/route.ts`, E2E readiness wiring и UI tests;
- reference landing/header/sidebar dependencies on Better Auth, Server Actions,
  account loaders and workspace loaders;
- все Prisma models из `template/prisma/schema.prisma`.

Reference public landing нельзя переносить целиком в эту итерацию: она вызывает
account/workspace server loaders и зависит от Better Auth. Ни одна Prisma model
не относится к iteration-2 data scope.

Актуальные решения сверены с официальной документацией:

- [Next.js installation](https://nextjs.org/docs/app/getting-started/installation);
- [Cache Components](https://nextjs.org/docs/app/api-reference/config/next-config-js/cacheComponents);
- [`use cache`](https://nextjs.org/docs/app/api-reference/directives/use-cache);
- [external rewrites](https://nextjs.org/docs/app/api-reference/config/next-config-js/rewrites);
- [standalone output](https://nextjs.org/docs/app/api-reference/config/next-config-js/output);
- [Next.js error handling](https://nextjs.org/docs/app/getting-started/error-handling);
- [Jest](https://nextjs.org/docs/app/guides/testing/jest) и
  [Playwright](https://nextjs.org/docs/app/guides/testing/playwright);
- [shadcn with Tailwind v4](https://ui.shadcn.com/docs/tailwind-v4);
- [Hey API TypeScript generation](https://heyapi.dev/docs/openapi/typescript/get-started),
  [Fetch client](https://heyapi.dev/docs/openapi/typescript/clients/fetch) и
  [SDK plugin](https://heyapi.dev/docs/openapi/typescript/plugins/sdk).

После установки зависимостей и до изменения Next.js-кода исполнитель также
читает package-local документацию установленной версии Next.js.

## 3. Scope

### Входит

- новый `apps/web` на Next.js App Router и React;
- Node.js 22 or newer (the code generator's minimum), strict TypeScript, npm
  lockfile, ESLint, Prettier and typecheck;
- Tailwind CSS 4 and a minimal shadcn baseline matching the reference design
  direction: `radix-lyra`, neutral tokens, CSS variables, Tabler icons and
  square radius;
- `output: "standalone"`, `cacheComponents: true`;
- root layout, providers, global styles and minimal metadata;
- deployment-wide static `en`/`ru` i18n foundation;
- hydration-safe system/light/dark theme support;
- typed root route and minimal header/navigation primitives;
- generated TypeScript Fetch client and flat SDK from committed OpenAPI;
- separate browser and SSR client factories over the same generated SDK;
- localized loading, expected API failure, not-found and route error states;
- provider-independent global error boundary;
- technical `/` page proving SSR and browser API calls;
- Jest/Testing Library and full-stack Playwright harness;
- durable web conventions, migration register and acceptance evidence.

### Не входит

- login, logout, register, current-user or account settings;
- Identity, real session issuance, antiforgery or OAuth;
- reference product landing copy or authenticated application shell;
- dashboard, sidebar, workspaces, organizations, teams, invitations or API keys;
- Prisma, Better Auth, Server Actions, Route Handlers under `/api/**`, or direct DB
  access;
- PostgreSQL, EF Core migrations, seed, transactions or persistent schema;
- request-dependent locale detection, locale-prefixed routes or a language
  switcher;
- TanStack Query, SWR, MSW or a general product-state layer;
- MDX/documents, analytics, remote cache handlers or Yandex Metrika;
- final security-header/CSP ownership, YARP, Docker, Aspire or production process
  supervision;
- active OpenSpec change/spec.

### Toolchain baseline

The design-date baseline is Node.js 24 (with `engines.node` accepting supported
Node.js 22+), Next.js `16.2.11`, React/React DOM `19.2.8`, next-intl `4.13.4`,
next-themes `0.4.6`, Tailwind CSS `4.3.3` and shadcn `4.14.1`. The committed
`package-lock.json` is the reproducibility authority. Any compatibility-driven
deviation discovered from installed package documentation is documented before
functional Next.js code is written.

## 4. Карта соответствий

| Reference | Новый API | Новый UI | Проверка |
| --- | --- | --- | --- |
| `template/src/app/layout.tsx`, `globals.css`, `app-providers.tsx` | N/A | root layout, providers, Tailwind/shadcn tokens | layout/component tests, production build |
| `template/src/i18n/**`, `common.{en,ru}.json` | N/A | deployment-wide locale and foundation messages | locale fallback and message-shape tests |
| `template/src/components/application/theme/theme-switcher.tsx` | N/A | hydration-safe theme switcher | SSR markup and interaction tests |
| `template/src/features/application/application-routes.ts`, public header primitives | N/A | typed `/` route and minimal header/navigation | route and navigation tests |
| `template/src/app/api/health/route.ts`, `template/e2e/support/config.ts` | existing `/api/health` and `/api/v1/system/status` | SSR and browser status panels | full-stack Playwright smoke |
| `template/src/app/global-error.tsx`, `not-found.tsx`, error components | existing RFC Problem Details | loading/error/not-found conventions | component and failure-state tests |
| reference public home account/workspace loaders | outside scope | not copied | dependency and source guards |
| all reference Prisma models | no API/schema changes | no data access | package/source guards |

## 5. REST-контракт до UI-работы

Iteration 2 consumes the already committed operation
`GET /api/v1/system/status`; it does not change the API contract.

| Property | Decision |
| --- | --- |
| Authorization | Anonymous |
| Browser query | `echo=browser` |
| SSR query | `echo=ssr` |
| Success | `{ "data": { "status", "apiVersion", "timestamp", "echo" } }` |
| Expected failures | generated RFC Problem Details types for 400/404/405/500 |
| Caching | both smoke calls request `no-store` |
| Pagination/filtering | not applicable |
| Mutations/antiforgery | none |
| Transactions | none |
| Schema migration/seed | none |
| Background work/cache invalidation/audit | none |

`GET /api/health` is used only as the API readiness URL for E2E orchestration.
The UI does not call the protected system probe and does not simulate a session.

## 6. Web application structure

The exact file list is finalized in the implementation plan, but responsibilities
remain separated as follows:

```text
apps/web/
├── src/app/                       # routes and Next boundaries
├── src/components/application/    # providers, header, theme, navigation
├── src/components/system/         # technical status presentation
├── src/i18n/                      # static locale resolution and message loading
├── src/messages/                  # only common/system en+ru bundles
├── src/lib/api/
│   ├── generated/                 # committed generated code; never hand-edited
│   ├── browser/                   # client-only factory
│   ├── server/                    # server-only request-scoped factory
│   └── failures/                  # shared generated-error normalization
├── test/                          # Jest/Testing Library
└── e2e/                           # Playwright smoke and support
```

Each unit has one responsibility:

- generated code mirrors OpenAPI;
- runtime factories select transport and credentials;
- an operation adapter invokes the generated function derived from OpenAPI
  `operationId: GetSystemStatus`;
- status components render state without owning transport details;
- Next route files compose these units.

No handwritten interface duplicates an OpenAPI DTO.

## 7. Generated client discipline

The selected approach uses the current exact releases
`@hey-api/openapi-ts@0.99.0` and `@hey-api/client-fetch@0.13.1`. The SDK plugin is
configured as `@hey-api/sdk` in the generator configuration. Because the
generator is pre-1.0, every codegen/runtime package is exact-pinned and upgrades
are explicit.

- Input: repository `contracts/openapi/v1.json`.
- Output: `apps/web/src/lib/api/generated/`.
- Output: generated types, Fetch runtime and flat tree-shakeable SDK functions.
- Generated files are committed and carry generated-file headers.
- `api:generate` regenerates the output.
- `api:check` regenerates and fails when the output has a Git diff.
- Lint/format configuration does not rewrite generated files.
- Application data access imports generated SDK operations, not raw `fetch`.

The generated SDK remains the single contract for browser and server calls.

## 8. Browser and SSR data flow

### Browser

The browser factory is client-safe and uses:

- a relative API base;
- explicit `credentials: "same-origin"`;
- `cache: "no-store"` for status;
- no token storage and no JavaScript cookie access.

During dev/E2E, Next.js external rewrite is only a transport bridge:

```text
Browser → Next origin /api/** → API_PROXY_TARGET → ASP.NET Core
```

`API_PROXY_TARGET` is unset in the final production topology.

When the final single-origin topology exists:

```text
Browser → Kestrel /api/**
```

Next.js never defines an `/api/**` Route Handler and CORS remains disabled.

### SSR

The SSR factory:

- lives in a `server-only` module;
- requires an absolute `API_INTERNAL_BASE_URL` only when the runtime call occurs;
- creates a new generated client per request;
- uses `no-store` for request-bound calls;
- accepts a typed allowlist `{ cookie?: string; correlationId?: string }`;
- never accepts or forwards an arbitrary request header collection;
- never forwards `Authorization`;
- does not call Next.js `headers()` or `cookies()` itself.

Future authenticated callers must read dynamic request state outside cached
scopes and pass only the permitted values. The iteration-2 public SSR status call
passes neither cookie nor correlation ID.

```text
Next Server Component → API_INTERNAL_BASE_URL → ASP.NET Core
```

`API_INTERNAL_BASE_URL` and `API_PROXY_TARGET` are server-only. No API origin is
compiled into a `NEXT_PUBLIC_*` variable.

## 9. Cache Components, locale, theme and navigation

`cacheComponents` remains enabled. The status server component is under a
`Suspense` boundary and performs an uncached runtime request, so `next build`
does not require a running API.

Locale is deliberately static for a deployment because dynamic localization is
not used with the chosen Cache Components strategy:

- supported bundles are `en` and `ru`;
- `PUBLIC_DEFAULT_LOCALE` selects the deployment language;
- build and runtime use the same value; changing the language requires a new
  build/restart rather than request-time revalidation;
- missing or invalid values fall back to `en`, matching reference behavior;
- routes have no locale prefix;
- cookies, `Accept-Language` and user settings do not select locale;
- no language switcher is rendered.

Only foundation namespaces are copied. Product message bundles move with their
own later feature slices.

The theme provider preserves system/light/dark behavior. Before hydration the
switcher renders stable disabled markup with an accessible label. The minimal
header contains branding/root navigation and the theme switch only; product
links and authenticated navigation remain out of scope.

## 10. Status composition and error handling

The technical root page contains two independently identified status regions:

1. an async SSR status component calls the generated status operation
   (`operationId: GetSystemStatus`) with `echo=ssr`;
2. a Client Component calls the same generated operation after hydration with
   `echo=browser`.

Both show source, status, API version and timestamp. The browser status supports
retry and cancels an obsolete request on unmount/retry.

The shared failure adapter produces a discriminated result:

- `problem`: typed Problem Details with stable `code`, HTTP status and `traceId`;
- `network`: backend unavailable or transport failure;
- `configuration`: invalid/missing SSR internal base URL.

UI branches on `code`, not the invariant-English server title/detail. It renders
localized safe text and an optional `traceId`; raw exception messages, stack
traces and arbitrary backend details are never shown.

Expected SSR problem/network/configuration failures render inside the SSR status
region. They do not intentionally trip the route-wide error boundary.

Next boundaries have distinct responsibilities:

- route `loading` supplies an accessible skeleton/status;
- route `error` handles uncaught exceptions and exposes `reset()`;
- not-found uses foundation messages and returns home;
- global error is self-contained static fallback markup that does not require
  i18n or theme providers.

Status changes use an accessible status/`aria-live` region. Retry and theme
controls are keyboard accessible.

## 11. Test-first strategy

Minimal test-runner/bootstrap configuration may be established first, but every
functional behavior begins with a focused failing test. The implementation order
is:

1. locale resolution and typed root route;
2. generated-client existence/drift contract;
3. browser factory configuration;
4. request-scoped SSR factory and header isolation;
5. failure normalization;
6. theme and Next error/loading primitives;
7. SSR status loader;
8. browser loading/success/problem/network/retry behavior;
9. full-stack Playwright smoke.

### Unit/component expectations

- `en`, `ru`, missing and invalid locale behavior;
- stable pre-hydration theme markup and theme toggle;
- relative browser request with same-origin credentials;
- independent SSR clients do not leak cookies across requests;
- only cookie/correlation ID can cross the SSR allowlist;
- Problem Details maps by `code` and preserves `traceId`;
- network/configuration failures expose no internal data;
- loading, success, failure, cancellation and retry states;
- global error renders without providers.

### Contract and source guards

- generated SDK exposes a callable operation derived from
  `operationId: GetSystemStatus`;
- a second generation has no diff;
- generated output type-checks;
- no handwritten API response DTO exists;
- `apps/web` dependencies/source contain no Prisma or Better Auth;
- `apps/web/src` contains no `"use server"` directive or `/api/**` Route Handler;
- application data access outside generated runtime does not call raw `fetch`.

### Playwright harness

Playwright starts or reuses two loopback processes:

1. ASP.NET Core, ready when `/api/health` returns 200;
2. Next.js on `127.0.0.1:3127`, configured with both server-only API URLs.

The smoke suite proves:

- `/` renders without uncaught page errors or first-party 5xx responses;
- the SSR panel contains successful API v1 data and `echo=ssr`;
- the browser panel requests the UI origin and contains `echo=browser`;
- an intercepted Problem Details response creates a safe browser error state;
- removing the interception and selecting retry restores success;
- theme toggle is keyboard accessible.

## 12. Acceptance commands

Run required .NET verification from the repository root even though the API
contract does not change:

```bash
dotnet restore Template.sln
dotnet build Template.sln --no-restore
dotnet test Template.sln --no-restore
```

Run web verification from `apps/web`:

```bash
npm ci
npm run api:check
npm run lint
npm run typecheck
npm test -- --runInBand
npm run build
npm run e2e
test -f .next/standalone/server.js
```

Run repository/contract guards from the root:

```bash
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
git diff --exit-code -- contracts/openapi/v1.json
git diff --exit-code -- template/
git diff --check
```

The production web build is also run without a live API, proving that the SSR
status request is deferred to runtime.

## 13. Durable documentation and completion

Implementation creates `docs/web-conventions.md` for:

- browser/SSR API addressing and cookie forwarding;
- generated client ownership and drift workflow;
- fixed-locale Cache Components policy;
- UI failure and loading conventions;
- local/E2E rewrite ownership.

At completion, `docs/aspnetcore-migration-plan.md` records:

- iteration-2 scope and state;
- the reference/API/UI/test correspondence table;
- exact command output and test counts;
- known reference differences;
- unchanged `template/` evidence.

Known intentional differences from reference are the temporary technical home
page, generated ASP.NET REST access instead of Server Actions, RFC Problem
Details, no runtime locale switching, and absence of auth/product data.

The next gate remains iteration 3: PostgreSQL, EF Core, Identity,
register/login/logout/current-user, secure cookie issuance and antiforgery.
Product landing/application-shell parity stays in its registered later
iteration. The external Next rewrite remains dev/E2E-only until the production
Kestrel/YARP topology is implemented.

# Web application conventions

## Ownership boundary

`apps/web` is a Next.js UI and never owns `/api/**`, business logic, sessions,
database access, or external integrations. ASP.NET Core is the only API host.
The web application contains no Prisma, Better Auth, Server Actions, API Route
Handlers, direct database access, or browser bearer-token storage.

## Dependency integrity

Runtime dependencies contain only packages needed by the built application.
The exact-pinned `shadcn` CLI is a development dependency: it supplies build-time
CSS/component tooling but is not shipped as a production runtime dependency.
Both direct and development dependencies stay exact-pinned, and
`package-lock.json` is authoritative.

Security overrides are exact, narrowly justified compatibility bridges:

- `postcss` is held at `8.5.22` for every consumer because stable Next.js
  `16.2.11` otherwise installs vulnerable `8.4.31`;
- Next.js `sharp` is held at `0.35.3`;
- the two JavaScript YAML 4 consumers are held at `js-yaml` `4.3.0`;
- the shadcn MCP dependency is held at `@hono/node-server` `2.0.11`.

The initially reviewed Hono floor `2.0.5` is not used: the live registry audit
now reports a later advisory across `2.0.0`–`2.0.9`, so exact `2.0.11` is the
first currently audited stable release.

The override versions pass generation, CLI/MCP-HTTP transport, lint, typecheck,
Jest, production build, standalone-runtime, and full-stack E2E checks. Remove
an override only after an exact upstream dependency accepts an audited version
and the same matrix passes. `npm audit --json` checks the full tree;
`npm run audit:prod` is the required production-dependency gate.

## Generated REST contract

`contracts/openapi/v1.json` is the input to
`apps/web/openapi-ts.config.ts`. `npm run api:generate` writes the committed
`src/lib/api/generated` tree. Generated files are never hand-edited or formatted.
`npm run api:check` regenerates and byte-compares the entire tree.

Application data adapters call generated SDK operations and import generated
DTOs. They do not call raw `fetch` and do not redefine response or Problem
Details types. `npm run boundaries:check` enforces these rules across
`js`, `jsx`, `mjs`, `cjs`, `ts`, `tsx`, `mts`, and `cts` source and Route
Handler forms.

## Generated TypeScript metadata

Next.js owns and regenerates `next-env.d.ts`; it stays in the `tsconfig.json`
`include` list but is ignored and untracked. TypeScript incremental
`*.tsbuildinfo` files are also ignored. `next typegen`, `next dev`, and
`next build` may refresh these generated files without dirtying tracked source.

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
header collection and never forwards `Authorization`. Request-time auth loaders
forward only the incoming `Cookie` and correlation ID. Callers read request
state outside cached scopes and pass only explicitly permitted values. The
anonymous system-status probe passes no forwarded headers.

Uncached request-time calls use `cache: "no-store"`. With Cache Components,
runtime SSR work begins below `connection()` and a `Suspense` boundary so builds
do not require a live API and request configuration is not frozen at build time.
Login loads capabilities and session in parallel there; dashboard loads its
session there. An explicit anonymous session causes navigation between login
and dashboard, while network/configuration/Problem Details failures render the
safe API-failure state rather than being treated as anonymous.

Server-rendered session reads add
`X-Template-Session-Renewal: suppress` through the generated SDK. ASP.NET Core
still authenticates and projects the request, but it does not slide the
persistent ticket or emit an unusable renewal cookie during Server Component
rendering. This marker grants no access and is not forwarded to other API
operations.

## Authentication UI and mutations

Browser authentication mutations use generated SDK operations and always fetch
a fresh CSRF pair first with `GET /api/v1/auth/csrf`. The request token is sent
as `X-CSRF-TOKEN`; browser credentials remain `same-origin`. The current local
button and logout control follow this CSRF-first path. Automation-only
credential sign-in and cleanup use the same generated contract and CSRF rule.
After an authenticated dashboard renders, a minimal Client Component performs
an unmarked same-origin `getAuthSession` generated-SDK call. That browser-owned
request can receive the secure HttpOnly sliding-renewal cookie; JavaScript never
reads or copies the cookie.

Redirect targets are normalized to safe same-origin application paths. Full
URLs, protocol-relative `//` values, malformed/encoded escape forms, and
`/api/**` or `/auth/**` targets are rejected in favor of `/dashboard`.

`/auth/login` has no name, email, or password fields. When the API advertises
local automation, the page offers one **Create local automation user** button.
The scenario API returns plaintext generated credentials once for automation,
but the visible UI never renders or retains them and discards the response
before refreshing and navigating.

The iteration-3 `/dashboard` is only a protected session proof. It distinguishes
anonymous state from API failure, renders the safe user/session projection and
logout, and is not the product dashboard planned for iteration 9.

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
npm audit --json
npm run audit:prod
npm run api:check
npm run boundaries:check
npm run format:check
npm run lint
npm run typecheck
npm test -- --runInBand
npm run build
npm run e2e:install
npm run e2e
test -f .next/standalone/server.js
```

E2E starts ASP.NET Core on `127.0.0.1:5297` and Next.js on
`127.0.0.1:3127`. The API readiness probe is `/api/health/ready`.

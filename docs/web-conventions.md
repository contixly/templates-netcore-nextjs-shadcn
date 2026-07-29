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
and the same matrix passes.

After [`GHSA-mh99-v99m-4gvg`](https://github.com/advisories/GHSA-mh99-v99m-4gvg)
was published on 2026-07-23 and updated on 2026-07-24, `npm audit --json`
started reporting 26 high findings in the development-only ESLint/Jest graph.
The advisory's only patched line is
`brace-expansion` 5.0.8. Current stable Next/ESLint plugins and Jest still
declare older `minimatch`/`glob` ranges; replacing those transitive packages
with their major-10/5 APIs would break old CommonJS callable imports. Do not
silence the audit or force that incompatible override. Re-run the full audit
when stable upstream ranges move; it remains a known external tooling blocker,
not an accepted zero-finding result. `npm run audit:prod` remains the required
production-dependency gate and reports zero findings for this application.

## Generated REST contract

`contracts/openapi/v1.json` is the input to
`apps/web/openapi-ts.config.ts`. `npm run api:generate` writes the committed
`src/lib/api/generated` tree. Generated files are never hand-edited or formatted.
`npm run api:check` regenerates and byte-compares the entire tree.

OAuth protocol callbacks are intentionally absent from this REST contract.
Next.js starts a flow only through the generated
`challengeExternalAuth` operation and never parses an authorization code,
state, provider token, or callback payload.

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
created for each isolated credential context. The factory accepts only
`{ cookie?: string; correlationId?: string }`; it never accepts an arbitrary
header collection and never forwards `Authorization`. The combined login auth
loader gives its anonymous capabilities client only the correlation ID; its
separate session client receives the incoming `Cookie` and correlation ID.
The account, connection, and paged-session loaders use the same request-bound
cookie/correlation allow-list and isolated generated clients.
Callers read request state outside cached scopes and pass only explicitly
permitted values. The anonymous system-status probe passes no forwarded
headers.

Uncached request-time calls use `cache: "no-store"`. With Cache Components,
runtime SSR work begins below `connection()` and a `Suspense` boundary so builds
do not require a live API and request configuration is not frozen at build time.
Login loads capabilities and session in parallel there without placing the
cookie on the capabilities request; dashboard loads its session there. An
explicit anonymous session causes navigation between login and dashboard,
while network/configuration/Problem Details failures render the safe
API-failure state rather than being treated as anonymous.

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
External sign-in, provider disconnect, profile update, session revoke,
revoke-others, and account deletion use the same CSRF-first generated-SDK
pattern.
After an authenticated dashboard renders, a minimal Client Component performs
an unmarked same-origin `getAuthSession` generated-SDK call. That browser-owned
request can receive the secure HttpOnly sliding-renewal cookie. After a
successful read, it refreshes the current App Router route so the uncached
Server Component projects the now-current session timestamps; failed reads
leave the existing server-rendered state in place. JavaScript never reads or
copies the cookie.

Redirect targets are normalized to safe same-origin application paths. Full
URLs, protocol-relative `//` values, malformed/encoded escape forms, and
`/api/**` or `/auth/**` targets are rejected in favor of `/dashboard`.

`/auth/login` has no name, email, or password fields. When the API advertises
local automation, the page offers one **Create local automation user** button.
The scenario API returns plaintext generated credentials once for automation,
but the visible UI never renders or retains them and discards the response
before refreshing and navigating.

The same page renders one external-provider button for each provider advertised
by the API. A synchronous in-flight guard permits only one challenge at a time.
Navigation occurs only when the generated API returns an absolute,
credential-free HTTPS URL; it is a top-level navigation, not an embedded
callback handler. The browser validates URL shape but does not copy provider
host metadata from ASP.NET Core.

`/auth/error` maps only the callback's stable allow-listed `code` values to
localized copy. Unknown, repeated, or arbitrary query fields use generic copy
and are never echoed. No callback state, authorization code, access/refresh
token, provider subject, bearer value, or session secret is retained in
JavaScript memory or browser storage beyond the immediate generated request
inputs. Authentication remains the secure HttpOnly cookie only.

The iteration-3 `/dashboard` is only a protected session proof. It distinguishes
anonymous state from API failure, renders the safe user/session projection and
logout, and is not the product dashboard planned for iteration 9.

## Account settings UI

The protected `/user` shell is request-time server rendered below
`connection()`/`Suspense`. It forwards only the incoming cookie and correlation
id to isolated generated clients. Only an explicit anonymous projection
redirects to `/auth/login?redirect=%2Fuser%2Fprofile`; API, network,
configuration, or malformed-projection failures render a safe failure state
instead of being treated as anonymous. `/user` redirects to `/user/profile`.

Iteration 4 owns exactly four navigation destinations:

- `/user/profile`;
- `/user/connections`;
- `/user/security`;
- `/user/danger`.

Invitations, API Keys, organization settings, and the final product shell are
not implied by this navigation and remain in their planned iterations.

The profile page renders the provider-managed avatar, display name, canonical
read-only primary email, secondary verified emails, user id, and creation time.
The client trims and validates the 2–50 character name for feedback, but the API
is authoritative. The visible projection changes only after the REST mutation
returns its confirmed account response.

Connections render the server's configured-plus-connected union, including a
connection whose provider configuration was removed. Connect reuses the
external challenge adapter with `intent=connect`; disconnect uses the generated
account mutation and server-provided disabled reason. UI disabled state is
presentation only: the API again prevents removal of the current provider or
any removal that would leave no connected provider with complete runtime
configuration. Connected providers whose configuration was removed stay
visible but do not count as usable survivors. Disconnect revokes neither
provider consent nor remote tokens because no token is stored.

Security renders only safe session fields. Browser/OS text is best-effort
presentation derived from the bounded user-agent projection and is not an
authorization input. Pagination returns the opaque `nextCursor` verbatim,
appends and de-duplicates ids, and never decodes or constructs cursors. The
current session has no single-revoke control; revoke-others keeps the current
browser.

Danger requires the exact primary email after outer whitespace trimming.
During the destructive request a synchronous request-identity lock prevents
duplicate submits and dismissal. Only confirmed API success performs a full
top-level navigation to `/`; a failure leaves the dialog recoverable.

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

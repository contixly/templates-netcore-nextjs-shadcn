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

- `postcss` is held at `8.5.25` for every consumer because stable Next.js
  `16.2.11` otherwise installs a version now covered by the incomplete
  source-map-fix advisory;
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

Server-rendered cookie-bearing session and account projection reads add
`X-Template-Session-Renewal: suppress` through the generated SDK. ASP.NET Core
still authenticates and projects the request, but it does not slide the
persistent ticket or emit an unusable renewal cookie during Server Component
rendering. This marker grants no access, affects only safe `GET` requests, and
is added explicitly only by the relevant server adapters.

## Authentication UI and mutations

Browser authentication mutations use generated SDK operations and always fetch
a fresh CSRF pair first with `GET /api/v1/auth/csrf`. The request token is sent
as `X-CSRF-TOKEN`; browser credentials remain `same-origin`. The current local
button and logout control follow this CSRF-first path. Automation-only
credential sign-in and cleanup use the same generated contract and CSRF rule.
External sign-in, provider disconnect, profile update, session revoke,
revoke-others, and account deletion use the same CSRF-first generated-SDK
pattern.
After the shared site-header server guard confirms one well-formed authenticated
projection, it mounts exactly one minimal Client Component beside the account
navigation. That component performs one unmarked same-origin `getAuthSession`
generated-SDK call for protected `/welcome`, `/workspaces`, `/w/**`, and
`/user/**` surfaces. The browser-owned request can receive the secure HttpOnly
sliding-renewal cookie; a successful read refreshes the current App Router route
so uncached Server Components project the now-current session, while a failed
read leaves the existing projection in place. Document-local pathname-cycle
state prevents concurrent mounts and the successful `router.refresh()`
same-path remount from issuing a second request in the current cycle. Each later
App Router soft navigation to a different protected pathname starts a new
unmarked renewal in the same document. A failed read releases its pathname
cycle so a later remount or navigation may retry; a stale request cannot clear
or refresh a newer pathname cycle. The transient `/dashboard` resolver clears
the prior cycle and defers renewal to its final `/welcome` or `/w/**`
destination, avoiding two reads for one resolver navigation. Anonymous, failed,
and malformed server projections mount neither account navigation nor renewal.
Individual dashboard/settings pages must not add duplicate refresh components.
JavaScript never reads or copies the cookie.

Redirect targets are normalized to safe same-origin application paths. Full
URLs, protocol-relative `//` values, malformed escapes, repeated encoded
separator confusion in the pathname, and `/api/**` or `/auth/**` targets are
rejected in favor of `/dashboard`. Encoded `/` and `%` remain valid query or
fragment data, while literal or repeatedly encoded controls and backslashes are
rejected anywhere in the target.

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

The site header streams its account entry below a request-time
`connection()`/`Suspense` boundary and shows it only for a confirmed
authenticated projection; anonymous, failed, or malformed projections fail
closed. This visible entry is the supported path from workspace UI into the
protected account shell and its logout control. Its cached SSR session read
suppresses sliding renewal, and the same confirmed header guard owns the single
browser renewal described above.

The protected `/user` shell is request-time server rendered below
`connection()`/`Suspense`. It forwards only the incoming cookie and correlation
id to isolated generated clients. Only an explicit anonymous projection
redirects to `/auth/login?redirect=%2Fuser%2Fprofile`; API, network,
configuration, or malformed-projection failures render a safe failure state
instead of being treated as anonymous. Its Suspense fallback comes from the
fixed deployment-locale message catalogue. `/user` redirects to
`/user/profile`.

Iteration 4 owns exactly four navigation destinations:

- `/user/profile`;
- `/user/connections`;
- `/user/security`;
- `/user/danger`.

Invitations, API Keys, organization settings, and the final product shell are
not implied by this navigation and remain in their planned iterations.

The profile page renders the provider-managed avatar, display name, canonical
read-only primary email, secondary verified emails, user id, and creation time.
The client trims and validates the 2–50 UTF-16-code-unit name with JavaScript
`string.length`, matching .NET `string.Length`, but the API is authoritative.
The visible projection changes only after the REST mutation returns its
confirmed account response.

Connections render the server's configured-plus-connected union, including a
connection whose provider configuration was removed. Connect reuses the
external challenge adapter with `intent=connect`; disconnect uses the generated
account mutation and server-provided disabled reason. UI disabled state is
presentation only: the API again prevents removal of the current provider or
any removal that would leave no connected provider with complete runtime
configuration. Connected providers whose configuration was removed stay
visible but do not count as usable survivors. Disconnect revokes neither
provider consent nor remote tokens because no token is stored. After a
successful disconnect, the browser reloads and replaces the complete
connections projection through the generated SDK so survivor-dependent
disconnect permissions are authoritative. The confirmed DELETE is never
reported as failed merely because that reload fails: the client first applies a
conservative disconnected projection, recomputes survivor permissions with the
server's current-method/configured-survivor rules, and offers an explicit
generated-SDK list-refresh retry without repeating the DELETE.

Security renders only safe session fields. Browser/OS text is best-effort
presentation derived from the bounded user-agent projection and is not an
authorization input. Pagination returns the opaque `nextCursor` verbatim,
appends and de-duplicates ids, and never decodes or constructs cursors. The
current session has no single-revoke control; revoke-others keeps the current
browser. After revoke-others succeeds, the browser reloads and replaces the
fresh first page through the generated SDK, so the current session remains
visible even when it was not in the previously loaded pages. If that reload
fails, stale revoked rows and the normal empty state stay hidden; a distinct
partial-success state retries page one without repeating revoke-others.

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

Each interactive Client Component boundary keeps its exact first-action control
disabled until that boundary has hydrated, then publishes
`data-interaction-ready="true"` (organization shell controls use their
organization-specific readiness attribute). Playwright waits for that exact
control's readiness attribute and enabled state before clicking or typing. The
root `data-app-hydrated` marker is not proof that descendant Client Component
boundaries are interactive and must not be used to justify an interaction.

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

## Organization-aware UI (iteration 5)

Organization loaders and member loaders are generated-SDK-only SSR adapters.
Like other cookie-bearing projections, they forward only the allow-listed
cookie/correlation context and add `X-Template-Session-Renewal: suppress`; the
browser's normal unmarked session read remains responsible for any sliding
renewal. Browser organization mutations first obtain CSRF and use the generated
SDK; raw organization `fetch` calls and hand-written DTOs are prohibited.

`/dashboard` resolves the active accessible organization, otherwise the first
organization in server ordering, otherwise redirects to `/welcome`. `/welcome`
renders first-workspace onboarding only for zero accessible organizations;
otherwise it redirects through `/dashboard`. `/workspaces` server-renders its
authoritative first page at the canonical URL. Its explicit load-more button uses
the generated organizations GET operation in the browser and the returned opaque
cursor without changing the URL; refresh/new-tab navigation starts again at page
one, and old cursor bookmarks redirect to `/workspaces`.
`/w/{organizationKey}` accepts a UUID or non-UUID slug;
both the workspace root and a direct
`/w/{nonCanonicalOrganizationKey}/dashboard` request redirect to
`/w/{canonicalSlug}/dashboard` after successful lookup. A deep-link read never
changes active context. A missing key sends a zero-organization user to
onboarding and a user with another accessible organization to `forbidden()`, so
the API's non-disclosure result keeps the intended protected UI distinction.

Workspace settings are `/w/{key}/settings/{workspace,users,roles}`. They show
only the fixed organization roles and omit Teams, Invitations, and API Keys.
The header's route-owned parallel switcher slot calls set-active before routing;
it preserves known one-key settings routes and otherwise goes to the selected
dashboard. It may skip set-active for the routed selection only when that id also
equals the session's active preference, so an explicit deep-linked selection
persists for later `/dashboard`. The independently loaded routed detail is
authoritative for its organization id: it replaces a same-id first-page summary
without changing list order, or is prepended when that id is absent. A slug
update replaces the browser URL with the returned canonical key.

The switcher treats permanent attachment, React Activity visibility, and the
exact committed pathname generation as separate lifetimes. A selection captures
its pathname generation before set-active transport. Actual deletion makes its
continuation inert before local or router effects. An attached hidden completion
settles the request lock and local state but never replays the old push; a hidden
success queues only one refresh, drained on reveal only when that exact pathname
generation is still current. Any intervening pathname change, including a
change away and back while the parallel slot remains mounted or renders `null`,
permanently discards old-path navigation and queued reconciliation. Failure can
remain safely retryable on a preserved instance without displaying raw API
data. A visible same-generation success keeps one suffix-preserving canonical
push followed by one refresh, including after StrictMode lifecycle replay.
Queued refresh protection is defense in depth: each committed pathname change
eagerly discards the queue, and reveal also verifies the queued origin generation
before draining it.

Lists return opaque server cursors unchanged, explicitly load more, and
de-duplicate ids. Workspace browser accumulation keeps prior pages and the last
cursor on a safe failure, retries the same GET, and applies every incoming
duplicate over its older entry while preserving local tail entries and confirmed
deletion tombstones. The load-more control uses its organization-boundary
hydration readiness marker.

Organization creation uses two lifecycle signals. Insertion cleanup marks
permanent deletion, making any later transport completion inert. Layout cleanup
records React Activity hiding without destroying the instance: a hidden
completion settles local request/pending state, suppresses stale push, and on
success queues only one refresh for the originating surface. Reveal drains that
refresh exactly once. Visible completion still performs canonical push plus
refresh, including under StrictMode replay.

Workspace settings reject exact normalized D-format UUID-shaped slugs before
transport. They compare normalized inputs with the latest confirmed detail and
send the exact generated PATCH request containing only dirty name, slug, and/or
allowed-domain fields. Allowed domains have set semantics: normalized,
de-duplicated values are compared without regard to order, so reordering alone
neither enables Save nor adds a stale domain collection to an unrelated PATCH.
The settings form applies the contract maximum after this normalization and
de-duplication: 101 distinct valid domains produce a localized field error and
no transport, while more than 100 raw comma/newline tokens that collapse to at
most 100 distinct generated-array items remain valid. The API independently
retains its authoritative raw JSON `maxItems: 100` boundary. A real set addition
or removal remains dirty. A normalized no-change form keeps Save disabled; a
successful response replaces both visible inputs and the dirty comparison
baseline, preventing stale administrators from overwriting unrelated fields. On
a later RSC projection, update capability is taken immediately from the latest
server prop so demotion disables every field, removes Save, and blocks forced
submit without overwriting a local draft or its latest mutation-confirmed
comparison baseline. The server workspace-settings page keys this local form
boundary by the resolved immutable organization id, never the mutable slug or
pathname. Therefore a slug reused by another organization remounts all local
baseline/input/feedback/pending state before that replacement can submit, while
a fresh projection for the same id retains the mounted dirty/confirmed state and
continues to reconcile capability from the latest prop. Each form owns separate
attachment and visibility lifecycles. An insertion-effect cleanup marks the
instance permanently detached during an actual keyed deletion, before
replacement layout work; a completed mutation checks that marker immediately
after transport and before any ref/state write or router side effect. Thus a
late success from replaced A cannot navigate or refresh mounted B. React
Activity preservation is different: hiding cleans layout effects and detaches
host refs but retains state, refs, and the insertion-effect lifetime. A separate
layout marker therefore records visibility. An attached hidden completion still
clears its request lock and pending state and reconciles failure or the confirmed
success baseline, but queues canonical replace/refresh without invoking either
global router effect. Layout setup on reveal flushes that queue exactly once;
ordinary visible and StrictMode-replayed forms keep normal completion behavior.

Workspace deletion uses the same two lifetimes. Insertion cleanup permanently
invalidates a keyed dialog and discards any queued router work. Layout cleanup
only marks the attached dialog Activity-hidden. A hidden success clears its
request lock, closes local dialog state, and invokes a still-live `onDeleted`
reconciliation, but does not replace or refresh globally. Because the deleted
settings resource cannot remain the revealed destination, it queues the
required `/workspaces` replacement plus refresh and drains both exactly once on
reveal; repeated hide/reveal does not replay them. Permanent deletion before
reveal discards the queue. Hidden failure remains local and retryable without
navigation, while visible and StrictMode success keeps immediate replace plus
refresh. The existing immutable-id key still makes different-organization late
completion inert.

Mutation responses are immediately authoritative. A successful write followed
by a failed refresh remains a confirmed partial success: the UI keeps a
conservative projection and offers a GET-only refresh retry, never repeats the
mutation. Direct member add exposes an outside-domain confirmation only for the
typed 409 acknowledgement problem; the initial request has no write. Its
generated `emailDomain` metadata remains `string | null`: explicit null renders
the fixed localized unknown-domain fallback and still permits the one confirmed
retry, while an omitted, blank, or wrong-typed value fails closed. Directory
state also keeps the current actor separate and never renders member removal
controls. A later RSC `initialPage` immediately replaces only member-directory
server page zero; locally loaded continuation pages and their final cursor,
confirmed mutation overlays/order, active generated reads, feedback, and
GET-only recovery survive. The RSC projection has no read generation and cannot
retire an overlay; a causally later successful generated member GET remains the
only authority that can retire it. Member-directory continuation is an exact
first-action organization control: its server HTML is disabled, and it becomes
enabled only after that Client Component boundary publishes
`data-organization-control-interaction-ready="true"`.

Activity cleanup of a pending directory load-more or GET-only recovery read
aborts/supersedes the coordinator and dispatches a matching read-generation
cancellation. The reducer clears only that `activeRead`, preserves confirmed
state/recovery, and produces no failure feedback, so reveal exposes a usable
continuation/retry. A newer read cannot be cleared by older cleanup, and actual
keyed deletion remains reducer-inert while its insertion cleanup still aborts
any read created after the earlier hide.

The account-deletion dialog keeps generic safe copy for unknown failures, but
maps the exact typed `organization_ownership_transfer_required` Problem Details
code to localized guidance to promote another owner or share ownership before
retrying. A safe trace id remains visible when supplied; the API result remains
authoritative.

## Collaboration UI (iteration 6)

Collaboration route values are centralized and percent-encode every dynamic
segment:

- `/w/{organizationKey}/settings/teams`;
- `/w/{organizationKey}/settings/invitations`;
- `/user/invitations`;
- `/invite/{invitationId}`.

There are no Next Route Handlers, Server Actions, Prisma/Better Auth paths,
handwritten transport DTOs, raw collaboration `fetch` calls, bearer storage, or
direct database access. All server reads and browser mutations use the committed
generated REST SDK.

### Loading and navigation

Team/member/candidate, organization/account invitation, and invitation-decision
Server Component loaders call generated operations through
`createServerApiClient`. They forward only the allow-listed cookie and
correlation id, set `X-Template-Session-Renewal: suppress`, use `no-store`, and
return normalized `ApiResult<T>` values. They never forward `Authorization` or
arbitrary incoming headers. The shared protected client boundary performs the
visible browser renewal instead.

Workspace settings always exposes Teams. Invitations is exposed only when the
current server projection says `canManageInvitations`; account navigation always
exposes Invitations, and zero-workspace onboarding offers Review Invitations
without making account routes organization-dependent. The workspace switcher
preserves the Teams suffix. It preserves the Invitations suffix only when the
target organization's compact server projection grants
`canManageInvitations`; otherwise it routes directly to that target's canonical
dashboard, without an intermediate private settings redirect.

Both settings pages resolve session and organization detail before feature data.
A successfully resolved noncanonical key redirects to the returned canonical
slug. The invitation settings page applies `canManageInvitations` before
canonical redirect or activity/team reads, so a denied member receives the
existing protected forbidden page without private canonical-key or activity
disclosure. Team settings is readable by every organization member.

### Teams settings

`/w/{organizationKey}/settings/teams` server-loads the first team page with a
limit of 20 and keys its client directory by immutable organization ID. Each
team renders the API's embedded first member page; dedicated generated member
reads continue from its cursor. Members see team names, counts, roles, and
composition plus a read-only notice. Administrators and owners additionally see
create, rename, confirmed delete, candidate search/page/add, and remove
controls. No active-team state, control, or copy exists.

Create and rename validate the normalized name with the API's `1..50` Unicode
scalar contract. The browser counts code points rather than UTF-16 code units,
so exactly 50 supplementary-plane letters are valid while 51 are rejected; the
Unicode property pattern continues to reject unsupported characters and
malformed surrogate input.

Team continuation appends without discarding confirmed pages and retries the
same opaque cursor after a safe read failure. Member continuation starts from
each embedded page and uses limit 50. Candidate search trims/caps the query at
100 characters, requests 20 at a time, preserves the query through continuation,
and invalidates stale/in-flight results when the dialog closes or the search
changes. Duplicate IDs are reconciled by immutable identity.

Mutation responses become local authority before a recovery read. Create,
rename, delete, add, and remove remain visibly committed when a subsequent
refresh fails; Retry performs only a generated GET and never resends the
mutation. Member reads capture both request and mutation generations. A read
made stale by a later confirmed add/remove is discarded, cannot resurrect an
older member projection, and queues exactly one latest replacement recovery
while another read is active. The post-read mutation overlay is likewise applied
only while its confirming mutation generation is still current, so an
interleaved newer confirmation remains local authority even if its recovery
fails. React Activity/different-resource guards prevent a hidden, unmounted, or
replaced component's late completion from updating state or navigating.
Confirmed create, rename, and add overlays retire only when a causally newer
browser read returns the same immutable row (and the confirmed name for rename).
Delete absence is authoritative only on an exhaustive replacement first page.
Remove absence is authoritative on an exhaustive generated first-page
replacement or on the terminal page of a complete de-duplicated traversal that
began with such a replacement; an embedded/RSC cursor or tail-only continuation
never establishes that coverage. An incoming RSC page cannot acknowledge an
overlay directly; it advances the read generation and queues a current browser
GET while the local authority remains projected.

Confirmed member-count projections are generation-stamped. Stale RSC data
cannot acknowledge or erase a saved add/remove count and instead queues a
generated team recovery. A causally newer generated team page containing the
same immutable team retires the count overlay and rebases to that projection,
including concurrent membership changes. A complete generated member traversal
is also authoritative: its de-duplicated size reconciles the count, advances the
team-projection generation, and remains projected as generation-stamped local
authority. A delayed RSC payload cannot erase that traversal count and queues a
generated team recovery instead. Any overlapping team GET that began before the
traversal became authoritative is discarded and queues one current replacement
GET; only a later team GET containing the immutable team may retire/rebase the
count. This cross-resource generation boundary prevents an older team or RSC
projection from regressing a newer member count without treating RSC or partial
pagination as authority.
Named regions/dialogs, explicit labels/descriptions, bounded fields, disabled
pending states, focus behavior, and team/person-specific accessible names are
part of the component contract.

### Invitation activity and creation

`/w/{organizationKey}/settings/invitations` server-loads invitation activity at
limit 20 and exhausts the generated team collection in 100-row opaque-cursor
pages for the selector, without an overall team-count cap. Every team page uses
the request-bound generated SDK loader with `no-store` and suppressed session
renewal. Duplicate immutable team IDs are reconciled without duplicate options;
a continuation failure, empty cursor, or cursor cycle fails the page safely.
Admin/owner users can filter by pending, accepted, rejected, canceled, or
expired, load opaque-cursor continuations, and create workspace-only or
team-targeted invitations. Admins may assign member/admin; owners may also
assign owner. A new filter is a transactional first-page replacement: it clears
prior-filter items/cursor,
retains the requested filter on failure for GET-only Retry, and ignores stale
overlapping responses. A continuation failure preserves visible activity.
Create confirmation is also a read-generation boundary. The confirmed pending
row is overlaid only in `all` and `pending`, never in accepted/rejected/canceled/
expired. A relevant read that started before confirmation cannot replace that
row or its cursor state; it is discarded and queues one latest first-page GET.
The mutation projection remains visible if that reconciliation is delayed,
fails, or has not yet observed the committed row, and Retry/reconciliation stay
GET-only so the browser never encourages a duplicate create.
Only rows actually returned by the server acknowledge an overlay, by immutable
invitation id. The local accumulated list is never acknowledgement input, so an
unrelated continuation cannot clear a confirmed row. An RSC `initialPage` that
does not contain every outstanding confirmed id retains those overlays and
queues one current all-filter GET; an RSC/server row containing an exact id may
retire only that overlay. Multiple confirmed creates therefore remain causal
independently until each is observed by the server.

The browser trims and lowercases the email, rejects non-ASCII or control
characters, performs a basic structural check, and validates the selected
generated team before mutation. The API remains authoritative for email
structure, domain, duplicate, membership, permission, and role rules. A
successful `InvitationResponse` is committed even if refresh, clipboard, or link
projection fails; the form cannot repeat the POST. Every success says that no
email was sent and requires manual sharing. Exact `notification_failed` adds one
fixed warning; unknown warning/provider detail is ignored.

The browser resolves `invitationPath` against `window.location.origin` and shows
or copies it only when it is credential-free, query/hash-free, same-origin, and
exactly the encoded `/invite/{invitationId}` path for the returned ID. An unsafe
path is never shown/copied, but the committed invitation remains visible with
manual recovery guidance. Clipboard errors are mapped to fixed localized copy
and neither links nor clipboard/provider text enter logs or browser storage.

### Account list and invitation decisions

`/user/invitations` loads only the account invitation collection (limit 20), not
organization detail. It shows unexpired pending invitations for the current
primary email and inaccessible organizations, with workspace/team/role/expiry,
exact decision links, pagination, an empty state, and safe partial-failure
recovery. Zero-workspace `/welcome` reuses that collection while suppressing its
empty card; a supplemental invitation read failure never blocks onboarding.
Every server `initialPage` replacement advances the account-list request
generation, resets pending/failure state and adopts the new items/cursor. A
continuation from the prior generation cannot append items, replace the new
cursor, or clear a newer pending state from its `finally` path.

`/invite/{invitationId}` maps the server/typed failure state as follows:

| State | UI behavior |
| --- | --- |
| `pending` | show details; accept/reject only when verified and `canRespond` |
| `accepted`, `rejected`, `canceled`, `expired` | terminal read-only state |
| `recipient-mismatch` | generic forbidden copy; purge/hide all invitation details |
| `email-verification-required` | verification guidance; local confirmation only when the server advertises local automation |
| `domain-restricted` | read-only policy state |
| `already-member` | read-only state plus canonical workspace link |

Anonymous decision access returns to login with the exact encoded invitation
path. Accept commits an accepted/non-actionable local projection before routing
to the encoded canonical organization dashboard. If navigation throws or is
delayed, it never repeats POST. Reject commits rejected locally, then performs a
GET reconciliation; failed or still-actionable reconciliation remains
non-actionable and Retry is GET-only. `invitation_not_pending` similarly disables
mutations before reconciliation. A recipient-mismatch response or failure
immediately purges all prior workspace/email/team/inviter details and makes late
mutation completions inert.
Exact `invitation_recipient_already_member` from either accept or reject
projects the current matching invitation as terminal `already-member` with
`canRespond: false`. It retains only that already-safe invitation projection and
its encoded canonical workspace link, never uses the recipient-mismatch
projection, and never repeats the decision POST.
A confirmed accepted, rejected, or already-member projection survives an
incoming same-invitation pending RSC payload, which cannot prove that it observed
the mutation. A terminal same-id server projection or causally newer browser GET
may replace it with authoritative state; a different/missing invitation or
recipient mismatch clears the overlay and its details.

Every browser collaboration mutation obtains a fresh CSRF value through
`runCsrfMutation` before the generated operation. Local email confirmation uses
the generated `confirmLocalAutomationEmail` operation, only in the advertised
unverified local-automation state, and reloads eligibility after the renewed
session response. E2E never mutates email verification directly in PostgreSQL.

All known stable collaboration codes map to fixed shape-identical en/ru catalog
copy. Unknown, network, thrown, and malformed-success cases use generic safe
copy. Components may inspect only normalized `kind`, `code`, `status`, and safe
`traceId`; raw Problem Details `detail`, exception messages, clipboard errors,
email/link text, and notifier/provider detail are never rendered.

## API-key management UI (iteration 7)

The browser management routes are `/user/api-keys` and
`/w/{organizationKey}/settings/api-keys`. Both use the committed generated REST
SDK, typed generated DTOs and existing protected server/browser client
boundaries; they contain no raw `fetch`, Server Action, Prisma/Better Auth,
direct database access, bearer storage or handwritten transport DTO.

The personal route is available to every authenticated user. The organization
route resolves session and trusted organization detail, canonicalizes the key
before the API-key list call, uses the trusted organization UUID, and requires
trusted `canManageApiKeys` before rendering any management boundary. A direct
member route or an unexpected successful list response with that capability
false renders the same safe authorization failure: no row, secret reveal,
mutation controls or payload becomes visible. Navigation exposes the
organization entry only when the trusted capability is true.

One shared, owner-keyed management surface renders safe metadata, opaque
pagination, create/edit/enable-disable, rotate/reveal and revoke. An immutable
owner key isolates personal and organization local state. UI convenience
defaults (personal `basic-read`, organization `organization-read-all`, 30 days,
1,000/hour) do not relax the explicit API request contract. The UI submits
preset IDs only, never raw scopes.

A create/rotate raw credential lives only in the reveal component's transient
state. It is copied by an explicit user action and cleared on close/unmount;
background reconciliation cannot erase a confirmed mutation or preserve the
secret. No credential enters URL/cookie/local/session storage, telemetry, error
copy, screenshot/video/trace artifact or test output. A failed post-mutation
refresh is a committed partial success: retain the safe local projection and
offer a generated GET-only reconciliation, never replay the mutation.

Pagination passes opaque cursor values verbatim, de-duplicates by immutable key
ID and rejects stale completions. Confirmed create/update/rotate/revoke results
remain authoritative over older reads. Every accepted fresh first page starts
an authoritative traversal and every accepted continuation appends to that
traversal; immutable IDs are de-duplicated with the first accepted occurrence
winning, and stale or failed reads do not acknowledge overlays. A non-revocation
overlay retires only when the same owner/key row has a valid RFC 3339 `updatedAt`
at or after the confirmed value.
Comparison preserves up to nine fractional-second digits, so an older
sub-millisecond value or malformed calendar timestamp fails closed. A revoked
null overlay remains through nonterminal pages and through a completed traversal
that still contains the stale row; it retires only when the key is absent from
the complete accepted traversal.

The generated SDK represents mixed cookie-or-key security as multiple security
descriptors. Generation therefore runs the deterministic auth-hardening step:
a nonempty scalar `auth` value fails locally on such an operation before fetch,
while a scheme-selective callback or explicit `x-api-key` header remains valid.
Scalar auth remains supported on a single-scheme machine operation. This guard
is generation-owned and `npm run api:check` verifies it with the rest of the
generated tree.

Secret-bearing API-key Playwright scenarios disable trace, video and screenshot
capture. Task 11 acceptance separately performed a one-off scan of captured
stdout and artifacts without printing a matching secret. The standard
`npm run e2e` script does not automate that scan and must not be cited as though
it does.

## Public documentation UI (iteration 8)

The public documentation source is target-owned Markdown/MDX under
`apps/web/src/features/documents/content`. The deterministic compiler emits two
committed artifacts: the exact static Next.js registry/import map under
`src/features/documents/generated` and the runtime-neutral API index at
`contracts/documents/search-index.json`. Rendering reads only the generated web
registry. The API embeds the neutral index and never reads frontend source at
runtime. Generated files are changed only through `npm run content:generate`;
`npm run content:check` byte-compares a fresh in-memory result.

`/docs` and `/docs/{**slug}` use the dedicated public `(documents)` route group.
They do not load the protected application shell, session, account navigation,
organization state, Prisma, Better Auth, or Server Actions. Public URLs have no
locale suffix or locale prefix. The validated fixed deployment locale selects
the `en` or `ru` registry variant; production-visible canonical pages require
both. Static rendering, navigation, metadata, sitemap, and article content need
no live API call.

`/docs/index` permanently redirects with HTTP 308 to canonical `/docs`; page
and metadata resolution retain the same guard before registry lookup.
`APP_PUBLIC_ORIGIN` supplies the absolute public origin for canonical metadata,
social image URLs, and the sitemap. Local development safely defaults to
`http://localhost:3000`, but production must set its externally reachable HTTPS
origin explicitly.

Next.js 16.2.11 Cache Components remain enabled. In that mode
`dynamicParams` is unavailable, so documentation routes do not export
`dynamicParams = false`. `generateStaticParams()` still enumerates every
production-visible canonical slug, while both `generateMetadata` and the page
perform an exact generated-registry lookup and call `notFound()` for an unknown
or unpublished slug. Because Cache Components can commit the streaming shell
before that lookup finishes, the observed unknown-page transport is HTTP `200`
with the framework's not-found UI and a `noindex` robots marker, not a transport
404. This is a known framework-compatible presentation difference. The
presentation-only OG handler performs its lookup before returning and preserves
a real empty-body `404` for an unknown image.

The article layer supplies localized breadcrumb/sidebar, previous/next links,
`h2` table of contents and scroll spy, safe fragments, copy controls, status and
fallback markers, GFM, and the closed MDX component map. Exact authoring rules
and commands are in [`documentation-authoring.md`](documentation-authoring.md).

Browser search is the only documentation UI path that calls the API. It resolves
the deployment locale explicitly, limits the input to the REST contract,
debounces by 250 ms, aborts superseded requests, and prevents stale completion
from replacing newer state. The adapter uses the generated
`searchDocumentsSystem` operation with the shared same-origin browser client.
It does not use raw `fetch`, redefine transport types, expose arbitrary Problem
Details text, retain credentials, or use browser storage.

Next Route Handlers remain forbidden below `/api/**`. The sole explicit
boundary-checker exception is
`src/app/(documents)/docs/og/[...slug]/route.ts`, which produces the established
presentation-only `/docs/og/{**slug}` PNG surface. It is not an API/BFF and does
not establish permission for other Route Handlers. Standard docs Open Graph and
Twitter images use Next metadata file conventions; the sitemap contains only
deduplicated production-visible canonical routes.

## Application shell and product presentation (iteration 9)

Iteration 9 is UI-only composition over the REST contracts delivered in
iterations 4–8. Route groups separate the public landing page, simple
authentication pages, public documents, and protected product routes without
changing their URLs. `/docs/**` keeps its documents shell. Protected pages use
the `(protected)` layout and a route-aware `@applicationNavigation` parallel
slot; every protected leaf supplies its exact safe login return path and, for a
workspace route, its organization key.

The navigation slot calls one composite shell loader after authentication. That
function composes request-cached constituent generated-SDK projections for the
session, account, organization page, and optional current organization; the
composite function itself is not described as React-cached. Equivalent page and
shell reads reuse those cached constituent loaders. SSR reads remain `no-store`
and suppress sliding renewal; exactly one `BrowserSessionRefresh` performs the
visible renewal for a protected navigation. Feature pages still own their
existing reads and mutations, so the shell is a presentation projection rather
than a business service or replacement transport model.

This implemented composition intentionally supersedes the proposed composite
React-cached loader in design spec §6.2. The single route-aware navigation slot
owns one composite invocation for a protected leaf, while the constituent
session, account, organization-list, and current-organization loaders are
request-cached. Those constituent caches deduplicate equivalent upstream
projections shared with page composition, so caching the composite function is
neither implemented nor required for request-level deduplication.

The responsive shell owns exactly one `main#main-content`, its sidebar/header
scroll frame, breadcrumbs, documentation shortcut, organization controls, and
account controls. Protected loading, error, not-found, unauthorized and
forbidden descendants render semantic sections inside that landmark rather
than introducing nested mains; standalone root status surfaces retain their
own main landmark. Source-inventory and rendered-integration tests guard the
single skip target and single-main invariant. Navigation visibility is derived
from trusted API capabilities, but hidden links never replace API authorization
or non-disclosure. The non-sensitive sidebar preference uses the dedicated
`template.sidebar=open|closed` cookie with `Path=/`, `SameSite=Lax`, and a
30-day maximum age. It is separate from the secure HttpOnly authentication
cookie and contains no credential or identity state.

Account and workspace settings reuse shared semantic page/rail/section
composition without changing generated-SDK operations, CSRF behavior,
authorization, canonicalization, validation, filtering, pagination, or causal
mutation reconciliation. The static interactive dashboard uses frozen,
target-owned fixture data and component-local state for chart range, tabs,
filtering, sorting, selection, column visibility, pagination, reordering, and
drawer edits. Navigation or reload resets that state. The static fixture and
local interactions introduce no additional dashboard data request or new
endpoint; the existing authentication, access, and organization projections
still run. All user-facing feedback explicitly says that demo changes are not
saved. Table selection totals are derived from filtered table row models, so a
selected row hidden by the current search/filter does not remain in the visible
selection count. Row reordering uses dnd-kit's vertical-axis modifier and is
covered by deterministic keyboard drag-and-drop; browser parity also exercises
search/filtering, hidden selection, pagination, column visibility, drawer
save/toast feedback, and reload reset without coordinate-dependent gestures.

Locale remains deployment-fixed `en | ru` with the existing safe English
fallback, fixed UTC time zone, no locale URL prefix, and no language switcher.
Application and dashboard message catalogs have paired shapes. Product metadata
uses the closed route catalog and `APP_PUBLIC_ORIGIN`; dynamic protected metadata
is generic and never loads user or organization data. Authentication and
protected routes emit `noindex,nofollow` with no canonical or Open Graph URL.
The sitemap contains only the public landing page and the documentation corpus.

Loading, not-found, unauthorized, forbidden, route-error, and global-error
surfaces use fixed localized copy. They may expose only a safe trace identifier,
never raw Problem Details detail, exception/provider text, route/query/body or
cursor values, cookies, credentials, or protected names. The global error page
does not depend on the application or i18n providers.

There is no new iteration-9 API endpoint, OpenAPI operation, handwritten
transport DTO, EF model or migration, table, index, seed, transaction, audit
event, cache invalidation event, or background job. ASP.NET Core still owns
`/api/**`, authorization, validation, persistence, and business rules. Browser
authentication remains the secure same-origin HttpOnly session cookie; unsafe
operations retain the antiforgery cookie plus `X-CSRF-TOKEN` flow. Existing
Problem Details, opaque-cursor, feature-filter, transaction, and schema behavior
is unchanged, and `npm run api:check` guards the generated client against the
unchanged OpenAPI contract.

The browser-storage source boundary rejects direct, aliased, destructured,
bound, `.call`, and `.apply` forms of `localStorage.setItem` and
`sessionStorage.setItem`, including bound-apply combinations. A
`setItem.apply.bind` capability retains arguments supplied after `.bind`'s
receiver and combines them with later invocation arguments, so both partially
and fully pre-bound sensitive application arrays are rejected through direct,
aliased and destructured paths. The analysis remains scope-aware so shadowed
`Map`-like objects do not become false positives. The dedicated
safe-preferences module is the only narrow persistence exception and continues
to store only approved presentation preferences, never identity, credentials,
bearer tokens, protected data, or dashboard edits.

The exact `postcss` override is `8.5.25`. It replaced `8.5.22` after the
production audit began reporting the incomplete source-map advisory fix in
versions through `8.5.22`; the final `npm audit --omit=dev` reports zero
production vulnerabilities. Future dependency updates must keep the locked
override, clean install, production audit, build and complete test gates in
agreement rather than weakening the audit threshold.

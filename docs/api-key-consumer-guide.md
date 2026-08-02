# API key consumer guide

This guide covers the supported read-only machine surface under `/api/v1` and
the browser-session endpoints that issue and manage personal or organization
API keys.

## Send a key

Send exactly one header value on every machine request:

```http
x-api-key: <YOUR_API_KEY>
```

`<YOUR_API_KEY>` is a placeholder, not a usable credential. Never put a real
key in source control, screenshots, URLs, query strings, browser storage,
analytics, or logs. The header name is lowercase in this guide; HTTP header
names are case-insensitive.

On routes that also accept a browser session, the presence of `x-api-key`
selects API-key authentication exclusively. A valid session cookie does not
rescue a blank, duplicate, malformed, unknown, disabled, revoked, or expired
key.

### Safe generated Node client usage

The generated SDK describes a mixed browser-or-machine operation with separate
cookie and API-key security alternatives. Do not configure a nonempty scalar
`auth` value for such an operation: the generation-owned runtime guard rejects
it locally before fetch so one secret cannot be copied into both `Cookie` and
`x-api-key`. Select the API-key scheme explicitly instead:

```ts
const apiKey = process.env.TEMPLATE_API_KEY;
if (!apiKey) throw new Error("TEMPLATE_API_KEY is required");

const client = createClient({
  baseUrl: process.env.TEMPLATE_API_URL,
  auth: (scheme) => (scheme.name === "x-api-key" ? apiKey : undefined),
});

await getOrganizations({ client });
```

An explicit header on a dedicated client is also supported:

```ts
const apiKey = process.env.TEMPLATE_API_KEY;
if (!apiKey) throw new Error("TEMPLATE_API_KEY is required");

const client = createClient({
  baseUrl: process.env.TEMPLATE_API_URL,
  headers: { "x-api-key": apiKey },
});
```

Never set a `Cookie` header to an API key. A scalar `auth` value remains valid
for a single-scheme machine operation such as `/api/v1/me`, but the selective
callback or explicit-header forms are safer when one client calls both mixed
and machine-only operations. Keep the environment value in a secrets manager
and out of browser bundles, command history and captured test artifacts.

## Read-only scope matrix

API keys grant reads only. They cannot create or mutate organizations, members,
teams, invitations, accounts, or keys.

| Request                                                             | Required API-key scopes                               | Credential modes  |
| ------------------------------------------------------------------- | ----------------------------------------------------- | ----------------- |
| `GET /api/v1/me`                                                    | `basic:read`                                          | API key only      |
| `GET /api/v1/organizations`                                         | `organization:read`                                   | Cookie or API key |
| `GET /api/v1/organizations/{organizationId}`                        | `organization:read`                                   | API key only      |
| `GET /api/v1/organizations/{organizationId}/members`                | `organization:read` + `member:read`                   | Cookie or API key |
| `GET /api/v1/organizations/{organizationId}/teams`                  | `organization:read` + `team:read`                     | Cookie or API key |
| `GET /api/v1/organizations/{organizationId}/teams/{teamId}/members` | `organization:read` + `team:read` + `teamMember:read` | Cookie or API key |

Management clients select presets rather than submitting raw scopes:

| Preset                           | Expanded scopes                                                    |
| -------------------------------- | ------------------------------------------------------------------ |
| `basic-read`                     | `basic:read`                                                       |
| `organization-read`              | `organization:read`                                                |
| `organization-members-read`      | `organization:read`, `member:read`                                 |
| `organization-teams-read`        | `organization:read`, `team:read`                                   |
| `organization-team-members-read` | `organization:read`, `team:read`, `teamMember:read`                |
| `organization-read-all`          | `organization:read`, `member:read`, `team:read`, `teamMember:read` |

At least one preset is required. Preset identifiers and scopes are
case-sensitive closed sets.

## Personal and organization principals

A **personal key** acts as its owning user. Organization reads are checked
against that user's current membership on every request. Losing membership
removes access immediately; the returned organization role and capabilities
remain the current stored membership projection.

An **organization key** is bound to exactly one organization. It cannot access
another organization, and its access does not depend on the user who originally
created it after creation. Organization-key projections use:

- `accessPrincipal: "organization"`;
- the non-membership sentinel `currentRole: "organization"`;
- every browser mutation capability set to `false`.

Personal-key and cookie projections use `accessPrincipal: "user"` and the
current membership role. The organization sentinel is a response discriminator,
not a stored organization membership role.

`GET /api/v1/me` likewise publishes a closed principal union. A personal key
returns `ownerKind: "user"`, a required UUID `userId`, and a null
`organizationId`; an organization key returns the inverse. Consumers should
narrow on `ownerKind` or `accessPrincipal` rather than treating the paired
owner, role, and capability fields as independent values.

Team list items also contain required `membersIncluded`. Cookie reads return
`true`. An API key with `team:read` but without `teamMember:read` receives
`membersIncluded: false`, an empty embedded `members` page, and the safe
`memberCount`. Use the dedicated team-members endpoint with all three required
scopes to read member identities.

## Envelopes and pagination

Successful responses use a data envelope:

```json
{
  "data": {}
}
```

Collections place their page inside `data`:

```json
{
  "data": {
    "items": [],
    "nextCursor": null
  }
}
```

Pass a non-null `nextCursor` back unchanged as the next request's `cursor`.
Never decode, edit, concatenate, or synthesize a cursor. `limit` defaults to
`50` and accepts integers from `1` through `100`. API-key management cursors are
opaque, typed, versioned, checksum-protected canonical base64url values for the
`createdAt DESC, apiKeyId DESC` order. The existing organization, member, team,
and team-member endpoints retain their own opaque target cursor kinds and
orders; cursors are not interchangeable between collections.

API-key management lists active, disabled, and expired non-revoked keys only.
Iteration 7 provides no name, status, or search filters.

## Problem Details and retries

Errors use `application/problem+json` with the standard required Problem Details
fields plus stable `code` and safe `traceId`. Treat HTTP status and `code` as the
machine contract; do not parse human-readable titles or details.

| Status | Stable code                      | Action                                                                                  |
| ------ | -------------------------------- | --------------------------------------------------------------------------------------- |
| `401`  | `api_key_missing`                | Supply exactly one nonblank `x-api-key` value on an API-key-only route.                 |
| `401`  | `api_key_invalid`                | Replace a malformed, unknown, disabled, revoked, or expired credential.                 |
| `403`  | `api_key_permission_denied`      | Issue or select a key with every scope required by the endpoint.                        |
| `403`  | `organization_access_denied`     | Use a personal key with current membership or the key owned by the target organization. |
| `404`  | Resource-specific not-found code | The authorized target organization or team was not found.                               |
| `429`  | `api_key_rate_limited`           | Wait for the response's `Retry-After` duration before retrying.                         |

An API-key `429` includes required `Retry-After` as an integer number of whole
seconds from `1` through `86400`. Apply bounded backoff and do not retry before
that interval. Authentication consumes one quota unit for every valid-key
presentation, including a request that later fails scope or organization-access
authorization. Invalid credentials do not reveal whether a stored key exists.

Validation failures use `400`; management authorization can use `403`, missing
or foreign key targets use `404 api_key_not_found`, and a semantic PATCH no-op
uses `409 api_key_update_unchanged`.

## Create, store, rotate, and revoke

Key management is a browser operation. It requires the secure HttpOnly session
cookie; unsafe management requests also require the antiforgery cookie and the
`X-CSRF-TOKEN` request header obtained from `GET /api/v1/auth/csrf`. API keys
cannot call their own management routes.

Every organization management request supplies `organizationId` as a required
canonical UUID path value, including list and create. Item update, revoke, and
rotate requests also supply the required canonical UUID `apiKeyId`; generated
SDK calls therefore require a `path` object and cannot be invoked without
options.

Creation requires every field: `name`, nonempty `presetIds`, `expiresIn`,
`rateLimitEnabled`, `rateLimitMax`, and `rateLimitWindow`. The closed values are:

- `expiresIn`: `never`, `7d`, `30d`, `90d`, or `365d`;
- `rateLimitWindow`: `1m`, `1h`, or `1d`;
- `rateLimitMax`: integer `1..1000000`;
- trimmed `name`: `1..32` Unicode scalars with no control characters.

The API body is explicit and supplies no management defaults. UI defaults such
as 30 days, 1000 requests per hour, and starter presets are conveniences only.
Response `rateLimitMax` and `requestCount` values are JSON integers and generate
as numeric SDK fields, not numeric strings.

Only a successful create or rotate response contains the raw `key`. Copy it
directly into an approved secrets manager while the reveal view is open, verify
the stored value through a read request, and then clear any transient clipboard
or local state. List, update, revoke, `/me`, and resource-read responses expose
only safe identity/configuration data and never return the raw key or hash.

Rotation keeps the logical key ID and configuration, atomically invalidates the
old credential, resets the active rate window and request counter, and preserves
historical `lastRequestAt`. Store the newly revealed key before closing the
response. Revocation invalidates the credential and removes it from subsequent
lists; a later repeat of the management request returns `404 api_key_not_found`.
Neither rotation nor revocation can be performed with an API key itself.

## Membership-loss coverage boundary

The service re-authorizes a personal key against current membership for every
organization request. The black-box browser E2E suite deliberately does **not**
claim a membership-loss journey: the public contract has no member-removal
operation (only member-role PATCH), and this iteration adds no endpoint,
database hook, or test backdoor to manufacture one. Runtime behavior is covered
at the PostgreSQL/API integration boundary by
`MachineOrganizationEndpointTests.PersonalKeyReadsOnlyCurrentMembershipsWithUserAccessProjection`,
which issues a personal key, removes membership in the test database, and
asserts the next machine read omits the organization. This is an intentional
coverage boundary, not an E2E claim.

# API conventions

## Scope and ownership

ASP.NET Core owns every `/api/**` route. Consumer APIs use URL versioning under
`/api/v1`; operational health routes remain unversioned. The Next.js application
uses REST only and does not access the database or authentication store directly.

## Success and error bodies

Successful JSON responses use a typed envelope:

```json
{ "data": {} }
```

API failures use `application/problem+json` with required non-null RFC Problem
Details fields `type`, `title`, `status`, `detail`, and `instance`, plus required
stable `code` and `traceId`. Validation responses also require an `errors`
dictionary. Each segment of a dotted validation property path is camel-cased,
and messages from source keys that normalize to the same JSON path are merged.
The initial codes are `invalid_request`, `validation_failed`, `unauthorized`,
`forbidden`, `not_found`, `method_not_allowed`, and `internal_error`.

`type` is always `urn:template:problem:{code}`. Client code branches on `code`,
not on invariant-English `title`, `detail`, or validation messages. API responses
never expose stack traces, exception messages, SQL, secrets, cookies, or
authorization headers.

Health `503` is a typed health result rather than a Problem Details failure.

## Validation and authorization

Minimal API binding and Data Annotations validate request DTOs and parameters at
the HTTP boundary. Domain and application rules remain independent of HTTP
validation. Endpoint composition creates one central `/api/v1` consumer group
with the named authenticated-user policy and gives modules that group for
consumer mappings; public operations explicitly opt out with `AllowAnonymous`.

The iteration-1 cookie handler uses scheme `Template.Session` and cookie
`__Host-template.session` with `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`,
and no `Domain`. API challenge/forbid returns `401`/`403` and never redirects to
HTML.

The browser never reads the HttpOnly cookie and never stores a bearer token.
Iteration 3 introduces `GET /api/v1/auth/session`: both anonymous and
authenticated projections return `200 { "data": ... }` with
`Cache-Control: no-store`. Browser requests send the same-origin cookie
automatically; Next.js SSR forwards the incoming `Cookie` header to the API.
Antiforgery is required before the first cookie-authenticated mutation.
The target deployment is same-origin, so CORS is not enabled.

## Health

- `GET /api/health` is the compatibility alias for readiness.
- `GET /api/health/live` excludes dependency checks.
- `GET /api/health/ready` runs checks tagged `ready`.
- Health responses expose only `status` and UTC `timestamp`.
- Healthy responses use `200`; unhealthy readiness uses `503`.
- Every health response uses `Cache-Control: no-store`.

Future database/cache checks must opt into readiness with tag `ready` and must
not participate in liveness.

## Correlation and logging

`X-Correlation-ID` is accepted only when it contains exactly one non-empty value
that is 1–64 characters and restricted to ASCII letters, digits, `.`, `_`, or
`-`. Invalid input is ignored. The canonical value appears in the response
header, Problem Details `traceId`, and the `TraceId` logging scope.

Completion logs contain method, path without query, status, elapsed milliseconds,
and trace scope. Bodies, query values, cookies, and credential headers are not
logged. Health completion is `Debug`; normal API success is `Information`; 4xx
is `Warning`; 5xx is `Error`.

Problem Details/status middleware is limited to `/api/**`, preserving future
Next.js/YARP response ownership.

## OpenAPI

The canonical document is OpenAPI 3.1 document `v1`. Runtime
`/api/openapi/v1.json` exists only in `Development` and `Test`. Production does
not expose a dynamic document or documentation UI.

Cookie authentication is described as cookie `apiKey` scheme `cookieAuth` with
name `__Host-template.session`. Protected operations carry its security
requirement; anonymous operations do not.

Success-envelope schemas require non-null `data`. Standard and validation
Problem Details schemas publish the same required invariant fields that runtime
customization always writes; validation additionally requires `errors`.

Export and verify from the repository root:

```bash
dotnet restore Template.sln
dotnet build apps/api/src/Template.Api/Template.Api.csproj \
  --no-restore \
  -p:OpenApiGenerateDocuments=true
dotnet test Template.sln --no-restore
git diff --exit-code -- contracts/openapi/v1.json
```

Commit `contracts/openapi/v1.json` with every intentional contract change.
Breaking field removal, semantic change, or incompatible status/auth change
requires a documented `/api/v2` and deprecation decision.

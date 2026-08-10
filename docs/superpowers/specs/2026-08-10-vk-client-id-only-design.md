# VK ClientId-only parity fix

## Context

The immutable reference configures VK with `VK_CLIENT_ID` only. Its Better Auth
registration passes only `clientId`; no VK client secret is read or sent.

The ASP.NET Core target currently applies one `ClientId` + `ClientSecret`
completeness rule to every external provider. Its OpenIddict VK ID registration
also calls `SetClientSecret`. Consequently the reference-compatible local
configuration fails startup validation before Kestrel starts.

## Considered approaches

1. **Provider-specific credential requirements (selected).** Keep the shared
   configuration shape, but require only `ClientId` for VK and the existing
   pair for Google, GitHub, GitLab, and Yandex. Register VK as a public PKCE
   client without sending a secret. This restores reference behavior without
   weakening validation for the other providers.
2. **Synthetic VK secret.** Rejected because it invents a credential absent
   from the reference and may send an invalid value during token exchange.
3. **Disable VK when no secret is present.** Rejected because it silently drops
   a configured reference feature instead of reproducing it.

## Design

### Configuration and validation

`ExternalAuthenticationOptionsValidator` resolves every configured dictionary
key to the closed `ExternalProvider` set before validating its credential
requirements.

- VK is configured when `ClientId` is a canonical non-empty value.
- Google, GitHub, GitLab, and Yandex remain configured only when both
  `ClientId` and `ClientSecret` are canonical non-empty values.
- Unknown providers, null provider objects, missing VK `ClientId`, and partial
  credential pairs for secret-bearing providers fail closed with the existing
  non-disclosing validation error.
- A VK `ClientSecret` value is never required and never sent. The shared
  options object may contain the legacy field, but VK registration ignores it;
  this lets an existing ignored local file with an empty field start without
  treating that field as a protocol credential.

The options API will expose a client-id-only lookup for VK instead of reusing
the pair-based `TryGetCompleteCredentials` helper.

### OpenIddict registration

`AddVk` uses the VK client ID lookup, omits `SetClientSecret`, explicitly sets
the registration client type to `public`, and retains the current dedicated
callback, email scope, authorization-code flow, and S256 PKCE capability.
Other registrations are unchanged.

### Public configuration contract

`appsettings.Local.example.json` documents VK with `ClientId` only.
`docs/authentication-persistence-operations.md` records VK as the sole
client-id-only provider and keeps credential-pair instructions for the other
four providers. No API, OpenAPI, EF model, migration, database, cookie, or UI
contract changes.

## Test-first verification

The first regression test supplies `PublicOrigin` plus VK `ClientId` only and
must fail against the current implementation. After the fix it must prove that:

- options validation succeeds;
- exactly one VK registration is produced;
- the registration is public, has the expected client ID, has no client
  secret, and retains S256 PKCE support.

Existing incomplete-provider tests continue proving that a lone client ID for
a secret-bearing provider fails closed. Additional focused coverage verifies
that a configured VK block without a client ID still fails. Then run the
focused API tests and the required solution restore, build, and test commands.
Confirm the OpenAPI contract and immutable `template/` remain unchanged.

## Scope

This change fixes only VK credential parity. It does not alter OAuth callback
paths, identity reconciliation, provider token persistence, external provider
semantics, or any later migration iteration.

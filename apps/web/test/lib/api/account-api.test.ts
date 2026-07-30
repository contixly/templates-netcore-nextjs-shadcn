/** @jest-environment node */

import {
  deleteBrowserAccount,
  disconnectBrowserAccountProvider,
  revokeBrowserAccountSession,
  revokeOtherBrowserAccountSessions,
  updateBrowserAccountProfile,
} from "@/src/lib/api/account/browser/account-mutations";
import { loadAccount } from "@/src/lib/api/account/server/load-account";
import { loadConnections } from "@/src/lib/api/account/server/load-connections";
import { loadSessions } from "@/src/lib/api/account/server/load-sessions";
import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import type { Client } from "@/src/lib/api/generated/client";
import {
  type AccountResponse,
  deleteAccount,
  disconnectAccountProvider,
  getAccount,
  getAccountConnections,
  getAccountSessions,
  revokeAccountSession,
  revokeOtherAccountSessions,
  updateAccountProfile,
} from "@/src/lib/api/generated";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

jest.mock("@/src/lib/api/generated", () => ({
  deleteAccount: jest.fn(),
  disconnectAccountProvider: jest.fn(),
  getAccount: jest.fn(),
  getAccountConnections: jest.fn(),
  getAccountSessions: jest.fn(),
  revokeAccountSession: jest.fn(),
  revokeOtherAccountSessions: jest.fn(),
  updateAccountProfile: jest.fn(),
}));
jest.mock("@/src/lib/api/auth/browser/get-auth-csrf", () => ({
  getAuthCsrfToken: jest.fn(),
}));
jest.mock("@/src/lib/api/server/client", () => ({
  createServerApiClient: jest.fn(),
}));
jest.mock("@/src/lib/api/server/request-headers", () => ({
  readForwardedApiHeaders: jest.fn(),
}));

const client = { role: "request-bound" } as unknown as Client;
const mockedCreateServerClient = jest.mocked(createServerApiClient);
const mockedReadForwardedApiHeaders = jest.mocked(readForwardedApiHeaders);
const mockedGetAccount = jest.mocked(getAccount);
const mockedGetConnections = jest.mocked(getAccountConnections);
const mockedGetSessions = jest.mocked(getAccountSessions);
const mockedGetCsrf = jest.mocked(getAuthCsrfToken);
const mockedUpdateProfile = jest.mocked(updateAccountProfile);
const mockedDisconnect = jest.mocked(disconnectAccountProvider);
const mockedRevokeSession = jest.mocked(revokeAccountSession);
const mockedRevokeOthers = jest.mocked(revokeOtherAccountSessions);
const mockedDeleteAccount = jest.mocked(deleteAccount);

const account: AccountResponse = {
  id: "01900000-0000-7000-8000-000000000001",
  displayName: "Account User",
  primaryEmail: "account@example.test",
  imageUrl: null,
  createdAt: "2026-07-29T00:00:00Z",
  verifiedEmails: [],
};

function sdkSuccess<T>(data: T) {
  return {
    data: { data },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  };
}

beforeEach(() => {
  jest.clearAllMocks();
  mockedReadForwardedApiHeaders.mockResolvedValue({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-account",
  });
  mockedCreateServerClient.mockReturnValue({ ok: true, client });
});

it("loads account projections with request-bound headers and no-store generated operations", async () => {
  mockedGetAccount.mockResolvedValue(sdkSuccess(account));
  mockedGetConnections.mockResolvedValue(sdkSuccess({ items: [] }));
  mockedGetSessions.mockResolvedValue(
    sdkSuccess({ items: [], nextCursor: "next-page" }),
  );

  await expect(loadAccount()).resolves.toEqual({ ok: true, data: account });
  await expect(loadConnections()).resolves.toEqual({
    ok: true,
    data: { items: [] },
  });
  await expect(
    loadSessions({ cursor: "opaque-cursor", limit: 10 }),
  ).resolves.toEqual({
    ok: true,
    data: { items: [], nextCursor: "next-page" },
  });

  expect(mockedReadForwardedApiHeaders).toHaveBeenCalledTimes(3);
  expect(mockedCreateServerClient).toHaveBeenCalledTimes(3);
  expect(mockedCreateServerClient).toHaveBeenCalledWith({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-account",
  });
  expect(mockedGetAccount).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    headers: {
      "X-Template-Session-Renewal": "suppress",
    },
  });
  expect(mockedGetConnections).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    headers: {
      "X-Template-Session-Renewal": "suppress",
    },
  });
  expect(mockedGetSessions).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    headers: {
      "X-Template-Session-Renewal": "suppress",
    },
    query: { cursor: "opaque-cursor", limit: 10 },
  });
});

it("returns safe server adapter failures without leaking backend detail", async () => {
  mockedGetConnections.mockResolvedValue({
    data: undefined,
    error: {
      type: "urn:template:problem:internal_error",
      title: "Account unavailable",
      status: 503,
      instance: "/api/v1/account/connections",
      code: "internal_error",
      detail: "private account provider failure",
      traceId: "trace-safe",
    },
    request: new Request("https://example.test"),
    response: new Response(null, { status: 503 }),
  });

  const result = await loadConnections();

  expect(result).toEqual({
    ok: false,
    failure: {
      kind: "problem",
      code: "internal_error",
      status: 503,
      traceId: "trace-safe",
    },
  });
  expect(JSON.stringify(result)).not.toContain(
    "private account provider failure",
  );
});

it("returns server configuration failures before calling the generated SDK", async () => {
  mockedCreateServerClient.mockReturnValue({
    ok: false,
    failure: {
      kind: "configuration",
      code: "api_configuration_missing",
    },
  });

  await expect(loadAccount()).resolves.toEqual({
    ok: false,
    failure: {
      kind: "configuration",
      code: "api_configuration_missing",
    },
  });
  expect(mockedGetAccount).not.toHaveBeenCalled();
});

it("gets fresh CSRF before every browser account mutation", async () => {
  mockedGetCsrf
    .mockResolvedValueOnce({ ok: true, data: "csrf-profile" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-disconnect" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-session" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-others" })
    .mockResolvedValueOnce({ ok: true, data: "csrf-delete" });
  mockedUpdateProfile.mockResolvedValue(sdkSuccess(account));
  mockedDisconnect.mockResolvedValue(sdkSuccess({ provider: "github" }));
  mockedRevokeSession.mockResolvedValue(
    sdkSuccess({ sessionId: "session-other" }),
  );
  mockedRevokeOthers.mockResolvedValue(sdkSuccess({ revokedCount: 2 }));
  mockedDeleteAccount.mockResolvedValue(sdkSuccess({ deleted: true }));

  await expect(
    updateBrowserAccountProfile(client, { displayName: "Updated User" }),
  ).resolves.toEqual({ ok: true, data: account });
  await expect(
    disconnectBrowserAccountProvider(client, "github"),
  ).resolves.toEqual({ ok: true, data: { provider: "github" } });
  await expect(
    revokeBrowserAccountSession(client, "session-other"),
  ).resolves.toEqual({
    ok: true,
    data: { sessionId: "session-other" },
  });
  await expect(revokeOtherBrowserAccountSessions(client)).resolves.toEqual({
    ok: true,
    data: { revokedCount: 2 },
  });
  await expect(
    deleteBrowserAccount(client, {
      confirmationEmail: "account@example.test",
    }),
  ).resolves.toEqual({ ok: true, data: { deleted: true } });

  expect(mockedGetCsrf).toHaveBeenCalledTimes(5);
  expect(mockedGetCsrf).toHaveBeenCalledWith(client);
  expect(mockedUpdateProfile).toHaveBeenCalledWith({
    client,
    body: { displayName: "Updated User" },
    headers: { "X-CSRF-TOKEN": "csrf-profile" },
  });
  expect(mockedDisconnect).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-disconnect" },
    path: { provider: "github" },
  });
  expect(mockedRevokeSession).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-session" },
    path: { sessionId: "session-other" },
  });
  expect(mockedRevokeOthers).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-others" },
  });
  expect(mockedDeleteAccount).toHaveBeenCalledWith({
    client,
    body: { confirmationEmail: "account@example.test" },
    headers: { "X-CSRF-TOKEN": "csrf-delete" },
  });

  for (const operation of [
    mockedUpdateProfile,
    mockedDisconnect,
    mockedRevokeSession,
    mockedRevokeOthers,
    mockedDeleteAccount,
  ]) {
    expect(mockedGetCsrf.mock.invocationCallOrder[0]).toBeLessThan(
      operation.mock.invocationCallOrder[0],
    );
  }
});

it("does not mutate when obtaining CSRF fails", async () => {
  mockedGetCsrf.mockResolvedValue({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });

  await expect(
    updateBrowserAccountProfile(client, { displayName: "Updated User" }),
  ).resolves.toEqual({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
  expect(mockedUpdateProfile).not.toHaveBeenCalled();
});

it("normalizes thrown browser mutation failures", async () => {
  mockedGetCsrf.mockResolvedValue({ ok: true, data: "csrf-delete" });
  mockedDeleteAccount.mockRejectedValue(
    new TypeError("private internal API origin"),
  );

  const result = await deleteBrowserAccount(client, {
    confirmationEmail: "account@example.test",
  });

  expect(result).toEqual({
    ok: false,
    failure: { kind: "network", code: "api_unavailable" },
  });
  expect(JSON.stringify(result)).not.toContain("private internal API origin");
});

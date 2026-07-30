/** @jest-environment node */

import type { Client } from "@/src/lib/api/generated/client";
import {
  challengeExternalAuth,
  createLocalAutomationScenario,
  getAuthCapabilities,
  getAuthCsrf,
  getAuthSession,
  logout,
} from "@/src/lib/api/generated";
import { createLocalAutomationBrowserSession } from "@/src/lib/api/auth/browser/create-local-automation-browser-session";
import { logoutBrowserSession } from "@/src/lib/api/auth/browser/logout-browser-session";
import { startExternalAuth } from "@/src/lib/api/auth/browser/start-external-auth";
import { loadAuthCapabilities } from "@/src/lib/api/auth/load-auth-capabilities";
import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";
import { createBrowserApiClient } from "@/src/lib/api/browser/client";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

jest.mock("@/src/lib/api/generated", () => ({
  challengeExternalAuth: jest.fn(),
  createLocalAutomationScenario: jest.fn(),
  getAuthCapabilities: jest.fn(),
  getAuthCsrf: jest.fn(),
  getAuthSession: jest.fn(),
  logout: jest.fn(),
}));
jest.mock("@/src/lib/api/browser/client", () => ({
  createBrowserApiClient: jest.fn(),
}));
jest.mock("@/src/lib/api/server/client", () => ({
  createServerApiClient: jest.fn(),
}));
jest.mock("@/src/lib/api/server/request-headers", () => ({
  readForwardedApiHeaders: jest.fn(),
}));

const client = {} as Client;
const capabilitiesClient = { role: "capabilities" } as unknown as Client;
const sessionClient = { role: "session" } as unknown as Client;
const mockedCapabilities = jest.mocked(getAuthCapabilities);
const mockedChallenge = jest.mocked(challengeExternalAuth);
const mockedSession = jest.mocked(getAuthSession);
const mockedCsrf = jest.mocked(getAuthCsrf);
const mockedCreate = jest.mocked(createLocalAutomationScenario);
const mockedLogout = jest.mocked(logout);
const mockedCreateBrowserClient = jest.mocked(createBrowserApiClient);
const mockedCreateServerClient = jest.mocked(createServerApiClient);
const mockedReadForwardedApiHeaders = jest.mocked(readForwardedApiHeaders);

beforeEach(() => {
  jest.clearAllMocks();
  mockedCreateBrowserClient.mockReturnValue(client);
  mockedCreateServerClient.mockReset();
});

it("loads capability and session data from generated envelopes", async () => {
  mockedCapabilities.mockResolvedValue({
    data: {
      data: { localAutomationEnabled: true, providers: [] },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedSession.mockResolvedValue({
    data: {
      data: { authenticated: false, user: null, session: null },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  await expect(loadAuthCapabilities(client)).resolves.toEqual({
    ok: true,
    data: { localAutomationEnabled: true, providers: [] },
  });
  await expect(loadAuthSession(client)).resolves.toEqual({
    ok: true,
    data: { authenticated: false, user: null, session: null },
  });
  expect(mockedCapabilities).toHaveBeenCalledWith({
    client,
    cache: "no-store",
  });
  expect(mockedSession).toHaveBeenCalledWith({
    client,
    cache: "no-store",
  });
});

it("composes request-bound server authentication state", async () => {
  mockedReadForwardedApiHeaders.mockResolvedValue({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-auth",
  });
  mockedCreateServerClient
    .mockReturnValueOnce({ ok: true, client: capabilitiesClient })
    .mockReturnValueOnce({ ok: true, client: sessionClient });
  mockedCapabilities.mockResolvedValue({
    data: {
      data: { localAutomationEnabled: true, providers: [] },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedSession.mockResolvedValue({
    data: {
      data: { authenticated: false, user: null, session: null },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  await expect(loadServerAuthState()).resolves.toEqual({
    ok: true,
    data: {
      capabilities: { localAutomationEnabled: true, providers: [] },
      session: { authenticated: false, user: null, session: null },
    },
  });
  expect(mockedReadForwardedApiHeaders).toHaveBeenCalledTimes(1);
  expect(mockedCreateServerClient).toHaveBeenNthCalledWith(1, {
    correlationId: "trace-auth",
  });
  expect(mockedCreateServerClient).toHaveBeenNthCalledWith(2, {
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-auth",
  });
  expect(mockedCapabilities).toHaveBeenCalledWith({
    client: capabilitiesClient,
    cache: "no-store",
  });
  expect(mockedSession).toHaveBeenCalledWith({
    client: sessionClient,
    cache: "no-store",
    headers: { "X-Template-Session-Renewal": "suppress" },
  });
});

it("marks dashboard SSR session reads as non-renewing", async () => {
  mockedReadForwardedApiHeaders.mockResolvedValue({
    cookie: "__Host-template.session=opaque",
  });
  mockedCreateServerClient.mockReturnValue({ ok: true, client });
  mockedSession.mockResolvedValue({
    data: {
      data: { authenticated: false, user: null, session: null },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  await loadServerAuthSession();

  expect(mockedSession).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    headers: { "X-Template-Session-Renewal": "suppress" },
  });
});

it("gets CSRF before creating a local browser session", async () => {
  mockedCsrf.mockResolvedValue({
    data: { data: { requestToken: "csrf-create" } },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedCreate.mockResolvedValue({
    data: {
      data: {
        user: {
          id: "01900000-0000-7000-8000-000000000001",
          name: "Local User",
          email: "local-agent+ui@local-agent.test",
          emailVerified: false,
          image: null,
        },
        email: "local-agent+ui@local-agent.test",
        password: "local-secret-password",
        cleanupUrl: "/api/local-auth/scenario",
      },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  const result = await createLocalAutomationBrowserSession(client);

  expect(result.ok).toBe(true);
  expect(mockedCreate).toHaveBeenCalledWith({
    client,
    body: {},
    headers: { "X-CSRF-TOKEN": "csrf-create" },
  });
  expect(mockedCsrf.mock.invocationCallOrder[0]).toBeLessThan(
    mockedCreate.mock.invocationCallOrder[0],
  );
});

it("gets CSRF before logout", async () => {
  mockedCsrf.mockResolvedValue({
    data: { data: { requestToken: "csrf-logout" } },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedLogout.mockResolvedValue({
    data: {
      data: { authenticated: false, user: null, session: null },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  await logoutBrowserSession(client);

  expect(mockedLogout).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-logout" },
  });
});

it("gets CSRF before starting an external provider challenge", async () => {
  mockedCsrf.mockResolvedValue({
    data: { data: { requestToken: "csrf-external" } },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedChallenge.mockResolvedValue({
    data: {
      data: {
        authorizationUrl:
          "https://accounts.google.com/o/oauth2/v2/auth?state=safe",
      },
    },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });

  await expect(
    startExternalAuth({
      provider: "google",
      intent: "signIn",
      returnUrl: "/dashboard",
    }),
  ).resolves.toEqual({
    ok: true,
    data: {
      authorizationUrl:
        "https://accounts.google.com/o/oauth2/v2/auth?state=safe",
    },
  });
  expect(mockedChallenge).toHaveBeenCalledWith({
    client,
    body: { intent: "signIn", returnUrl: "/dashboard" },
    headers: { "X-CSRF-TOKEN": "csrf-external" },
    path: { provider: "google" },
  });
  expect(mockedCsrf.mock.invocationCallOrder[0]).toBeLessThan(
    mockedChallenge.mock.invocationCallOrder[0],
  );
});

it("normalizes an external challenge Problem Details failure", async () => {
  mockedCsrf.mockResolvedValue({
    data: { data: { requestToken: "csrf-external" } },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(),
  });
  mockedChallenge.mockResolvedValue({
    data: undefined,
    error: {
      type: "urn:template:problem:external_provider_not_configured",
      title: "Provider not configured",
      status: 404,
      detail: "Backend detail must remain private.",
      instance: "/api/v1/auth/external/google/challenge",
      code: "external_provider_not_configured",
      traceId: "trace-external",
    },
    request: new Request("https://example.test"),
    response: new Response(null, { status: 404 }),
  });

  await expect(
    startExternalAuth({
      provider: "google",
      intent: "signIn",
      returnUrl: "/dashboard",
    }),
  ).resolves.toEqual({
    ok: false,
    failure: {
      kind: "problem",
      code: "external_provider_not_configured",
      status: 404,
      traceId: "trace-external",
    },
  });
});

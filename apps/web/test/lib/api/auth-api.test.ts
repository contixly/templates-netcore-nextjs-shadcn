/** @jest-environment node */

import type { Client } from "@/src/lib/api/generated/client";
import {
  createLocalAutomationScenario,
  getAuthCapabilities,
  getAuthCsrf,
  getAuthSession,
  logout,
} from "@/src/lib/api/generated";
import { createLocalAutomationBrowserSession } from "@/src/lib/api/auth/browser/create-local-automation-browser-session";
import { logoutBrowserSession } from "@/src/lib/api/auth/browser/logout-browser-session";
import { loadAuthCapabilities } from "@/src/lib/api/auth/load-auth-capabilities";
import { loadAuthSession } from "@/src/lib/api/auth/load-auth-session";
import { loadServerAuthSession } from "@/src/lib/api/auth/server/load-server-auth-session";
import { loadServerAuthState } from "@/src/lib/api/auth/server/load-server-auth-state";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

jest.mock("@/src/lib/api/generated", () => ({
  createLocalAutomationScenario: jest.fn(),
  getAuthCapabilities: jest.fn(),
  getAuthCsrf: jest.fn(),
  getAuthSession: jest.fn(),
  logout: jest.fn(),
}));
jest.mock("@/src/lib/api/server/client", () => ({
  createServerApiClient: jest.fn(),
}));
jest.mock("@/src/lib/api/server/request-headers", () => ({
  readForwardedApiHeaders: jest.fn(),
}));

const client = {} as Client;
const mockedCapabilities = jest.mocked(getAuthCapabilities);
const mockedSession = jest.mocked(getAuthSession);
const mockedCsrf = jest.mocked(getAuthCsrf);
const mockedCreate = jest.mocked(createLocalAutomationScenario);
const mockedLogout = jest.mocked(logout);
const mockedCreateServerClient = jest.mocked(createServerApiClient);
const mockedReadForwardedApiHeaders = jest.mocked(readForwardedApiHeaders);

beforeEach(() => {
  jest.clearAllMocks();
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
  mockedCreateServerClient.mockReturnValue({ ok: true, client });
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
  expect(mockedCreateServerClient).toHaveBeenCalledWith({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-auth",
  });
  expect(mockedCapabilities).toHaveBeenCalledWith({
    client,
    cache: "no-store",
  });
  expect(mockedSession).toHaveBeenCalledWith({
    client,
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

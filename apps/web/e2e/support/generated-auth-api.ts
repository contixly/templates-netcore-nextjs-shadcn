import type { APIRequestContext } from "@playwright/test";

import {
  createLocalAutomationScenario,
  deleteLocalAutomationScenario,
  getAccount,
  getAccountSessions,
  getAuthCsrf,
  getAuthSession,
  logout,
  signInLocalAutomation,
  type CreateLocalAutomationScenarioRequest,
} from "../../src/lib/api/generated";
import { createClient, type Client } from "../../src/lib/api/generated/client";

const webOrigin = "http://127.0.0.1:3127";

async function sharedCookieHeader(
  request: APIRequestContext,
  url: URL,
): Promise<string | undefined> {
  // Chromium sends Secure cookies on trusted loopback origins; Playwright's
  // API transport needs the matching cookies copied from its shared context.
  const state = await request.storageState();
  const cookies = state.cookies.filter((cookie) => {
    const domain = cookie.domain.startsWith(".")
      ? cookie.domain.slice(1)
      : cookie.domain;
    const domainMatches =
      url.hostname === domain || url.hostname.endsWith(`.${domain}`);
    const pathMatches =
      url.pathname === cookie.path ||
      url.pathname.startsWith(
        cookie.path.endsWith("/") ? cookie.path : `${cookie.path}/`,
      );
    return domainMatches && pathMatches;
  });
  return cookies.length > 0
    ? cookies.map((cookie) => `${cookie.name}=${cookie.value}`).join("; ")
    : undefined;
}

function createPlaywrightFetch(request: APIRequestContext): typeof fetch {
  return async (input, init) => {
    const source = input instanceof Request ? input : new Request(input, init);
    const url = new URL(source.url);
    if (url.origin !== webOrigin) {
      throw new Error(`E2E SDK request escaped the web origin: ${url.origin}.`);
    }
    const headers: Record<string, string> = {};
    source.headers.forEach((value, name) => {
      headers[name] = value;
    });
    const cookie = await sharedCookieHeader(request, url);
    if (cookie) {
      headers.cookie = cookie;
    }
    const body =
      source.method === "GET" || source.method === "HEAD"
        ? undefined
        : Buffer.from(await source.arrayBuffer());
    const response = await request.fetch(source.url, {
      data: body,
      failOnStatusCode: false,
      headers,
      method: source.method,
    });
    const responseHeaders = new Headers();
    for (const header of response.headersArray()) {
      responseHeaders.append(header.name, header.value);
    }
    return new Response(new Uint8Array(await response.body()), {
      headers: responseHeaders,
      status: response.status(),
    });
  };
}

export function clientFor(request: APIRequestContext): Client {
  return createClient({
    baseUrl: webOrigin,
    fetch: createPlaywrightFetch(request),
  });
}

export async function csrf(client: Client): Promise<string> {
  const result = await getAuthCsrf({ client });
  if (!result.data) {
    throw new Error(
      `CSRF request failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data.requestToken;
}

export async function signInLocalAutomationUser(
  request: APIRequestContext,
  email: string,
  password: string,
) {
  const client = clientFor(request);
  const result = await signInLocalAutomation({
    client,
    body: { email, password },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw new Error(
      `Local credential sign-in failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}

export async function cleanupExistingLocalAutomationUser(
  request: APIRequestContext,
  email: string,
  password: string,
) {
  const client = clientFor(request);
  const signIn = await signInLocalAutomation({
    client,
    body: { email, password },
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!signIn.data) {
    if (signIn.response?.status === 401) {
      return { deletedOrganizations: 0, found: false };
    }
    throw new Error(
      `Local preflight sign-in failed with ${signIn.response?.status ?? 0}.`,
    );
  }

  const cleanup = await deleteLocalAutomationScenario({
    client,
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!cleanup.data) {
    throw new Error(
      `Local preflight cleanup failed with ${cleanup.response?.status ?? 0}.`,
    );
  }
  return {
    deletedOrganizations: cleanup.data.data.deletedOrganizations,
    found: true,
  };
}

export async function cleanupLocalAutomationUser(request: APIRequestContext) {
  const client = clientFor(request);
  const result = await deleteLocalAutomationScenario({
    client,
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw new Error(
      `Local cleanup failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}

export async function logoutGeneratedSession(request: APIRequestContext) {
  const client = clientFor(request);
  const result = await logout({
    client,
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw new Error(`Logout failed with ${result.response?.status ?? 0}.`);
  }
  return result.data.data;
}

export async function createLocalAutomationUser(
  request: APIRequestContext,
  body: CreateLocalAutomationScenarioRequest,
) {
  const client = clientFor(request);
  const result = await createLocalAutomationScenario({
    client,
    body,
    headers: { "X-CSRF-TOKEN": await csrf(client) },
  });
  if (!result.data) {
    throw new Error(
      `Local account creation failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}

export async function getGeneratedAccount(request: APIRequestContext) {
  const result = await getAccount({
    client: clientFor(request),
    cache: "no-store",
  });
  if (!result.data) {
    throw new Error(
      `Account lookup failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}

export async function getGeneratedAuthSession(request: APIRequestContext) {
  const result = await getAuthSession({ client: clientFor(request) });
  if (!result.data) {
    throw new Error(
      `Session lookup failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}

export async function getGeneratedAccountSessions(request: APIRequestContext) {
  const result = await getAccountSessions({
    client: clientFor(request),
    cache: "no-store",
  });
  if (!result.data) {
    throw new Error(
      `Account session lookup failed with ${result.response?.status ?? 0}.`,
    );
  }
  return result.data.data;
}

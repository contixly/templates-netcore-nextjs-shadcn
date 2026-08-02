/** @jest-environment node */

import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  createBrowserApiKey,
  listBrowserApiKeys,
  revokeBrowserApiKey,
  rotateBrowserApiKey,
  updateBrowserApiKey,
} from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { loadApiKeys } from "@/src/lib/api/api-keys/server/load-api-keys";
import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import {
  createPersonalApiKey,
  listPersonalApiKeys,
  revokePersonalApiKey,
  rotatePersonalApiKey,
  updatePersonalApiKey,
} from "@/src/lib/api/generated/sdk.gen";
import type { ApiKeyResponse } from "@/src/lib/api/generated/types.gen";
import type { Client } from "@/src/lib/api/generated/client";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

jest.mock("@/src/lib/api/generated/sdk.gen", () => ({
  createPersonalApiKey: jest.fn(),
  listPersonalApiKeys: jest.fn(),
  revokePersonalApiKey: jest.fn(),
  rotatePersonalApiKey: jest.fn(),
  updatePersonalApiKey: jest.fn(),
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
const keyId = "01900000-0000-7000-8000-000000000909";
const key: ApiKeyResponse = {
  id: keyId,
  ownerKind: "user",
  ownerId: "01900000-0000-7000-8000-000000000001",
  name: "CLI",
  start: "tk_live_safe_sta",
  status: "active",
  enabled: true,
  scopes: ["basic:read"],
  rateLimitEnabled: true,
  rateLimitMax: 1000,
  rateLimitWindow: "1h",
  requestCount: 0,
  windowStartedAt: null,
  lastRequestAt: null,
  expiresAt: "2026-09-01T00:00:00Z",
  rotatedAt: null,
  createdAt: "2026-08-02T00:00:00Z",
  updatedAt: "2026-08-02T00:00:00Z",
};

const mockedGetCsrf = jest.mocked(getAuthCsrfToken);
const mockedCreate = jest.mocked(createPersonalApiKey);
const mockedList = jest.mocked(listPersonalApiKeys);
const mockedRevoke = jest.mocked(revokePersonalApiKey);
const mockedRotate = jest.mocked(rotatePersonalApiKey);
const mockedUpdate = jest.mocked(updatePersonalApiKey);
const mockedCreateServerClient = jest.mocked(createServerApiClient);
const mockedReadForwardedHeaders = jest.mocked(readForwardedApiHeaders);

function sdkSuccess<T>(data: T) {
  return {
    data: { data },
    error: undefined,
    request: new Request("https://example.test"),
    response: new Response(null, { status: 200 }),
  };
}

beforeEach(() => {
  jest.clearAllMocks();
  mockedGetCsrf.mockResolvedValue({ ok: true, data: "csrf-api-key" });
  mockedReadForwardedHeaders.mockResolvedValue({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-api-key",
  });
  mockedCreateServerClient.mockReturnValue({ ok: true, client });
});

it("loads exactly the requested page through the generated GET and SSR header policy", async () => {
  mockedList.mockResolvedValue(
    sdkSuccess({ items: [key], nextCursor: "next-page" }),
  );

  await expect(
    loadApiKeys({ kind: "personal" }, { cursor: "opaque", limit: 50 }),
  ).resolves.toEqual({
    ok: true,
    data: { items: [key], nextCursor: "next-page" },
  });

  expect(mockedCreateServerClient).toHaveBeenCalledWith({
    cookie: "__Host-template.session=opaque",
    correlationId: "trace-api-key",
  });
  expect(mockedList).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    headers: { "X-Template-Session-Renewal": "suppress" },
    query: { cursor: "opaque", limit: 50 },
  });
});

it("uses generated safe GET directly and obtains fresh CSRF for every unsafe call", async () => {
  mockedList.mockResolvedValue(sdkSuccess({ items: [key], nextCursor: null }));
  mockedCreate.mockResolvedValue(sdkSuccess({ ...key, key: "raw-once" }));
  mockedUpdate.mockResolvedValue(sdkSuccess({ ...key, name: "Updated" }));
  mockedRotate.mockResolvedValue(sdkSuccess({ ...key, key: "rotated-once" }));
  mockedRevoke.mockResolvedValue(
    sdkSuccess({ id: keyId, revokedAt: "2026-08-02T01:00:00Z" }),
  );

  await listBrowserApiKeys(client, { kind: "personal" }, { cursor: "next" });
  await createBrowserApiKey(
    client,
    { kind: "personal" },
    {
      name: "CLI",
      presetIds: ["basic-read"],
      expiresIn: "30d",
      rateLimitEnabled: true,
      rateLimitMax: 1000,
      rateLimitWindow: "1h",
    },
  );
  await updateBrowserApiKey(client, { kind: "personal" }, keyId, {
    name: "Updated",
  });
  await rotateBrowserApiKey(client, { kind: "personal" }, keyId);
  await revokeBrowserApiKey(client, { kind: "personal" }, keyId);

  expect(mockedList).toHaveBeenCalledWith({
    client,
    cache: "no-store",
    query: { cursor: "next" },
  });
  expect(mockedGetCsrf).toHaveBeenCalledTimes(4);
  expect(mockedCreate).toHaveBeenCalledWith({
    client,
    body: expect.objectContaining({ name: "CLI" }),
    headers: { "X-CSRF-TOKEN": "csrf-api-key" },
  });
  expect(mockedUpdate).toHaveBeenCalledWith({
    client,
    body: { name: "Updated" },
    headers: { "X-CSRF-TOKEN": "csrf-api-key" },
    path: { apiKeyId: keyId },
  });
  expect(mockedRotate).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-api-key" },
    path: { apiKeyId: keyId },
  });
  expect(mockedRevoke).toHaveBeenCalledWith({
    client,
    headers: { "X-CSRF-TOKEN": "csrf-api-key" },
    path: { apiKeyId: keyId },
  });
});

it("keeps the feature on generated transport types without raw fetch or Server Actions", () => {
  const files = [
    "src/lib/api/api-keys/server/load-api-keys.ts",
    "src/lib/api/api-keys/browser/api-key-mutations.ts",
  ].map((path) => readFileSync(resolve(process.cwd(), path), "utf8"));

  for (const source of files) {
    expect(source).not.toMatch(/\bfetch\s*\(/);
    expect(source).not.toMatch(/["']use server["']/);
    expect(source).not.toMatch(
      /(?:interface|type)\s+(?:CreateApiKeyRequest|UpdateApiKeyRequest|ApiKeyResponse|ApiKeyPageResponse)\b/,
    );
  }
  expect(files.join("\n")).toContain("@/src/lib/api/generated");
});

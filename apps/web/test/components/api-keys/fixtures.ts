import type {
  ApiKeyPageResponse,
  ApiKeyResponse,
  ApiKeySecretResponse,
} from "@/src/lib/api/generated/types.gen";

export const apiKey: ApiKeyResponse = {
  id: "01900000-0000-7000-8000-000000000901",
  ownerKind: "user",
  ownerId: "01900000-0000-7000-8000-000000000001",
  name: "CLI integration",
  start: "tmpl_live_safe1",
  status: "active",
  enabled: true,
  scopes: ["basic:read"],
  rateLimitEnabled: true,
  rateLimitMax: 1000,
  rateLimitWindow: "1h",
  requestCount: 12,
  windowStartedAt: "2026-08-02T09:00:00Z",
  lastRequestAt: "2026-08-02T09:15:00Z",
  expiresAt: "2026-09-01T10:00:00Z",
  rotatedAt: null,
  createdAt: "2026-08-02T10:00:00Z",
  updatedAt: "2026-08-02T10:00:00Z",
};

export const apiKeySecret: ApiKeySecretResponse = {
  ...apiKey,
  key: "tmpl_live_raw_secret_once",
};

export const apiKeyPage: ApiKeyPageResponse = {
  items: [apiKey],
  nextCursor: null,
};

export function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((next, fail) => {
    resolve = next;
    reject = fail;
  });
  return { promise, reject, resolve };
}

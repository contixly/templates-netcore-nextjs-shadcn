import "server-only";

import { resolveApiBaseUrl } from "@/src/lib/api/api-base-url";
import { createClient, type Client } from "@/src/lib/api/generated/client";
import type { ApiFailure } from "@/src/lib/api/result";

export type ForwardedApiHeaders = Readonly<{
  cookie?: string;
  correlationId?: string;
}>;

export type ServerApiClientResult =
  | { ok: true; client: Client }
  | {
      ok: false;
      failure: Extract<ApiFailure, { kind: "configuration" }>;
    };

export function createServerApiClient(
  forwarded: ForwardedApiHeaders = {},
): ServerApiClientResult {
  const baseUrl = resolveApiBaseUrl(process.env.API_INTERNAL_BASE_URL);

  if (!baseUrl.ok) {
    return {
      ok: false,
      failure: {
        kind: "configuration",
        code: baseUrl.code,
      },
    };
  }

  const headers = new Headers();

  if (forwarded.cookie) {
    headers.set("cookie", forwarded.cookie);
  }

  if (forwarded.correlationId) {
    headers.set("x-correlation-id", forwarded.correlationId);
  }

  const hasForwardedHeaders = Boolean(
    forwarded.cookie || forwarded.correlationId,
  );

  return {
    ok: true,
    client: createClient({
      baseUrl: baseUrl.value,
      cache: "no-store",
      ...(hasForwardedHeaders ? { headers } : {}),
    }),
  };
}

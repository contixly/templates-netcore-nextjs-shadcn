"use client";

import { getAuthCsrfToken } from "@/src/lib/api/auth/browser/get-auth-csrf";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import type { Client } from "@/src/lib/api/generated/client";
import type { ApiResult } from "@/src/lib/api/result";

export type MutationResponse<T> = Readonly<{
  data?: Readonly<{ data: T }>;
  error?: unknown;
  response?: Response;
}>;

export async function runCsrfMutation<T>(
  client: Client,
  operation: (csrfToken: string) => Promise<MutationResponse<T>>,
): Promise<ApiResult<T>> {
  const csrf = await getAuthCsrfToken(client);
  if (!csrf.ok) {
    return csrf;
  }

  try {
    const result = await operation(csrf.data);
    return result.data !== undefined
      ? { ok: true, data: result.data.data }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}

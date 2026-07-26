"use client";

import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getAuthCsrf } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { AuthCsrfResult } from "@/src/lib/api/result";

export async function getAuthCsrfToken(
  client: Client,
): Promise<AuthCsrfResult> {
  try {
    const result = await getAuthCsrf({ client, cache: "no-store" });
    return result.data !== undefined
      ? { ok: true, data: result.data.data.requestToken }
      : {
          ok: false,
          failure: normalizeApiFailure(result.error, result.response),
        };
  } catch (error) {
    return { ok: false, failure: normalizeApiFailure(error) };
  }
}

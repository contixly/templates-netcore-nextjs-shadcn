import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getAuthSession } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { AuthSessionResult } from "@/src/lib/api/result";

export async function loadAuthSession(
  client: Client,
  options: Readonly<{ suppressSlidingRenewal?: boolean }> = {},
): Promise<AuthSessionResult> {
  try {
    const result = await getAuthSession({
      client,
      cache: "no-store",
      ...(options.suppressSlidingRenewal
        ? {
            headers: {
              "X-Template-Session-Renewal": "suppress",
            },
          }
        : {}),
    });
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

import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getAuthCapabilities } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { AuthCapabilitiesResult } from "@/src/lib/api/result";

export async function loadAuthCapabilities(
  client: Client,
): Promise<AuthCapabilitiesResult> {
  try {
    const result = await getAuthCapabilities({ client, cache: "no-store" });
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

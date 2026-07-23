import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getSystemStatus } from "@/src/lib/api/generated";
import type { Client } from "@/src/lib/api/generated/client";
import type { SystemStatusResult } from "@/src/lib/api/result";

export type SystemStatusSource = "browser" | "ssr";

export async function loadSystemStatus(
  client: Client,
  echo: SystemStatusSource,
  signal?: AbortSignal,
): Promise<SystemStatusResult> {
  try {
    const result = await getSystemStatus({
      client,
      query: { echo },
      cache: "no-store",
      signal,
    });

    if (result.data !== undefined) {
      return {
        ok: true,
        data: result.data.data,
      };
    }

    return {
      ok: false,
      failure: normalizeApiFailure(result.error, result.response),
    };
  } catch (error) {
    return {
      ok: false,
      failure: normalizeApiFailure(error),
    };
  }
}

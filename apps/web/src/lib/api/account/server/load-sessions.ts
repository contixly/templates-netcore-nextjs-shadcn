import "server-only";

import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import {
  getAccountSessions,
  type GetAccountSessionsData,
} from "@/src/lib/api/generated";
import type { AccountSessionsResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

export type LoadSessionsQuery = Readonly<
  NonNullable<GetAccountSessionsData["query"]>
>;

export async function loadSessions(
  query: LoadSessionsQuery = {},
): Promise<AccountSessionsResult> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  try {
    const result = await getAccountSessions({
      client: client.client,
      cache: "no-store",
      headers: {
        "X-Template-Session-Renewal": "suppress",
      },
      query,
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

import "server-only";

import { cache } from "react";

import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getAccount } from "@/src/lib/api/generated";
import type { AccountResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

async function loadAccountUncached(): Promise<AccountResult> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  try {
    const result = await getAccount({
      client: client.client,
      cache: "no-store",
      headers: {
        "X-Template-Session-Renewal": "suppress",
      },
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

export const loadAccount = cache(loadAccountUncached);

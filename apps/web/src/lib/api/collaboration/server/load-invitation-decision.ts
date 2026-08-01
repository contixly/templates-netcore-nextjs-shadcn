import "server-only";

import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getInvitationDecision } from "@/src/lib/api/generated/sdk.gen";
import type { InvitationDecisionResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

export async function loadInvitationDecision(
  invitationId: string,
): Promise<ApiResult<InvitationDecisionResponse>> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  try {
    const result = await getInvitationDecision({
      client: client.client,
      cache: "no-store",
      headers: { "X-Template-Session-Renewal": "suppress" },
      path: { invitationId },
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

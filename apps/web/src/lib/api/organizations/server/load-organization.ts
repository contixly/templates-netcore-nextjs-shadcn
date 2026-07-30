import "server-only";

import { cache } from "react";

import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import { getOrganizationByKey } from "@/src/lib/api/generated/sdk.gen";
import type { OrganizationDetailResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

async function loadOrganizationUncached(
  organizationKey: string,
): Promise<ApiResult<OrganizationDetailResponse>> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  try {
    const result = await getOrganizationByKey({
      client: client.client,
      cache: "no-store",
      headers: {
        "X-Template-Session-Renewal": "suppress",
      },
      path: { organizationKey },
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

export const loadOrganization = cache(loadOrganizationUncached);

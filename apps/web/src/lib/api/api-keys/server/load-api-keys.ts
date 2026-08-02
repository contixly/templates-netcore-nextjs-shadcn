import "server-only";

import type { ApiKeyOwner } from "@/src/features/api-keys/api-key-routes";
import type { ApiKeyListQuery } from "@/src/lib/api/api-keys/browser/api-key-mutations";
import { normalizeApiFailure } from "@/src/lib/api/failures/normalize-api-failure";
import {
  listOrganizationApiKeys,
  listPersonalApiKeys,
} from "@/src/lib/api/generated/sdk.gen";
import type { ApiKeyPageResponse } from "@/src/lib/api/generated/types.gen";
import type { ApiResult } from "@/src/lib/api/result";
import { createServerApiClient } from "@/src/lib/api/server/client";
import { readForwardedApiHeaders } from "@/src/lib/api/server/request-headers";

export async function loadApiKeys(
  owner: ApiKeyOwner,
  query: ApiKeyListQuery = {},
): Promise<ApiResult<ApiKeyPageResponse>> {
  const client = createServerApiClient(await readForwardedApiHeaders());
  if (!client.ok) {
    return { ok: false, failure: client.failure };
  }

  try {
    const common = {
      client: client.client,
      cache: "no-store" as const,
      headers: { "X-Template-Session-Renewal": "suppress" },
      query,
    };
    const result =
      owner.kind === "personal"
        ? await listPersonalApiKeys(common)
        : await listOrganizationApiKeys({
            ...common,
            path: { organizationId: owner.organizationId },
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
